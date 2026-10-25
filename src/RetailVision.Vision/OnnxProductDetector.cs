using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using RetailVision.Core;

namespace RetailVision.Vision;

public sealed record ModelManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string ModelFile { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Version { get; init; } = "";
    public string Purpose { get; init; } = "sku-detection";
    public string Contract { get; init; } = "yolo26-xyxy-v1";
    public string Preprocessing { get; init; } = "rgb-nchw-letterbox114-scale255";
    public string Nms { get; init; } = "none";
    public string InputName { get; init; } = "images";
    public string OutputName { get; init; } = "output0";
    public int InputSize { get; init; } = 640;
    public int MaxDetections { get; init; } = 300;
    public List<string> Classes { get; init; } = [];
}

public sealed class OnnxProductDetector : IProductDetector, IDisposable
{
    private readonly InferenceSession session;
    public ModelManifest Manifest { get; }
    public OnnxProductDetector(string manifestPath, bool allowSyntheticTestModel = false)
    {
        var json = File.ReadAllBytes(manifestPath);
        if (json.Length > 128 * 1024) throw new InvalidDataException("Model manifest is too large.");
        Manifest = JsonSerializer.Deserialize<ModelManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Empty model manifest.");
        var m = Manifest;
        if (m.SchemaVersion != 1 || m.Contract != "yolo26-xyxy-v1" || m.Preprocessing != "rgb-nchw-letterbox114-scale255" || m.Nms != "none" ||
            m.InputSize != 640 || m.MaxDetections != 300 || string.IsNullOrWhiteSpace(m.Version) || m.Version.Length > 100 ||
            (m.Purpose != "sku-detection" && !(allowSyntheticTestModel && m.Purpose == "integration-test")) ||
            string.IsNullOrWhiteSpace(m.InputName) || string.IsNullOrWhiteSpace(m.OutputName) ||
            string.IsNullOrWhiteSpace(m.ModelFile) || m.ModelFile != Path.GetFileName(m.ModelFile) || !m.ModelFile.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ||
            m.Classes is null || m.Classes.Count is < 1 or > 500 || m.Classes.Any(s=>s is null || !Regex.IsMatch(s,"^[A-Z0-9][A-Z0-9._-]{0,63}$")) ||
            m.Classes.Distinct(StringComparer.Ordinal).Count() != m.Classes.Count || m.Sha256 is null || !Regex.IsMatch(m.Sha256,"^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("Unsupported or invalid model manifest.");
        var modelPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, m.ModelFile);
        if (new FileInfo(modelPath).Length > 512L * 1024 * 1024) throw new InvalidDataException("Model exceeds 512 MB.");
        var bytes = File.ReadAllBytes(modelPath);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(m.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Model SHA-256 mismatch.");
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
        session = new InferenceSession(bytes, options);
        try
        {
            if (session.InputMetadata.Count != 1 || session.OutputMetadata.Count != 1 ||
                !session.InputMetadata.TryGetValue(m.InputName, out var input) || input.ElementType != typeof(float) || !input.Dimensions.SequenceEqual(new[]{1,3,640,640}) ||
                !session.OutputMetadata.TryGetValue(m.OutputName, out var output) || output.ElementType != typeof(float) || !output.Dimensions.SequenceEqual(new[]{1,300,6}))
                throw new InvalidDataException("ONNX tensor names, types or static dimensions do not match the contract.");
            var metadata = session.ModelMetadata.CustomMetadataMap;
            if (metadata.GetValueOrDefault("retailvision.contract") != m.Contract || metadata.GetValueOrDefault("retailvision.purpose") != m.Purpose ||
                metadata.GetValueOrDefault("task") != "detect" || metadata.GetValueOrDefault("end2end") != "True" ||
                !(JsonSerializer.Deserialize<string[]>(metadata.GetValueOrDefault("retailvision.classes") ?? "[]") ?? []).SequenceEqual(m.Classes))
                throw new InvalidDataException("ONNX metadata or class mapping does not match the manifest. Use the verified export script.");
        }
        catch { session.Dispose(); throw; }
    }
    public async Task<DetectionResult> DetectAsync(DetectionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Image is null || request.FixtureId is not null) throw new ArgumentException("ONNX requires a prepared image, never a fixture ID.");
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() =>
        {
            var prepared = PhotoPreprocessor.ToTensor(request.Image);
            using var input = OrtValue.CreateTensorValueFromMemory(prepared.Tensor, new long[]{1,3,640,640});
            using var runOptions = new RunOptions();
            using var registration = cancellationToken.Register(() => runOptions.Terminate = true);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var outputs = session.Run(runOptions, new[] { Manifest.InputName }, new[] { input }, new[] { Manifest.OutputName });
                cancellationToken.ThrowIfCancellationRequested();
                return new DetectionResult(Manifest.Purpose == "integration-test" ? "TEST" : "REAL", Manifest.Version,
                    Parse(outputs[0].GetTensorDataAsSpan<float>(), prepared, Manifest.Classes), Manifest.Sha256);
            }
            catch (OnnxRuntimeException) when(cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        }, cancellationToken);
    }
    public static IReadOnlyList<Detection> Parse(ReadOnlySpan<float> output, Letterbox image, IReadOnlyList<string> classes)
    {
        if (output.Length != 300 * 6) throw new InvalidDataException("Unexpected output shape.");
        var detections = new List<Detection>();
        for(var i=0;i<300;i++)
        {
            var row=output.Slice(i*6,6);
            for(var j=0;j<6;j++) if(!float.IsFinite(row[j])) throw new InvalidDataException("Non-finite model output.");
            var score=row[4];
            if(score<0 || score>1) throw new InvalidDataException("Invalid model confidence.");
            if(score<.1f) continue; // Retain low-confidence candidates for manual review, independent of the confirmation threshold.
            var classIndex=(int)row[5];
            if(row[5]!=classIndex || classIndex<0 || classIndex>=classes.Count) throw new InvalidDataException("Model class index is outside the declared mapping.");
            var x1=Math.Clamp(((double)row[0]-image.Left)/image.ResizedWidth,0,1); var y1=Math.Clamp(((double)row[1]-image.Top)/image.ResizedHeight,0,1);
            var x2=Math.Clamp(((double)row[2]-image.Left)/image.ResizedWidth,0,1); var y2=Math.Clamp(((double)row[3]-image.Top)/image.ResizedHeight,0,1);
            if(x2<=x1 || y2<=y1) continue; // Boxes entirely in padding or outside the image have no visible area.
            detections.Add(new("onnx-"+i, classes[classIndex], score, 0, x1,y1,x2-x1,y2-y1,classIndex));
        }
        return detections;
    }
    public void Dispose() => session.Dispose();
}

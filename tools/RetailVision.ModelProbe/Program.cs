using System.Diagnostics;
using System.Text.Json;
using RetailVision.Core;
using RetailVision.Vision;

if (args.Length != 3)
    throw new ArgumentException("Usage: RetailVision.ModelProbe <manifest.json> <image-or-directory> <new-report.json>");
var output = Path.GetFullPath(args[2]);
if (File.Exists(output)) throw new IOException("Refusing to overwrite an existing report.");
var input = Path.GetFullPath(args[1]);
var paths = Directory.Exists(input)
    ? Directory.GetFiles(input).Where(p => new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(Path.GetExtension(p).ToLowerInvariant())).Order().ToArray()
    : [input];
if (paths.Length == 0) throw new InvalidDataException("No input images.");
using var detector = new OnnxProductDetector(args[0]);
var reports = new List<object>();
foreach (var path in paths)
{
    await using var source = File.OpenRead(path);
    var photo = await PhotoPreprocessor.PrepareAsync(source, default);
    var watch = Stopwatch.StartNew();
    var detection = await detector.DetectAsync(new DetectionRequest(Image: photo.Png));
    watch.Stop();
    if (detection.Mode != "REAL") throw new InvalidDataException("Expected a real SKU model.");
    reports.Add(new { image = Path.GetFileName(path), width = photo.Width, height = photo.Height,
        inferenceMs = watch.Elapsed.TotalMilliseconds, detection });
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
{
    kind = "native-inference-not-accuracy", model = detector.Manifest.Version,
    sha256 = detector.Manifest.Sha256, results = reports
}, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
Console.WriteLine($"Native C# inference completed for {reports.Count} images. Report: {output}");

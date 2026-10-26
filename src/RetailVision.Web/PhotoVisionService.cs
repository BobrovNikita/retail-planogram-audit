using RetailVision.Core;
using RetailVision.Vision;

namespace RetailVision.Web;

public sealed class VisionBusyException() : Exception("Анализ занят. Подождите завершения других проверок и повторите запрос.");
public sealed class VisionUnavailableException(string message) : Exception(message);

public sealed class PhotoVisionService : IDisposable
{
    private readonly OnnxProductDetector? detector;
    private readonly SemaphoreSlim inferenceGate = new(1);
    private readonly SemaphoreSlim imageGate = new(2);
    private int pending;
    public bool Ready => detector is not null;
    public string Message { get; }
    public IReadOnlyList<string> Skus => detector?.Manifest.Classes ?? [];
    public PhotoVisionService(IConfiguration config, ILogger<PhotoVisionService> logger)
    {
        var path = config["Vision:ManifestPath"];
        if(string.IsNullOrWhiteSpace(path)) { Message="Обученная модель SKU не подключена. Можно загрузить фото и настроить полки."; return; }
        try
        {
            detector=new OnnxProductDetector(Path.GetFullPath(path));
            Message="Подключена модель "+detector.Manifest.Version+" · CPU · "+
                (config["Vision:ModelNotice"] ?? "точность требует независимой проверки.");
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
        { Message="Модель недоступна: файл, контрольная сумма или контракт не прошли проверку. Подробности в журнале сервера.";logger.LogError(ex,"ONNX model configuration rejected"); }
    }
    // Explicit injection is only used by isolated integration tests; production always validates Purpose=sku-detection.
    public PhotoVisionService(OnnxProductDetector testDetector) { detector=testDetector; Message="TEST · синтетический ONNX-граф для интеграционной проверки, не обученная модель."; }
    public async Task<PreparedPhoto> PrepareAsync(Stream input,CancellationToken ct)
    {
        if(!await imageGate.WaitAsync(0,ct)) throw new InvalidDataException("Обработка фото занята. Повторите загрузку позже.");
        try {return await PhotoPreprocessor.PrepareAsync(input,ct);} finally {imageGate.Release();}
    }
    public async Task<DetectionResult> DetectAsync(Func<CancellationToken, Task<byte[]>> readImage,CancellationToken ct)
    {
        if(detector is null) throw new VisionUnavailableException(Message);
        if(Interlocked.Increment(ref pending)>3) {Interlocked.Decrement(ref pending);throw new VisionBusyException();}
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var entered=false;
        try
        {
            await inferenceGate.WaitAsync(timeout.Token); entered=true;
            var image = await readImage(timeout.Token);
            return await detector.DetectAsync(new DetectionRequest(Image:image),timeout.Token);
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) {throw new VisionUnavailableException("Анализ превысил 30 секунд. Попробуйте позже или проверьте модель.");}
        catch(Microsoft.ML.OnnxRuntime.OnnxRuntimeException) {throw new VisionUnavailableException("ONNX не смог обработать фото. Проверьте модель и повторите запрос.");}
        finally {if(entered)inferenceGate.Release();Interlocked.Decrement(ref pending);}
    }
    public void Dispose() {detector?.Dispose();inferenceGate.Dispose();imageGate.Dispose();}
}

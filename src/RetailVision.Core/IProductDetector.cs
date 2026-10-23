namespace RetailVision.Core;

public record DetectionRequest(string FixtureId);
public record DetectionResult(string Mode, string ModelVersion, IReadOnlyList<Detection> Products);

public interface IProductDetector
{
    Task<DetectionResult> DetectAsync(DetectionRequest request, CancellationToken cancellationToken = default);
}

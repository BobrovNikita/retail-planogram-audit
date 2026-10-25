namespace RetailVision.Core;

public record DetectionRequest(string? FixtureId = null, byte[]? Image = null);
public record DetectionResult(string Mode, string ModelVersion, IReadOnlyList<Detection> Products, string? ModelSha256 = null);

public interface IProductDetector
{
    Task<DetectionResult> DetectAsync(DetectionRequest request, CancellationToken cancellationToken = default);
}

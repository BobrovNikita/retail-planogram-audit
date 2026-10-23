using RetailVision.Core;

namespace RetailVision.Vision;

/// <summary>Only prepared illustrations; never accepts an arbitrary user photograph.</summary>
public sealed class FixtureProductDetector : IProductDetector
{
    public static readonly IReadOnlyList<ExpectedPosition> Expected = Array.AsReadOnly(new[]
    {
        new ExpectedPosition(1, 1, "RV-COLA"), new(1, 2, "RV-COLA"), new(1, 3, "RV-LIME"), new(1, 4, "RV-ORANGE"),
        new ExpectedPosition(2, 1, "RV-WATER"), new(2, 2, "RV-WATER"), new(2, 3, "RV-TEA"), new(2, 4, "RV-JUICE")
    });

    public Task<DetectionResult> DetectAsync(DetectionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.FixtureId is not ("correct" or "missing" or "sequence"))
            throw new ArgumentException("Unknown fixture.", nameof(request));
        var products = Expected.Select((e, i) => new Detection($"fixture-{i}", e.Sku, 1, e.Shelf,
            0.12 + (e.Position - 1) * 0.2, e.Shelf == 1 ? 0.12 : 0.58, 0.12, 0.30)).ToList();
        if (request.FixtureId == "missing") products.RemoveAt(2);
        if (request.FixtureId == "sequence")
            (products[2], products[3]) = (products[2] with { Sku = "RV-ORANGE" }, products[3] with { Sku = "RV-LIME" });
        return Task.FromResult(new DetectionResult("DEMO", "fixture-v1", products));
    }
}

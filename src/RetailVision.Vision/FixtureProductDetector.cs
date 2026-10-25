using RetailVision.Core;

namespace RetailVision.Vision;

/// <summary>Only prepared illustrations; never accepts an arbitrary user photograph.</summary>
public sealed class FixtureProductDetector : IProductDetector
{
    public static readonly IReadOnlyList<string> Scenarios = Array.AsReadOnly(new[]
        { "correct", "missing", "sequence", "replacement", "wrong-shelf", "extra", "uncertain" });
    // Frozen fixture labels are independent of the administrator's mutable catalog.
    public static IReadOnlyDictionary<string, Product> CreateProducts() => new[]
    {
        ("RV-COLA", "Cola Original", "#a24a43"), ("RV-LIME", "Lime Fresh", "#459d7a"),
        ("RV-ORANGE", "Orange Soda", "#e5a048"), ("RV-WATER", "Pure Water", "#619acb"),
        ("RV-TEA", "Green Tea", "#739660"), ("RV-JUICE", "Apple Juice", "#c1a54c")
    }.ToDictionary(p => p.Item1, p => new Product { Sku = p.Item1, Name = p.Item2, Color = p.Item3, Brand = "RetailVision", PackageSize = "500 мл" });

    public static readonly IReadOnlyList<ExpectedPosition> Expected = Array.AsReadOnly(new[]
    {
        new ExpectedPosition(1, 1, "RV-COLA", .18), new(1, 2, "RV-COLA", .38), new(1, 3, "RV-LIME", .58), new(1, 4, "RV-ORANGE", .78),
        new ExpectedPosition(2, 1, "RV-WATER", .18), new(2, 2, "RV-WATER", .38), new(2, 3, "RV-TEA", .58), new(2, 4, "RV-JUICE", .78)
    });

    public Task<DetectionResult> DetectAsync(DetectionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Image is not null) throw new ArgumentException("Fixture detector never accepts photographs.", nameof(request));
        if (!Scenarios.Contains(request.FixtureId, StringComparer.Ordinal))
            throw new ArgumentException("Unknown fixture.", nameof(request));
        var products = Expected.Select((e, i) => new Detection($"fixture-{i}", e.Sku, 1, e.Shelf,
            0.12 + (e.Position - 1) * 0.2, e.Shelf == 1 ? 0.12 : 0.58, 0.12, 0.30)).ToList();
        if (request.FixtureId == "missing") products.RemoveAt(2);
        if (request.FixtureId == "sequence")
            (products[2], products[3]) = (products[2] with { Sku = "RV-ORANGE" }, products[3] with { Sku = "RV-LIME" });
        if (request.FixtureId == "replacement") products[2] = products[2] with { Sku = "RV-WATER" };
        if (request.FixtureId == "wrong-shelf")
            (products[2], products[6]) = (products[2] with { Sku = "RV-TEA" }, products[6] with { Sku = "RV-LIME" });
        if (request.FixtureId == "extra") products.Add(new("fixture-extra", "RV-COLA", 1, 1, .28, .12, .05, .30));
        if (request.FixtureId == "uncertain") products[2] = products[2] with { Confidence = .2 };
        return Task.FromResult(new DetectionResult("DEMO", "fixture-v2", products));
    }
}

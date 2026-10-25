namespace RetailVision.Core;

// Short-lived private workspace; permanent audit history belongs to stage 5.
public sealed record PhotoAuditDraft
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string OwnerId { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddHours(24);
    public int Revision { get; init; } = 1;
    public int PlanogramId { get; init; }
    public int PlanogramVersion { get; init; }
    public PlanogramDocument Planogram { get; init; } = new();
    public Dictionary<string, string> ProductNames { get; init; } = [];
    public int Width { get; init; }
    public int Height { get; init; }
    public List<ShelfGeometry> Shelves { get; init; } = [];
    public double ConfidenceThreshold { get; init; } = .5;
    public DetectionResult? Detection { get; init; }
    public ComplianceResult? Compliance { get; init; }
}

public static class ShelfCalibration
{
    public static IReadOnlyList<string> Validate(PlanogramDocument plan, IReadOnlyList<ShelfGeometry>? bands)
    {
        if (bands is null || bands.Count != plan.Shelves.Count || bands.Any(b => b is null)) return ["Укажите границы каждой полки."];
        if (!bands.Select(b => b.Shelf).Order().SequenceEqual(plan.Shelves.Select(s => s.Number).Order())) return ["Номера полок не соответствуют планограмме."];
        static bool Unit(double x) => double.IsFinite(x) && x >= 0 && x <= 1;
        if (bands.Any(b => !Unit(b.Left) || !Unit(b.Right) || !Unit(b.Top) || !Unit(b.Bottom) || b.Right - b.Left < .01 || b.Bottom - b.Top < .01))
            return ["Границы должны лежать в пределах фото; ширина и высота полосы — не менее 1%."];
        for (var i = 0; i < bands.Count; i++) for (var j = i + 1; j < bands.Count; j++)
            if (Math.Min(bands[i].Right, bands[j].Right) > Math.Max(bands[i].Left, bands[j].Left) &&
                Math.Min(bands[i].Bottom, bands[j].Bottom) > Math.Max(bands[i].Top, bands[j].Top)) return ["Полосы полок не должны пересекаться."];
        return [];
    }
}

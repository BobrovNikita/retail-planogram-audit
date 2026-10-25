namespace RetailVision.Core;

public record ExpectedPosition(int Shelf, int Position, string Sku, double? CenterX = null);
public record Detection(string Id, string? Sku, double Confidence, int Shelf,
    double X, double Y, double Width, double Height, int? ClassIndex = null)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public double SupportX => CenterX;
    public double SupportY => Y + Height;
}
public enum ViolationKind { MissingProduct, WrongPosition, ExtraProduct, UnknownProduct, WrongShelf, WrongProduct }
public enum MatchingStrategy { Sequence, Positional }

/// <summary>Normalized shelf band. Assignment uses the box's bottom-center support point.</summary>
public record ShelfGeometry(int Shelf, double Left, double Right, double Top, double Bottom);
public sealed record ComplianceOptions
{
    public double ConfidenceThreshold { get; init; } = 0.5;
    public double PositionTolerance { get; init; } = 0.08;
    public double ShelfTolerance { get; init; } = 0.02;
    public MatchingStrategy Strategy { get; init; } = MatchingStrategy.Sequence;
    public IReadOnlySet<string>? KnownSkus { get; init; }
    public IReadOnlyList<ShelfGeometry> Shelves { get; init; } = [];
}

/// <summary>Every expected position and detection appears exactly once in the full match list.</summary>
public record PositionMatch(ExpectedPosition? Expected, Detection? Detection,
    ViolationKind? Kind, bool IsCertain, string Reason);
public record Violation(ViolationKind Kind, int Shelf, int? Position, string? Sku, string? DetectionId,
    string? ActualSku = null, int? ActualShelf = null, double? Confidence = null,
    bool IsCertain = true, string Reason = "");
public record FacingComparison(int Shelf, string Sku, int Expected, int Detected);
public record ComplianceMetrics(double PositionsPercent, double SequencePercent, double SkuPresencePercent,
    double FacingsPercent, int PresentSkus, int ExpectedSkus, int ConfirmedViolations, int ReviewCases);
public record ShelfStatistics(int Shelf, int Expected, int Detected, int Correct,
    double PositionsPercent, double FacingsPercent, int ConfirmedViolations, int ReviewCases);
public record ComplianceResult(double Percent, int Correct, int Expected, int Detected,
    IReadOnlyList<Violation> Violations, IReadOnlyList<string> MatchedDetectionIds,
    IReadOnlyList<PositionMatch> Matches, ComplianceMetrics Metrics,
    IReadOnlyList<ShelfStatistics> Shelves, IReadOnlyList<FacingComparison> Facings,
    MatchingStrategy Strategy, bool UsesPositionGeometry);

public interface IComplianceAnalyzer
{
    ComplianceResult Analyze(IReadOnlyList<ExpectedPosition> expected, IReadOnlyList<Detection> detections,
        double confidenceThreshold = 0.5);
    ComplianceResult Analyze(IReadOnlyList<ExpectedPosition> expected, IReadOnlyList<Detection> detections,
        ComplianceOptions options);
    ComplianceResult Analyze(PlanogramDocument planogram, IReadOnlyList<Detection> detections,
        ComplianceOptions? options = null);
}

public static class PlanogramPositions
{
    public static IReadOnlyList<ExpectedPosition> Expand(PlanogramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var skus = (document.Shelves ?? []).Where(s => s is not null).SelectMany(s => s.Slots ?? [])
            .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Sku)).Select(s => s.Sku).ToHashSet(StringComparer.Ordinal);
        var errors = PlanogramValidator.Validate(document, skus);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(document));
        var result = new List<ExpectedPosition>();
        foreach (var shelf in document.Shelves!.OrderBy(s => s.Number))
        {
            var position = 1;
            foreach (var slot in shelf.Slots.OrderBy(s => s.Position))
                for (var i = 0; i < slot.Facings; i++) result.Add(new(shelf.Number, position++, slot.Sku));
        }
        return result;
    }
}

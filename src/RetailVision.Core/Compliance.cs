namespace RetailVision.Core;

public record ExpectedPosition(int Shelf, int Position, string Sku);
public record Detection(string Id, string? Sku, double Confidence, int Shelf,
    double X, double Y, double Width, double Height);
public enum ViolationKind { MissingProduct, WrongPosition, ExtraProduct, UnknownProduct, WrongShelf }
public record Violation(ViolationKind Kind, int Shelf, int? Position, string? Sku, string? DetectionId);
public record ComplianceResult(double Percent, int Correct, int Expected, int Detected,
    IReadOnlyList<Violation> Violations, IReadOnlyList<string> MatchedDetectionIds);

public interface IComplianceAnalyzer
{
    ComplianceResult Analyze(IReadOnlyList<ExpectedPosition> expected, IReadOnlyList<Detection> detections,
        double confidenceThreshold = 0.5);
}

/// <summary>One-to-one LCS per shelf. Gaps do not shift every subsequent match.</summary>
public sealed class ComplianceAnalyzer : IComplianceAnalyzer
{
    public ComplianceResult Analyze(IReadOnlyList<ExpectedPosition> expected,
        IReadOnlyList<Detection> detections, double confidenceThreshold = 0.5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(confidenceThreshold, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(confidenceThreshold, 1);
        if (expected.Count > 500 || detections.Count > 500)
            throw new ArgumentException("At most 500 positions are supported.");
        if (expected.Any(e => e.Shelf < 1 || e.Position < 1 || string.IsNullOrWhiteSpace(e.Sku)) ||
            expected.Select(e => (e.Shelf, e.Position)).Distinct().Count() != expected.Count ||
            detections.Select(d => d.Id).Distinct().Count() != detections.Count)
            throw new ArgumentException("Positions and detection IDs must be valid and unique.");
        var violations = new List<Violation>();
        var matched = new HashSet<string>();
        var unmatchedExpected = new List<ExpectedPosition>();
        var unmatchedDetected = new List<Detection>();
        foreach (var shelf in expected.Select(e => e.Shelf).Union(detections.Select(d => d.Shelf)).Order())
        {
            var a = expected.Where(e => e.Shelf == shelf).OrderBy(e => e.Position).ToArray();
            var b = detections.Where(d => d.Shelf == shelf).OrderBy(d => d.X + d.Width / 2).ToArray();
            bool IsMatch(int i, int j) => b[j].Confidence >= confidenceThreshold && b[j].Sku == a[i].Sku;
            var dp = new int[a.Length + 1, b.Length + 1];
            for (var i = a.Length - 1; i >= 0; i--)
                for (var j = b.Length - 1; j >= 0; j--)
                    dp[i, j] = IsMatch(i, j) ? 1 + dp[i + 1, j + 1] : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            int x = 0, y = 0;
            while (x < a.Length || y < b.Length)
            {
                if (x < a.Length && y < b.Length && IsMatch(x, y))
                {
                    matched.Add(b[y++].Id);
                    x++;
                }
                else if (x < a.Length && (y == b.Length || dp[x + 1, y] >= dp[x, y + 1]))
                    unmatchedExpected.Add(a[x++]);
                else
                    unmatchedDetected.Add(b[y++]);
            }
        }
        foreach (var e in unmatchedExpected)
        {
            var candidate = unmatchedDetected.FirstOrDefault(d => d.Sku == e.Sku && d.Confidence >= confidenceThreshold);
            if (candidate is null)
                violations.Add(new(ViolationKind.MissingProduct, e.Shelf, e.Position, e.Sku, null));
            else
            {
                violations.Add(new(candidate.Shelf == e.Shelf ? ViolationKind.WrongPosition : ViolationKind.WrongShelf,
                    e.Shelf, e.Position, e.Sku, candidate.Id));
                unmatchedDetected.Remove(candidate);
            }
        }
        var known = expected.Select(e => e.Sku).ToHashSet();
        foreach (var d in unmatchedDetected)
            violations.Add(new(d.Sku is null || d.Confidence < confidenceThreshold || !known.Contains(d.Sku)
                ? ViolationKind.UnknownProduct : ViolationKind.ExtraProduct, d.Shelf, null, d.Sku, d.Id));
        var denominator = Math.Max(expected.Count, detections.Count);
        var percent = denominator == 0 ? 100 : Math.Round(100.0 * matched.Count / denominator, 1);
        return new(percent, matched.Count, expected.Count, detections.Count, violations, matched.Order().ToArray());
    }
}

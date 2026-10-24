using System.Text.Json;
using RetailVision.Core;

namespace RetailVision.Core.Tests;

public sealed class AdvancedComplianceTests
{
    private readonly ComplianceAnalyzer analyzer = new();
    private static ExpectedPosition E(int p, string sku, int shelf = 1, double? x = null) => new(shelf, p, sku, x);
    private static Detection D(string id, string? sku, double x = .2, int shelf = 1, double confidence = 1) => new(id, sku, confidence, shelf, x - .025, .1, .05, .2);
    private static ComplianceOptions Catalog(params string[] skus) => new() { KnownSkus = skus.ToHashSet(StringComparer.Ordinal) };

    [Fact]
    public void KnownReplacementIsOneConfirmedViolationWithBothSkus()
    {
        var r = analyzer.Analyze([E(1, "A"), E(2, "B"), E(3, "C")],
            [D("a", "A", .2), D("x", "X", .5), D("c", "C", .8)], Catalog("A", "B", "C", "X"));
        var issue = Assert.Single(r.Violations);
        Assert.Equal(ViolationKind.WrongProduct, issue.Kind);
        Assert.Equal("B", issue.Sku); Assert.Equal("X", issue.ActualSku);
        Assert.Equal(2, issue.Position); Assert.True(issue.IsCertain);
        Assert.Equal(66.7, r.Metrics.SkuPresencePercent);
        Assert.Equal(50, r.Metrics.FacingsPercent);
        Assert.Equal(3, r.Matches.Count);
    }
    [Fact]
    public void KnownUnplannedProductIsExtraWhileUnmappedLabelNeedsReview()
    {
        var r = analyzer.Analyze([E(1, "A")], [D("a", "A"), D("x", "X", .8)], Catalog("A", "X"));
        Assert.Equal(ViolationKind.ExtraProduct, Assert.Single(r.Violations).Kind);
        var unknown = analyzer.Analyze([E(1, "A")], [D("a", "A"), D("x", "X", .8)]);
        Assert.Equal(ViolationKind.UnknownProduct, Assert.Single(unknown.Violations).Kind);
        Assert.Equal(1, unknown.Metrics.ReviewCases);
    }
    [Fact]
    public void SameShelfPairsArePreferredOverCrossShelfPairs()
    {
        var r = analyzer.Analyze([E(1, "A", 1), E(1, "A", 2), E(2, "B", 2)],
            [D("b2", "B", .2, 2), D("a2", "A", .8, 2)]);
        Assert.Contains(r.Matches, m => m.Expected?.Shelf == 2 && m.Expected.Sku == "A" && m.Detection?.Id == "a2");
        Assert.DoesNotContain(r.Violations, v => v.Kind == ViolationKind.WrongShelf);
        Assert.Contains(r.Violations, v => v.Kind == ViolationKind.MissingProduct && v.Shelf == 1);
    }
    [Fact]
    public void PositionGeometryDistinguishesBothSidesOfSwap()
    {
        var r = analyzer.Analyze([E(1, "A", x: .2), E(2, "B", x: .8)], [D("b", "B", .2), D("a", "A", .8)]);
        Assert.Equal(0, r.Percent);
        Assert.Equal(50, r.Metrics.SequencePercent);
        Assert.Equal(100, r.Metrics.SkuPresencePercent);
        Assert.Equal(100, r.Metrics.FacingsPercent);
        Assert.Equal(2, r.Violations.Count);
        Assert.All(r.Violations, v => Assert.Equal(ViolationKind.WrongPosition, v.Kind));
    }
    [Theory]
    [InlineData(.05, 100)]
    [InlineData(.049, 0)]
    public void GeometryToleranceIncludesBoundary(double tolerance, double percent)
    {
        var r = analyzer.Analyze([E(1, "A", x: .2)], [D("a", "A", .25)], new ComplianceOptions { PositionTolerance = tolerance });
        Assert.Equal(percent, r.Percent);
        Assert.Equal(100, r.Metrics.SequencePercent);
    }
    [Fact]
    public void DuplicateSkuTieUsesClosestGeometryInsteadOfInputOrder()
    {
        var expected = new[] { E(1, "A", x: .2), E(2, "A", x: .8) };
        var detected = new[] { D("extra", "A", .6), D("right", "A", .8), D("left", "A", .2) };
        var r = analyzer.Analyze(expected, detected);
        Assert.Equal(2, r.Correct);
        Assert.Equal("extra", Assert.Single(r.Violations).DetectionId);
        Assert.Equal(JsonSerializer.Serialize(r), JsonSerializer.Serialize(analyzer.Analyze(expected.Reverse().ToArray(), detected.Reverse().ToArray())));
    }
    [Fact]
    public void UnknownAlignedObjectDoesNotAlsoProduceMissing()
    {
        var r = analyzer.Analyze([E(1, "A", x: .2)], [D("maybe", "A", confidence: .1)]);
        var issue = Assert.Single(r.Violations);
        Assert.Equal(ViolationKind.UnknownProduct, issue.Kind);
        Assert.False(issue.IsCertain);
        Assert.Equal(1, r.Metrics.ReviewCases);
        Assert.Equal(0, r.Metrics.ConfirmedViolations);
        Assert.NotNull(Assert.Single(r.Matches).Expected);
        Assert.Equal(0, r.Metrics.SkuPresencePercent);
    }
    [Fact]
    public void DistantUnknownIsNotAssignedAnInventedExpectedLocation()
    {
        var r = analyzer.Analyze([E(1, "A", x: .2)], [D("unknown", null, .8)]);
        Assert.Equal(2, r.Metrics.ReviewCases);
        Assert.All(r.Violations, v => Assert.False(v.IsCertain));
        Assert.Contains(r.Matches, m => m.Kind == ViolationKind.MissingProduct && m.Detection is null);
        Assert.Contains(r.Matches, m => m.Kind == ViolationKind.UnknownProduct && m.Expected is null);
    }
    [Fact]
    public void ConfidenceThresholdIsInclusive()
    {
        var r = analyzer.Analyze([E(1, "A")], [D("a", "A", confidence: .5)]);
        Assert.Equal(100, r.Percent);
        Assert.Equal(0, analyzer.Analyze([E(1, "A")], [D("a", "A", confidence: .499)]).Percent);
    }
    [Fact]
    public void ShelfAssignmentUsesBottomSupportPointInsteadOfCenterOrReportedShelf()
    {
        var detection = new Detection("tall", "A", 1, 1, .2, .2, .1, .5);
        var r = analyzer.Analyze([E(1, "A", 2)], [detection], new ComplianceOptions
        { Shelves = [new(1, 0, 1, .1, .48), new(2, 0, 1, .6, .9)], ShelfTolerance = 0 });
        Assert.Equal(100, r.Percent);
        Assert.Equal(2, Assert.Single(r.Matches).Detection!.Shelf);
    }
    [Fact]
    public void AmbiguousShelfRequiresReviewWithoutClaimingWrongShelf()
    {
        var r = analyzer.Analyze([E(1, "A")], [D("a", "A")], new ComplianceOptions
        { Shelves = [new(1, 0, 1, .1, .5), new(2, 0, 1, .2, .6)] });
        var issue = Assert.Single(r.Violations);
        Assert.Equal(ViolationKind.UnknownProduct, issue.Kind);
        Assert.Equal(0, issue.ActualShelf);
        Assert.Equal(100, r.Metrics.SkuPresencePercent);
        Assert.Equal(0, r.Percent);
    }
    [Fact]
    public void MissingItemDoesNotShiftLaterMatchesButPositionalBaselineDoes()
    {
        var a = new[] { E(1, "A"), E(2, "B"), E(3, "C"), E(4, "D") };
        var b = new[] { D("a", "A", .1), D("c", "C", .5), D("d", "D", .8) };
        var sequence = analyzer.Analyze(a, b);
        var positional = analyzer.Analyze(a, b, new ComplianceOptions { Strategy = MatchingStrategy.Positional });
        Assert.Equal(75, sequence.Percent);
        Assert.Equal(25, positional.Percent);
        Assert.Equal(75, positional.Metrics.SequencePercent);
        Assert.Single(sequence.Violations);
    }
    [Fact]
    public void PlanogramFacingsExpandIntoOrderedIndependentPositions()
    {
        var plan = new PlanogramDocument { Name = "Test", Shelves = [new() { Number = 1, Slots =
            [new() { Position = 1, Sku = "A", Facings = 2 }, new() { Position = 2, Sku = "B", Facings = 1 }] }] };
        var r = analyzer.Analyze(plan, [D("a1", "A", .1), D("a2", "A", .3), D("b", "B", .6)]);
        Assert.Equal(100, r.Percent);
        Assert.Equal(new[] { 1, 2, 3 }, r.Matches.Select(m => m.Expected!.Position));
        Assert.Equal(2, r.Facings.Single(f => f.Sku == "A").Expected);
        plan.Shelves[0].Slots[0].Facings = 0;
        Assert.Throws<ArgumentException>(() => PlanogramPositions.Expand(plan));
    }
    [Fact]
    public void ShelfAndFacingStatisticsExposeMissingAndExcessCounts()
    {
        var r = analyzer.Analyze([E(1, "A"), E(2, "A"), E(1, "B", 2)],
            [D("a", "A"), D("b1", "B", .2, 2), D("b2", "B", .8, 2)]);
        Assert.Equal(100, r.Metrics.SkuPresencePercent);
        Assert.Equal(50, r.Metrics.FacingsPercent);
        Assert.Equal(66.7, r.Percent);
        Assert.Equal((2, 1), (r.Facings[0].Expected, r.Facings[0].Detected));
        Assert.Equal((1, 2), (r.Facings[1].Expected, r.Facings[1].Detected));
        Assert.All(r.Shelves, s => Assert.Equal(50, s.PositionsPercent));
        Assert.Equal(r.Metrics.ConfirmedViolations, r.Shelves.Sum(s => s.ConfirmedViolations));
    }
    [Fact]
    public void EmptyExpectedHasDefinedInventoryMetrics()
    {
        var empty = analyzer.Analyze([], []);
        Assert.Equal(100, empty.Metrics.FacingsPercent);
        Assert.Equal(100, empty.Metrics.SkuPresencePercent);
        var extra = analyzer.Analyze([], [D("a", "A")], Catalog("A"));
        Assert.Equal(0, extra.Percent);
        Assert.Equal(0, extra.Metrics.FacingsPercent);
        Assert.Equal(100, extra.Metrics.SkuPresencePercent);
    }
    [Theory]
    [InlineData("nan-confidence")]
    [InlineData("infinity-coordinate")]
    [InlineData("out-of-image")]
    [InlineData("zero-width")]
    [InlineData("negative-shelf")]
    [InlineData("empty-id")]
    [InlineData("duplicate-id")]
    [InlineData("null-detection")]
    [InlineData("null-expected")]
    [InlineData("bad-center")]
    [InlineData("nan-threshold")]
    [InlineData("bad-tolerance")]
    [InlineData("unknown-strategy")]
    [InlineData("invalid-band")]
    [InlineData("duplicate-band")]
    [InlineData("missing-band")]
    [InlineData("catalog-missing-expected")]
    [InlineData("too-many")]
    public void InvalidInputsFailBeforeMatching(string issue)
    {
        var a = new List<ExpectedPosition> { E(1, "A") }; var b = new List<Detection> { D("a", "A") };
        var o = new ComplianceOptions();
        switch (issue)
        {
            case "nan-confidence": b[0] = b[0] with { Confidence = double.NaN }; break;
            case "infinity-coordinate": b[0] = b[0] with { X = double.PositiveInfinity }; break;
            case "out-of-image": b[0] = b[0] with { X = .99 }; break;
            case "zero-width": b[0] = b[0] with { Width = 0 }; break;
            case "negative-shelf": b[0] = b[0] with { Shelf = -1 }; break;
            case "empty-id": b[0] = b[0] with { Id = " " }; break;
            case "duplicate-id": b.Add(b[0]); break;
            case "null-detection": b[0] = null!; break;
            case "null-expected": a[0] = null!; break;
            case "bad-center": a[0] = a[0] with { CenterX = double.NaN }; break;
            case "nan-threshold": o = o with { ConfidenceThreshold = double.NaN }; break;
            case "bad-tolerance": o = o with { PositionTolerance = -1 }; break;
            case "unknown-strategy": o = o with { Strategy = (MatchingStrategy)42 }; break;
            case "invalid-band": o = o with { Shelves = [new(1, .8, .2, .1, .9)] }; break;
            case "duplicate-band": o = o with { Shelves = [new(1, 0, 1, .1, .5), new(1, 0, 1, .6, .9)] }; break;
            case "missing-band": o = o with { Shelves = [new(2, 0, 1, .1, .5)] }; break;
            case "catalog-missing-expected": o = Catalog("B"); break;
            case "too-many": a = Enumerable.Range(1, 501).Select(i => E(i, "A")).ToList(); break;
        }
        Assert.ThrowsAny<ArgumentException>(() => analyzer.Analyze(a, b, o));
    }
    [Fact]
    public void MaximumSupportedInputProducesUniqueMatches()
    {
        var a = Enumerable.Range(0, 500).Select(i => E(i + 1, "A", x: i / 500.0 + .00025)).ToArray();
        var b = Enumerable.Range(0, 500).Select(i => new Detection("d" + i, "A", 1, 1, i / 500.0, .1, .0005, .2)).ToArray();
        var r = analyzer.Analyze(a, b);
        Assert.Equal(500, r.Correct);
        Assert.Equal(500, r.Matches.Count);
        Assert.Equal(100, r.Percent);
        Assert.Empty(r.Violations);
    }
    [Fact]
    public void ExhaustiveShortSequencesPreserveOneToOneMappingAndLcsLength()
    {
        static IEnumerable<string[]> Words(string[] alphabet, int maxLength)
        {
            yield return [];
            if (maxLength == 0) yield break;
            foreach (var tail in Words(alphabet, maxLength - 1))
                foreach (var letter in alphabet) yield return [letter, .. tail];
        }
        static int Lcs(string[] a, string[] b) => a.Length == 0 || b.Length == 0 ? 0
            : a[0] == b[0] ? 1 + Lcs(a[1..], b[1..]) : Math.Max(Lcs(a[1..], b), Lcs(a, b[1..]));
        foreach (var a in Words(["A", "B"], 3))
            foreach (var b in Words(["A", "B", "?"], 3))
            {
                var r = analyzer.Analyze(a.Select((s, i) => E(i + 1, s)).ToArray(),
                    b.Select((s, i) => D("d" + i, s == "?" ? null : s, .2 + i * .2)).ToArray());
                Assert.Equal(a.Length, r.Matches.Count(m => m.Expected is not null));
                Assert.Equal(b.Length, r.Matches.Count(m => m.Detection is not null));
                Assert.Equal(b.Length, r.Matches.Where(m => m.Detection is not null).Select(m => m.Detection!.Id).Distinct().Count());
                Assert.Equal(Lcs(a, b), r.Correct);
                Assert.InRange(r.Percent, 0, 100);
                Assert.InRange(r.Metrics.FacingsPercent, 0, 100);
                Assert.Equal(r.Violations.Count, r.Metrics.ConfirmedViolations + r.Metrics.ReviewCases);
            }
    }
}

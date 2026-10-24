using RetailVision.Core;
using RetailVision.Vision;

namespace RetailVision.Core.Tests;

public sealed class ComplianceTests
{
    private readonly ComplianceAnalyzer analyzer = new();
    private static ExpectedPosition E(int p, string sku, int shelf = 1) => new(shelf, p, sku);
    private static Detection D(int p, string? sku, int shelf = 1, double confidence = 1) => new($"d-{shelf}-{p}", sku, confidence, shelf, p * .1, 0, .05, .2);

    [Theory]
    [InlineData("correct", 100, 0)]
    [InlineData("missing", 87.5, 1)]
    [InlineData("sequence", 75, 2)]
    [InlineData("replacement", 87.5, 1)]
    [InlineData("wrong-shelf", 75, 2)]
    [InlineData("extra", 88.9, 1)]
    [InlineData("uncertain", 87.5, 1)]
    public async Task FixturesUseRealAnalyzer(string scenario, double percent, int issues)
    {
        var detections = await new FixtureProductDetector().DetectAsync(new(scenario));
        var result = analyzer.Analyze(FixtureProductDetector.Expected, detections.Products);
        Assert.Equal("DEMO", detections.Mode);
        Assert.Equal(percent, result.Percent);
        Assert.Equal(issues, result.Violations.Count);
    }
    [Fact]
    public void GapDoesNotShiftRemainingMatches()
    {
        var result = analyzer.Analyze([E(1, "A"), E(2, "B"), E(3, "C")], [D(1, "A"), D(2, "C")]);
        Assert.Equal(2, result.Correct);
        Assert.Equal(ViolationKind.MissingProduct, Assert.Single(result.Violations).Kind);
    }
    [Fact]
    public void DuplicateSkusMatchOnceEach()
    {
        var result = analyzer.Analyze([E(1, "A"), E(2, "A"), E(3, "B")], [D(1, "A"), D(2, "B")]);
        Assert.Equal(2, result.Correct);
        Assert.Equal(2, result.MatchedDetectionIds.Distinct().Count());
        Assert.Single(result.Violations);
    }
    [Fact]
    public void ExtraFacingReducesCompliance()
    {
        var result = analyzer.Analyze([E(1, "A")], [D(1, "A"), D(2, "A")]);
        Assert.Equal(50, result.Percent);
        Assert.Equal(ViolationKind.ExtraProduct, Assert.Single(result.Violations).Kind);
    }
    [Fact]
    public void WrongShelfIsNotMatched()
    {
        var result = analyzer.Analyze([E(1, "A")], [D(1, "A", 2)]);
        Assert.Equal(0, result.Percent);
        Assert.Equal(ViolationKind.WrongShelf, Assert.Single(result.Violations).Kind);
    }
    [Theory]
    [InlineData("A", 0.2)]
    [InlineData(null, 1)]
    [InlineData("unknown", 1)]
    public void UncertainObjectsDoNotCountAsCorrect(string? sku, double confidence)
    {
        var result = analyzer.Analyze([E(1, "A")], [D(1, sku, confidence: confidence)]);
        Assert.Equal(0, result.Correct);
        Assert.Contains(result.Violations, v => v.Kind == ViolationKind.UnknownProduct);
        Assert.DoesNotContain(result.Violations, v => v.Kind == ViolationKind.MissingProduct);
        Assert.Equal(1, result.Metrics.ReviewCases);
        Assert.Equal(0, result.Metrics.ConfirmedViolations);
    }
    [Fact]
    public void EmptyShelfReportsEveryMissingPosition()
    {
        var result = analyzer.Analyze([E(1, "A"), E(2, "B")], []);
        Assert.Equal(0, result.Percent);
        Assert.Equal(2, result.Violations.Count);
        Assert.All(result.Violations, v => Assert.Null(v.DetectionId));
    }
    [Fact]
    public void EmptyPlanAndShelfAreDefinedAsComplete() => Assert.Equal(100, analyzer.Analyze([], []).Percent);
    [Fact]
    public void DuplicatePositionIsRejected() => Assert.Throws<ArgumentException>(() => analyzer.Analyze([E(1, "A"), E(1, "B")], []));
    [Fact]
    public async Task ArbitraryImageCannotBeSentToFixtureDetector() =>
        await Assert.ThrowsAsync<ArgumentException>(() => new FixtureProductDetector().DetectAsync(new("my-photo.jpg")));
}

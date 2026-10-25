using RetailVision.Core;
using RetailVision.Vision;

namespace RetailVision.Core.Tests;

public sealed class AuditReviewTests
{
    private static async Task<AuditSnapshot> Snapshot()
    {
        var result=await new FixtureProductDetector().DetectAsync(new DetectionRequest("uncertain"));
        var shelves=new ShelfGeometry[]{new(1,.05,.95,.05,.45),new(2,.05,.95,.50,.95)};
        var names=FixtureProductDetector.CreateProducts().ToDictionary(p=>p.Key,p=>p.Value.Name);
        var compliance=new ComplianceAnalyzer().Analyze(FixtureProductDetector.Expected,result.Products,new ComplianceOptions {Shelves=shelves,KnownSkus=names.Keys.ToHashSet()});
        return new(new PlanogramDocument(),FixtureProductDetector.Expected,names,shelves,.5,.02,800,500,result,compliance);
    }
    private static DetectionCorrection Change(Detection d,bool confirmed=false)=>new(d.Id,d.Sku,d.X,d.Y,d.Width,d.Height,confirmed);
    [Fact]
    public async Task ConfirmationRecalculatesWithoutChangingOriginalConfidence()
    {
        var s=await Snapshot();var current=s.OriginalDetection.Products.Select(d=>new ReviewedDetection(d)).ToArray();
        var edits=current.Select(c=>Change(c.Detection,c.Detection.Id=="fixture-2")).ToArray();
        var revised=AuditReview.Apply(s,current,edits);
        Assert.Equal(.2,revised.Single(c=>c.Detection.Id=="fixture-2").Detection.Confidence);
        Assert.Equal(100,AuditReview.Analyze(s,revised,new ComplianceAnalyzer()).Percent);
        Assert.Equal(87.5,s.OriginalResult.Percent);Assert.Equal(.2,s.OriginalDetection.Products[2].Confidence);
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown-sku")]
    [InlineData("outside")]
    [InlineData("nan")]
    [InlineData("zero")]
    [InlineData("unconfirmed")]
    [InlineData("bad-id")]
    [InlineData("too-many")]
    [InlineData("null")]
    public async Task InvalidCorrectionsFailBeforeAnalysis(string kind)
    {
        var s=await Snapshot();var current=s.OriginalDetection.Products.Select(d=>new ReviewedDetection(d)).ToArray();
        var d=Change(current[0].Detection,true);
        IReadOnlyList<DetectionCorrection>? edits=kind switch {
            "duplicate"=>[d,d],"unknown-sku"=>[d with{Sku="FORGED"}],"outside"=>[d with{X=.99}],
            "nan"=>[d with{Y=double.NaN}],"zero"=>[d with{Width=0}],"unconfirmed"=>[d with{Sku="RV-TEA",Confirmed=false}],
            "bad-id"=>[d with{Id="../../image"}],"too-many"=>Enumerable.Repeat(d,501).ToArray(),_=>null};
        Assert.Throws<ArgumentException>(()=>AuditReview.Apply(s,current,edits));
    }
    [Fact]
    public async Task AddedAndRelabelledObjectsCannotBeDemotedToModelPredictions()
    {
        var s=await Snapshot();var current=s.OriginalDetection.Products.Select(d=>new ReviewedDetection(d)).ToArray();
        var edit=Change(current[0].Detection,true) with {Sku="RV-TEA"};
        var changed=AuditReview.Apply(s,current,[edit]);
        Assert.Throws<ArgumentException>(()=>AuditReview.Apply(s,changed,[edit with{Confirmed=false}]));
        var added=edit with {Id="manual-"+Guid.NewGuid().ToString("N")};
        var manual=AuditReview.Apply(s,current,[added]);Assert.Equal(0,manual[0].Detection.Confidence);Assert.True(manual[0].ConfirmedByHuman);
        Assert.Throws<ArgumentException>(()=>AuditReview.Apply(s,manual,[added with{Confirmed=false}]));
        Assert.Empty(AuditReview.Apply(s,current,[]));
    }
}

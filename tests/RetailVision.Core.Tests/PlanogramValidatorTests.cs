using RetailVision.Core;

namespace RetailVision.Core.Tests;

public sealed class PlanogramValidatorTests
{
    private static readonly HashSet<string> Skus = ["SKU"];
    private static PlanogramDocument Valid() => new() { Name = "Test", Shelves = [new() { Number = 1, Slots = [new() { Position = 1, Sku = "SKU", Facings = 2 }] }] };

    [Fact]
    public void ValidAndEmptyShelfAreAllowed()
    {
        var d = Valid();
        Assert.Empty(PlanogramValidator.Validate(d, Skus));
        d.Shelves[0].Slots.Clear();
        Assert.Empty(PlanogramValidator.Validate(d, Skus));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("null-shelves")]
    [InlineData("null-shelf")]
    [InlineData("null-slots")]
    [InlineData("null-slot")]
    [InlineData("empty-name")]
    [InlineData("long-name")]
    [InlineData("null-store")]
    [InlineData("null-description")]
    [InlineData("schema")]
    [InlineData("no-shelves")]
    [InlineData("duplicate-shelf")]
    [InlineData("shelf-gap")]
    [InlineData("duplicate-position")]
    [InlineData("position-gap")]
    [InlineData("unknown-sku")]
    [InlineData("zero-facings")]
    [InlineData("too-many-facings")]
    [InlineData("total-limit")]
    public void RejectsInvalidDocumentsWithoutThrowing(string issue)
    {
        PlanogramDocument? d = Valid();
        switch (issue)
        {
            case "null": d = null; break;
            case "null-shelves": d.Shelves = null!; break;
            case "null-shelf": d.Shelves.Add(null!); break;
            case "null-slots": d.Shelves[0].Slots = null!; break;
            case "null-slot": d.Shelves[0].Slots.Add(null!); break;
            case "empty-name": d.Name = " "; break;
            case "long-name": d.Name = new('x', 121); break;
            case "null-store": d.Store = null!; break;
            case "null-description": d.Description = null!; break;
            case "schema": d.SchemaVersion = 2; break;
            case "no-shelves": d.Shelves.Clear(); break;
            case "duplicate-shelf": d.Shelves.Add(new() { Number = 1 }); break;
            case "shelf-gap": d.Shelves[0].Number = 2; break;
            case "duplicate-position": d.Shelves[0].Slots.Add(new() { Position = 1, Sku = "SKU" }); break;
            case "position-gap": d.Shelves[0].Slots[0].Position = 2; break;
            case "unknown-sku": d.Shelves[0].Slots[0].Sku = "MISSING"; break;
            case "zero-facings": d.Shelves[0].Slots[0].Facings = 0; break;
            case "too-many-facings": d.Shelves[0].Slots[0].Facings = 101; break;
            case "total-limit": d.Shelves[0].Slots = Enumerable.Range(1, 6).Select(i => new SlotDocument { Position = i, Sku = "SKU", Facings = 100 }).ToList(); break;
        }
        Assert.NotEmpty(PlanogramValidator.Validate(d, Skus));
    }
}

using RetailVision.Core;

namespace RetailVision.Web.Models;

public record PlanogramEditorModel(int Id, int Version, PlanogramDocument Document, IReadOnlyList<Product> Products);
public sealed class PlanogramSaveRequest
{
    public int Id { get; set; }
    public int Version { get; set; }
    public PlanogramDocument? Document { get; set; }
}
public sealed class PlanogramImportModel
{
    public IFormFile? File { get; set; }
}

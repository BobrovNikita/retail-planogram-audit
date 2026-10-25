using System.ComponentModel.DataAnnotations;
using RetailVision.Core;

namespace RetailVision.Web.Models;

public sealed class PhotoUploadModel
{
    [Range(1, int.MaxValue)] public int PlanogramId { get; set; }
    public IFormFile? Photo { get; set; }
    public List<Planogram> Planograms { get; set; } = [];
    public List<PhotoAuditDraft> Drafts { get; set; } = [];
}
public record PhotoPageModel(PhotoAuditDraft Draft, bool ModelReady, string ModelMessage);
public sealed record CalibrationRequest(int Revision, List<ShelfGeometry>? Shelves, double ConfidenceThreshold = .5);

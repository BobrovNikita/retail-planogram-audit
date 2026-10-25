using System.ComponentModel.DataAnnotations;
using RetailVision.Core;

namespace RetailVision.Web.Models;
public sealed class HistoryFilter
{
    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }
    public string? Mode { get; set; } = "";
    [StringLength(120)] public string? Search { get; set; }
    public string? State { get; set; } = "";
    public int Page { get; set; } = 1;
}
public record HistoryPage(HistoryFilter Filter,IReadOnlyList<Audit> Audits,int Total);
public record AuditReport(Audit Audit,AuditSnapshot Snapshot,IReadOnlyList<ReviewedDetection> Current,ComplianceResult Result);
public record AuditDay(DateTime Date,int Count,double Average);
public record AnalyticsPage(HistoryFilter Filter,int Count,double? Average,int Violations,int Review,int Corrected,IReadOnlyList<AuditDay> Days);
public record AuditComparison(AuditReport Left,AuditReport Right,bool SameContext);
public record ReviewRequest(int Revision,string? Reason,List<DetectionCorrection>? Detections,bool Restore = false);

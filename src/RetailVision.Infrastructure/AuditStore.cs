using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RetailVision.Core;

namespace RetailVision.Infrastructure;

public sealed class AuditConflictException() : Exception("Проверка изменена в другой вкладке. Обновите страницу перед исправлением.");

public sealed class AuditStore(RetailVisionDbContext db, IComplianceAnalyzer analyzer, string dataDirectory)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string directory = Path.Combine(Path.GetFullPath(dataDirectory),"audit-photos");
    public static AuditSnapshot Snapshot(Audit audit) => JsonSerializer.Deserialize<AuditSnapshot>(audit.SnapshotJson,Json)!;
    public static IReadOnlyList<ReviewedDetection> Current(Audit audit) => JsonSerializer.Deserialize<List<ReviewedDetection>>(audit.CurrentDetectionsJson,Json)!;
    public static ComplianceResult Result(Audit audit) => JsonSerializer.Deserialize<ComplianceResult>(audit.ResultJson,Json)!;
    public IQueryable<Audit> Query(string owner, bool includeArchived = false) => db.Audits.AsNoTracking().Where(a=>a.OwnerId==owner && a.SnapshotJson!="{}" && (includeArchived || a.ArchivedAtUtc==null));
    public Task<Audit?> ReadAsync(int id,string owner,CancellationToken ct) => Query(owner,true).Include(a=>a.Corrections).SingleOrDefaultAsync(a=>a.Id==id,ct);
    public async Task<byte[]?> ImageAsync(Audit audit,CancellationToken ct)
    {
        if(audit.PhotoPath is null || !Regex.IsMatch(audit.PhotoPath,"^[a-f0-9]{32}\\.png$")) return null;
        var path=Path.Combine(directory,audit.PhotoPath);
        return File.Exists(path)?await File.ReadAllBytesAsync(path,ct):null;
    }
    public async Task<int> SaveAsync(string owner,string source,int? planogramId,int planogramVersion,AuditSnapshot snapshot,byte[]? photo,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(owner) || snapshot.OriginalDetection.Mode is not ("REAL" or "DEMO")) throw new ArgumentException("Сохраняются только REAL или явно обозначенные DEMO-проверки.");
        var existing=await Query(owner,true).Where(a=>a.SourceKey==source).Select(a=>(int?)a.Id).SingleOrDefaultAsync(ct);
        if(existing.HasValue)return existing.Value;
        if(snapshot.OriginalDetection.Mode=="REAL" && (photo is null || photo.Length==0 || photo.Length>32*1024*1024))throw new ArgumentException("Для REAL-проверки требуется фотография.");
        var current=snapshot.OriginalDetection.Products.Select(d=>new ReviewedDetection(d)).ToArray();
        var result=AuditReview.Analyze(snapshot,current,analyzer);
        var model=await db.ModelVersions.FirstOrDefaultAsync(m=>m.Name==snapshot.OriginalDetection.ModelVersion && m.Mode==snapshot.OriginalDetection.Mode,ct)
            ?? new ModelVersion {Name=snapshot.OriginalDetection.ModelVersion,Mode=snapshot.OriginalDetection.Mode};
        var audit=new Audit {OwnerId=owner,SourceKey=source,PlanogramId=planogramId,PlanogramVersion=planogramVersion,
            PlanogramName=snapshot.Planogram.Name,Store=snapshot.Planogram.Store,ModelVersion=model,
            ModelName=snapshot.OriginalDetection.ModelVersion,ModelSha256=snapshot.OriginalDetection.ModelSha256,
            Mode=snapshot.OriginalDetection.Mode,PlanogramSnapshotJson=JsonSerializer.Serialize(snapshot.Planogram,Json),
            SnapshotJson=JsonSerializer.Serialize(snapshot,Json),OriginalPercent=result.Percent};
        SetResult(audit,current,result);
        string? path=null;
        try
        {
            if(photo is not null)
            {
                Directory.CreateDirectory(directory);audit.PhotoPath=Guid.NewGuid().ToString("N")+".png";path=Path.Combine(directory,audit.PhotoPath);
                await File.WriteAllBytesAsync(path,photo,ct);
            }
            db.Audits.Add(audit);await db.SaveChangesAsync(ct);return audit.Id;
        }
        catch(DbUpdateException)
        {
            if(path is not null)File.Delete(path);db.ChangeTracker.Clear();
            var duplicate=await Query(owner,true).Where(a=>a.SourceKey==source).Select(a=>(int?)a.Id).SingleOrDefaultAsync(ct);
            if(duplicate.HasValue)return duplicate.Value;throw;
        }
        catch {if(path is not null)File.Delete(path);throw;}
    }
    public async Task CorrectAsync(int id,string owner,int revision,string reason,IReadOnlyList<DetectionCorrection>? changes,bool restore,CancellationToken ct)
    {
        var audit=await db.Audits.Include(a=>a.Detections).Include(a=>a.Violations).AsSplitQuery().SingleOrDefaultAsync(a=>a.Id==id && a.OwnerId==owner && a.ArchivedAtUtc==null,ct)
            ?? throw new KeyNotFoundException();
        if(audit.Revision!=revision)throw new AuditConflictException();
        if(string.IsNullOrWhiteSpace(reason) || reason.Trim().Length>1000)throw new ArgumentException("Укажите причину исправления (до 1000 символов).");
        var snapshot=Snapshot(audit);
        var current=restore?snapshot.OriginalDetection.Products.Select(d=>new ReviewedDetection(d)).ToArray():AuditReview.Apply(snapshot,Current(audit),changes);
        var result=AuditReview.Analyze(snapshot,current,analyzer);
        var before=audit.CurrentDetectionsJson;var beforeResult=Result(audit);
        audit.Revision++;audit.HasCorrections=true;
        audit.Corrections.Add(new AuditCorrection {ActorId=owner,Reason=reason.Trim(),Revision=audit.Revision,
            ChangesJson=JsonSerializer.Serialize(new {restore,before=JsonSerializer.Deserialize<JsonElement>(before),after=current,beforeResult,afterResult=result},Json)});
        SetResult(audit,current,result);
        try {await db.SaveChangesAsync(ct);} catch(DbUpdateConcurrencyException){throw new AuditConflictException();}
    }
    private static void SetResult(Audit audit,IReadOnlyList<ReviewedDetection> current,ComplianceResult result)
    {
        audit.CurrentDetectionsJson=JsonSerializer.Serialize(current,Json);audit.ResultJson=JsonSerializer.Serialize(result,Json);
        audit.CompliancePercent=result.Percent;audit.ConfirmedViolations=result.Metrics.ConfirmedViolations;
        audit.ReviewCases=result.Metrics.ReviewCases;audit.DetectionCount=result.Detected;
        audit.Detections.Clear();audit.Violations.Clear();
        foreach(var r in current) {var d=r.Detection;var assigned=result.Matches.Single(m=>m.Detection?.Id==d.Id).Detection!;
            audit.Detections.Add(new DetectedProduct {Sku=d.Sku,Confidence=d.Confidence,X=d.X,Y=d.Y,Width=d.Width,Height=d.Height,Shelf=assigned.Shelf});}
        foreach(var v in result.Violations)audit.Violations.Add(new AuditViolation {Type=v.Kind.ToString(),Description=v.Reason});
    }
}

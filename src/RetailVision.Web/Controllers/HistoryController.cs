using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RetailVision.Core;
using RetailVision.Infrastructure;
using RetailVision.Vision;
using RetailVision.Web.Models;

namespace RetailVision.Web.Controllers;

[Authorize(Policy="Admin"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),EnableRateLimiting("photo")]
public sealed class HistoryController(AuditStore store,PhotoDraftStore drafts,IProductDetector detector,IComplianceAnalyzer analyzer, AuditRetention retention, RetentionPolicy retentionPolicy) : Controller
{
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static AuditReport Report(Audit audit) => new(audit,AuditStore.Snapshot(audit),AuditStore.Current(audit),AuditStore.Result(audit));
    [HttpGet]
    public async Task<IActionResult> Trash(int page = 1, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 100000);
        var query = store.Query(Owner, true).Where(a => a.ArchivedAtUtc != null);
        return View(new TrashPage(await query.OrderByDescending(a => a.ArchivedAtUtc).ThenByDescending(a => a.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct), await query.CountAsync(ct), page, retentionPolicy));
    }
    [HttpPost]
    public Task<IActionResult> Archive(int id, int revision, CancellationToken ct) => ArchiveChange(id, revision, true, ct);
    [HttpPost]
    public Task<IActionResult> Unarchive(int id, int revision, CancellationToken ct) => ArchiveChange(id, revision, false, ct);
    private async Task<IActionResult> ArchiveChange(int id, int revision, bool archived, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        try { await retention.SetArchivedAsync(id, Owner, revision, archived, ct); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (AuditConflictException ex) { return Conflict(ex.Message); }
        TempData["Notice"] = archived ? "Проверка перемещена в корзину. Её можно восстановить." : "Проверка восстановлена вместе с фотографией и исправлениями.";
        return archived ? RedirectToAction(nameof(Trash)) : RedirectToAction(nameof(Details), new { id });
    }
    [HttpPost]
    public async Task<IActionResult> Purge(int id, int revision, bool confirm, CancellationToken ct)
    {
        if (!ModelState.IsValid || !confirm) return BadRequest("Подтвердите безвозвратное удаление.");
        try { await retention.PurgeAsync(id, Owner, revision, ct); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (AuditConflictException ex) { return Conflict(ex.Message); }
        catch (ArgumentException ex) { return UnprocessableEntity(ex.Message); }
        TempData["Notice"] = "Отчёт удалён безвозвратно. Его фотография удалена либо поставлена в очередь удаления; резервные копии хранятся отдельно.";
        return RedirectToAction(nameof(Trash));
    }
    private IQueryable<Audit> Filter(HistoryFilter filter)
    {
        filter.Mode??="";filter.State??="";
        if(filter.From?.Year is < 2000 or > 2100 || filter.To?.Year is < 2000 or > 2100 || filter.From>filter.To)
            ModelState.AddModelError("From","Проверьте диапазон дат (2000–2100, начало не позже конца).");
        if(filter.Mode is not ("" or "REAL" or "DEMO"))ModelState.AddModelError("Mode","Неизвестный режим.");
        if(filter.State is not ("" or "corrected" or "original"))ModelState.AddModelError("State","Неизвестный статус.");
        filter.Page=Math.Clamp(filter.Page,1,100000);
        var query=store.Query(Owner);
        if(!ModelState.IsValid)return query.Where(_=>false);
        if(filter.From.HasValue) {var start=filter.From.Value.Date;query=query.Where(a=>a.CreatedAtUtc>=start);}
        if(filter.To.HasValue) {var end=filter.To.Value.Date.AddDays(1);query=query.Where(a=>a.CreatedAtUtc<end);}
        if(filter.Mode!="")query=query.Where(a=>a.Mode==filter.Mode);
        if(!string.IsNullOrWhiteSpace(filter.Search)) {var search=filter.Search.Trim();query=query.Where(a=>a.PlanogramName.Contains(search)||a.Store.Contains(search));}
        if(filter.State!="")query=query.Where(a=>a.HasCorrections==(filter.State=="corrected"));
        return query;
    }
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] HistoryFilter filter,CancellationToken ct)
    {
        var query=Filter(filter);var total=await query.CountAsync(ct);
        if(!ModelState.IsValid)Response.StatusCode=422;
        return View(new HistoryPage(filter,await query.OrderByDescending(a=>a.CreatedAtUtc).ThenByDescending(a=>a.Id).Skip((filter.Page-1)*20).Take(20).ToListAsync(ct),total));
    }
    [HttpGet]
    public async Task<IActionResult> Analytics([FromQuery] HistoryFilter filter,CancellationToken ct)
    {
        if(!Request.Query.ContainsKey("Mode"))filter.Mode="REAL";
        var query=Filter(filter);if(!ModelState.IsValid)Response.StatusCode=422;
        var count=await query.CountAsync(ct);var average=await query.Select(a=>(double?)a.CompliancePercent).AverageAsync(ct);
        var days=await query.GroupBy(a=>a.CreatedAtUtc.Date).Select(g=>new {Date=g.Key,Count=g.Count(),Average=g.Average(a=>a.CompliancePercent)})
            .OrderByDescending(d=>d.Date).Take(60).ToListAsync(ct);
        return View(new AnalyticsPage(filter,count,average,await query.SumAsync(a=>a.ConfirmedViolations,ct),await query.SumAsync(a=>a.ReviewCases,ct),await query.CountAsync(a=>a.HasCorrections,ct),days.OrderBy(d=>d.Date).Select(d=>new AuditDay(d.Date,d.Count,d.Average)).ToArray()));
    }
    [HttpGet]
    public async Task<IActionResult> Details(int id,CancellationToken ct)
    {
        var audit=await store.ReadAsync(id,Owner,ct);return audit is null?NotFound():View(Report(audit));
    }
    [HttpGet]
    public async Task<IActionResult> Print(int id,CancellationToken ct)
    {
        var audit=await store.ReadAsync(id,Owner,ct);return audit is null?NotFound():View(Report(audit));
    }
    [HttpGet]
    public async Task<IActionResult> Image(int id,CancellationToken ct)
    {
        var audit=await store.ReadAsync(id,Owner,ct);if(audit is null)return NotFound();
        var bytes=await store.ImageAsync(audit,ct);return bytes is null?NotFound():File(bytes,"image/png");
    }
    [HttpGet]
    public async Task<IActionResult> Export(int id,CancellationToken ct)
    {
        var audit=await store.ReadAsync(id,Owner,ct);if(audit is null)return NotFound();
        return File(JsonSerializer.SerializeToUtf8Bytes(new {schemaVersion=1,audit.Id,audit.CreatedAtUtc,audit.Mode,audit.Revision,
            audit.PlanogramVersion,audit.ModelName,audit.ModelSha256,snapshot=AuditStore.Snapshot(audit),current=AuditStore.Current(audit),result=AuditStore.Result(audit),
            imageUrl=audit.PhotoPath is null?null:Url.Action(nameof(Image),new{id}),
            corrections=audit.Corrections.OrderBy(c=>c.Revision).Select(c=>new {c.Revision,c.CreatedAtUtc,c.ActorId,c.Reason,changes=JsonSerializer.Deserialize<JsonElement>(c.ChangesJson)})},
            new JsonSerializerOptions(AuditStore.Json){WriteIndented=true}),"application/json",$"retailvision-audit-{id}-r{audit.Revision}.json");
    }
    [HttpGet]
    public async Task<IActionResult> Compare([FromQuery] int[] ids,CancellationToken ct)
    {
        if(ids.Length!=2 || ids[0]==ids[1]){TempData["Notice"]="Отметьте две разные проверки для сравнения.";return RedirectToAction(nameof(Index));}
        var a=await store.ReadAsync(ids[0],Owner,ct);var b=await store.ReadAsync(ids[1],Owner,ct);
        if(a is null || b is null)return NotFound();
        return View(new AuditComparison(Report(a),Report(b),a.Mode==b.Mode && a.PlanogramSnapshotJson==b.PlanogramSnapshotJson && a.ModelName==b.ModelName && a.ModelSha256==b.ModelSha256
            && AuditStore.Snapshot(a).ConfidenceThreshold==AuditStore.Snapshot(b).ConfidenceThreshold
            && AuditStore.Snapshot(a).ShelfTolerance==AuditStore.Snapshot(b).ShelfTolerance
            && JsonSerializer.Serialize(AuditStore.Snapshot(a).Expected,AuditStore.Json)==JsonSerializer.Serialize(AuditStore.Snapshot(b).Expected,AuditStore.Json)));
    }
    [HttpPost]
    public async Task<IActionResult> SavePhoto(string id,int revision,CancellationToken ct)
    {
        var draft=await drafts.ReadAsync(id,Owner,ct);if(draft is null)return NotFound();
        if(draft.Revision!=revision)return Conflict("Черновик изменился. Обновите страницу.");
        if(draft.Detection?.Mode!="REAL" || draft.Compliance is null)return UnprocessableEntity("Сначала выполните анализ с обученной SKU-моделью.");
        var photo=await drafts.ImageAsync(id,Owner,ct);if(photo is null)return NotFound();
        var snapshot=new AuditSnapshot(draft.Planogram,PlanogramPositions.Expand(draft.Planogram),draft.ProductNames,draft.Shelves,
            draft.ConfidenceThreshold,0,draft.Width,draft.Height,draft.Detection,draft.Compliance);
        var saved=await store.SaveAsync(Owner,$"photo:{id}:{revision}",draft.PlanogramId,draft.PlanogramVersion,snapshot,photo,ct);
        return RedirectToAction(nameof(Details),new{id=saved});
    }
    [HttpPost]
    public async Task<IActionResult> SaveDemo(string scenario,string key,CancellationToken ct)
    {
        if(!FixtureProductDetector.Scenarios.Contains(scenario) || !Guid.TryParseExact(key,"N",out _))return BadRequest();
        var detection=await detector.DetectAsync(new DetectionRequest(scenario),ct);
        var products=FixtureProductDetector.CreateProducts();
        var plan=new PlanogramDocument {Name="Учебная витрина",Store="DEMO",Shelves=FixtureProductDetector.Expected.GroupBy(e=>e.Shelf)
            .Select(g=>new ShelfDocument {Number=g.Key,Slots=g.Select(e=>new SlotDocument {Position=e.Position,Sku=e.Sku}).ToList()}).ToList()};
        var bands=new ShelfGeometry[]{new(1,.05,.95,.05,.45),new(2,.05,.95,.50,.95)};
        var result=analyzer.Analyze(FixtureProductDetector.Expected,detection.Products,new ComplianceOptions {Shelves=bands,KnownSkus=products.Keys.ToHashSet(StringComparer.Ordinal)});
        var snapshot=new AuditSnapshot(plan,FixtureProductDetector.Expected,products.ToDictionary(p=>p.Key,p=>p.Value.Name),bands,.5,.02,800,500,detection,result,scenario);
        var saved=await store.SaveAsync(Owner,"demo:"+key,null,1,snapshot,null,ct);
        return RedirectToAction(nameof(Details),new{id=saved});
    }
    [HttpGet]
    public async Task<IActionResult> Edit(int id,CancellationToken ct)
    {
        var audit=await store.ReadAsync(id,Owner,ct);return audit is null || audit.ArchivedAtUtc is not null?NotFound():View(Report(audit));
    }
    [HttpGet]
    public async Task<IActionResult> ReviewData(int id,CancellationToken ct)
    {
        var audit=await store.ReadAsync(id,Owner,ct);if(audit is null || audit.ArchivedAtUtc is not null)return NotFound();
        return Json(new {audit.Revision,snapshot=AuditStore.Snapshot(audit),current=AuditStore.Current(audit)});
    }
    [HttpPost,RequestSizeLimit(256*1024)]
    public async Task<IActionResult> Correct(int id,[FromBody] ReviewRequest? request,CancellationToken ct)
    {
        if(!ModelState.IsValid || request is null)return UnprocessableEntity(new {error="Проверьте данные исправления."});
        try {await store.CorrectAsync(id,Owner,request.Revision,request.Reason??"",request.Detections,request.Restore,ct);return Json(new {url=Url.Action(nameof(Details),new{id})});}
        catch(KeyNotFoundException){return NotFound();}
        catch(AuditConflictException ex){return Conflict(new {error=ex.Message});}
        catch(ArgumentException ex){return UnprocessableEntity(new {error=ex.Message});}
    }
}

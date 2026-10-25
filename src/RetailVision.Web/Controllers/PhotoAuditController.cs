using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RetailVision.Core;
using RetailVision.Infrastructure;
using RetailVision.Vision;
using RetailVision.Web.Models;

namespace RetailVision.Web.Controllers;

[Authorize(Policy = "Admin"), EnableRateLimiting("photo"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PhotoAuditController(PlanogramStore plans, RetailVisionDbContext db, PhotoDraftStore drafts,
    PhotoVisionService vision, IComplianceAnalyzer analyzer) : Controller
{
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(new PhotoUploadModel { Planograms = await plans.Query().AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct), Drafts = await drafts.ListAsync(Owner,ct) });

    [HttpPost, RequestSizeLimit(9 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 9 * 1024 * 1024)]
    public async Task<IActionResult> Upload(PhotoUploadModel model, CancellationToken ct)
    {
        var plan = await plans.Query().AsNoTracking().SingleOrDefaultAsync(p => p.Id == model.PlanogramId, ct);
        if (plan is null) ModelState.AddModelError("PlanogramId", "Выберите существующую планограмму.");
        if (model.Photo is null || model.Photo.Length is <= 0 or > PhotoPreprocessor.MaxBytes) ModelState.AddModelError("Photo", "Выберите фото JPEG, PNG или WebP размером до 8 МБ.");
        if (ModelState.IsValid)
            try
            {
                await using var input = model.Photo!.OpenReadStream();
                var photo = await vision.PrepareAsync(input, ct);
                var document = PlanogramStore.Export(plan!);
                var draft = new PhotoAuditDraft { OwnerId = Owner, PlanogramId = plan!.Id, PlanogramVersion = plan.Version,
                    Planogram = document, Width = photo.Width, Height = photo.Height,
                    ProductNames = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Sku, p => p.Name, ct) };
                await drafts.CreateAsync(draft, photo.Png, ct);
                return RedirectToAction(nameof(Edit), new { id = draft.Id });
            }
            catch (InvalidDataException ex) { ModelState.AddModelError("Photo", ex.Message); }
        model.Planograms = await plans.Query().AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        model.Drafts = await drafts.ListAsync(Owner,ct);
        Response.StatusCode = 422; return View("Index", model);
    }
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var draft = await drafts.ReadAsync(id, Owner, ct);
        return draft is null ? NotFound() : View(new PhotoPageModel(draft, vision.Ready, vision.Message));
    }
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Image(string id, CancellationToken ct)
    {
        var bytes = await drafts.ImageAsync(id, Owner, ct);
        return bytes is null ? NotFound() : File(bytes, "image/png");
    }
    [HttpPost, RequestSizeLimit(16384)]
    public async Task<IActionResult> Calibrate(string id, [FromBody] CalibrationRequest? request, CancellationToken ct)
    {
        var draft = await drafts.ReadAsync(id, Owner, ct);
        if (draft is null) return NotFound();
        if (!ModelState.IsValid || request is null) return UnprocessableEntity(new { error = "Некорректная калибровка." });
        var errors = ShelfCalibration.Validate(draft.Planogram, request.Shelves);
        if (errors.Count > 0 || !double.IsFinite(request.ConfidenceThreshold) || request.ConfidenceThreshold is < .25 or > .95)
            return UnprocessableEntity(new { error = errors.FirstOrDefault() ?? "Порог подтверждения: от 25% до 95%." });
        if (request.Revision != draft.Revision) return Conflict(new { error = "Черновик изменён. Обновите страницу." });
        try
        {
            await drafts.SaveAsync(draft with { Shelves = request.Shelves!, ConfidenceThreshold = request.ConfidenceThreshold, Detection = null, Compliance = null }, Owner, ct);
            return Json(new { revision = draft.Revision + 1 });
        }
        catch (DraftConflictException ex) { return Conflict(new { error = ex.Message }); }
    }
    [HttpPost]
    public async Task<IActionResult> Analyze(string id, int revision, CancellationToken ct)
    {
        var draft = await drafts.ReadAsync(id, Owner, ct);
        if (draft is null) return NotFound();
        string? error = null;
        if (draft.Revision != revision) { Response.StatusCode = 409; error = "Черновик изменён. Обновите страницу."; }
        else if (ShelfCalibration.Validate(draft.Planogram, draft.Shelves).FirstOrDefault() is { } invalid) { Response.StatusCode = 422; error = invalid; }
        else if (!vision.Ready) { Response.StatusCode = 503; error = vision.Message; }
        else
            try
            {
                var unknown = vision.Skus.Except(draft.ProductNames.Keys, StringComparer.Ordinal).ToArray();
                var unsupported = PlanogramPositions.Expand(draft.Planogram).Select(e => e.Sku).Except(vision.Skus, StringComparer.Ordinal).ToArray();
                if (unknown.Length > 0 || unsupported.Length > 0) throw new InvalidDataException("Классы подключённой модели не соответствуют каталогу и выбранной планограмме. Проверьте manifest и создайте новый черновик.");
                var detection = await vision.DetectAsync(async token => await drafts.ImageAsync(id, Owner, token) ?? throw new InvalidDataException("Фото недоступно."), ct);
                var result = analyzer.Analyze(draft.Planogram, detection.Products, new ComplianceOptions
                { Shelves = draft.Shelves, ShelfTolerance = 0, ConfidenceThreshold = draft.ConfidenceThreshold, KnownSkus = draft.ProductNames.Keys.ToHashSet(StringComparer.Ordinal) });
                await drafts.SaveAsync(draft with { Detection = detection, Compliance = result }, Owner, ct);
                return RedirectToAction(nameof(Edit), new { id });
            }
            catch (DraftConflictException ex) { Response.StatusCode = 409; error = ex.Message; }
            catch (InvalidDataException ex) { Response.StatusCode = 422; error = ex.Message; }
            catch (VisionBusyException ex) { Response.StatusCode = 429; error = ex.Message; }
            catch (VisionUnavailableException ex) { Response.StatusCode = 503; error = ex.Message; }
        ModelState.AddModelError("", error!);
        return View("Edit", new PhotoPageModel(draft, vision.Ready, vision.Message));
    }
    [HttpPost]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await drafts.DeleteAsync(id, Owner, ct);
        return RedirectToAction(nameof(Index));
    }
}

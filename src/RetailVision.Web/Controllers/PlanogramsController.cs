using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailVision.Core;
using RetailVision.Infrastructure;
using RetailVision.Web.Models;

namespace RetailVision.Web.Controllers;

public sealed class PlanogramsController(RetailVisionDbContext db, PlanogramStore store) : Controller
{
    private const int MaxJsonBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await store.Query().AsNoTracking().OrderByDescending(p => p.Id).ToListAsync(ct));

    [Authorize(Policy = "Admin"), HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct) => View("Edit", new PlanogramEditorModel(0, 0,
        new() { Name = "Новая планограмма", Shelves = [new() { Number = 1 }] }, await Products(ct)));

    [Authorize(Policy = "Admin"), HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var plan = await store.Query().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return plan is null ? NotFound() : View(new PlanogramEditorModel(id, plan.Version, PlanogramStore.Export(plan), await Products(ct)));
    }

    private async Task<List<Product>> Products(CancellationToken ct) => await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

    [Authorize(Policy = "Admin"), HttpPost, RequestSizeLimit(MaxJsonBytes)]
    public async Task<IActionResult> Save([FromBody] PlanogramSaveRequest? request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request is null) return UnprocessableEntity(new { errors = new[] { "Некорректный JSON или поля планограммы." } });
        try
        {
            var plan = await store.SaveAsync(request.Id, request.Version, request.Document, ct);
            return Json(new { id = plan.Id, version = plan.Version, url = Url.Action(nameof(Edit), new { id = plan.Id }) });
        }
        catch (PlanogramWriteException ex) { return StatusCode(ex.Conflict ? 409 : 422, new { errors = ex.Errors }); }
    }

    [Authorize(Policy = "Admin"), HttpGet]
    public async Task<IActionResult> Export(int id, CancellationToken ct)
    {
        var plan = await store.Query().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return plan is null ? NotFound() : File(JsonSerializer.SerializeToUtf8Bytes(PlanogramStore.Export(plan), JsonOptions), "application/json", $"planogram-{id}-v{plan.Version}.json");
    }

    [Authorize(Policy = "Admin"), HttpGet]
    public IActionResult Import() => View(new PlanogramImportModel());

    [Authorize(Policy = "Admin"), HttpPost, RequestSizeLimit(300 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 300 * 1024)]
    public async Task<IActionResult> Import(PlanogramImportModel model, CancellationToken ct)
    {
        if (model.File is null || model.File.Length is <= 0 or > MaxJsonBytes)
            ModelState.AddModelError("File", "Выберите JSON размером до 256 КБ.");
        if (ModelState.IsValid)
        {
            try
            {
                await using var input = model.File!.OpenReadStream();
                var document = await JsonSerializer.DeserializeAsync<PlanogramDocument>(input, JsonOptions, ct);
                var plan = await store.SaveAsync(0, 0, document, ct);
                TempData["Notice"] = "Планограмма импортирована как новая копия.";
                return RedirectToAction(nameof(Edit), new { id = plan.Id });
            }
            catch (JsonException) { ModelState.AddModelError("File", "Некорректный формат JSON. Используйте файл экспорта RetailVision версии 1."); }
            catch (PlanogramWriteException ex) { foreach (var error in ex.Errors) ModelState.AddModelError("", error); }
        }
        Response.StatusCode = 422;
        return View(model);
    }

    [Authorize(Policy = "Admin"), HttpPost]
    public async Task<IActionResult> Duplicate(int id, int version, CancellationToken ct)
    {
        var plan = await store.Query().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return NotFound();
        if (plan.Version != version) return Conflict("Планограмма изменилась. Обновите список.");
        var document = PlanogramStore.Export(plan);
        document.Name = document.Name[..Math.Min(document.Name.Length, 110)] + " — копия";
        try
        {
            var copy = await store.SaveAsync(0, 0, document, ct);
            TempData["Notice"] = "Создана независимая копия планограммы.";
            return RedirectToAction(nameof(Edit), new { id = copy.Id });
        }
        catch (PlanogramWriteException ex) { return StatusCode(ex.Conflict ? 409 : 422, string.Join(" ", ex.Errors)); }
    }

    [Authorize(Policy = "Admin"), HttpGet]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var plan = await db.Planograms.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return plan is null ? NotFound() : View(plan);
    }

    [Authorize(Policy = "Admin"), HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, int version, CancellationToken ct)
    {
        var plan = await db.Planograms.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return NotFound();
        if (plan.Version != version || await db.Audits.AnyAsync(a => a.PlanogramId == id, ct))
            ModelState.AddModelError("", "Планограмма изменена или связана с аудитами. Удаление недоступно.");
        if (ModelState.IsValid)
        {
            db.Planograms.Remove(plan);
            try { await db.SaveChangesAsync(ct); TempData["Notice"] = "Планограмма удалена."; return RedirectToAction(nameof(Index)); }
            catch (DbUpdateException) { ModelState.AddModelError("", "Связанные данные изменились. Обновите страницу."); }
        }
        Response.StatusCode = 409;
        return View("Delete", plan);
    }
}

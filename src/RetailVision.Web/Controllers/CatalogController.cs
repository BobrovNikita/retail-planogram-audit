using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailVision.Core;
using RetailVision.Infrastructure;
using RetailVision.Web.Models;

namespace RetailVision.Web.Controllers;

public sealed class CatalogController(RetailVisionDbContext db, ProductImageStore images, ILogger<CatalogController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int? categoryId, string status = "active", string display = "cards", CancellationToken ct = default)
    {
        if (search?.Length > 100 || status is not ("active" or "inactive" or "all") || display is not ("cards" or "table")) return BadRequest();
        var query = db.Products.AsNoTracking().Include(p => p.Category).AsQueryable();
        if (!User.IsInRole("Administrator")) status = "active";
        if (status != "all") query = query.Where(p => p.IsActive == (status == "active"));
        if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.Name.Contains(search) || p.Sku.Contains(search) || p.Brand.Contains(search));
        return View(new CatalogViewModel(await query.OrderBy(p => p.Id).ToListAsync(ct), search,
            await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct), categoryId, status, display));
    }

    [Authorize(Policy = "Admin"), HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct) => View("Edit", await Populate(new ProductEditModel(), ct));

    [Authorize(Policy = "Admin"), HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (p is null) return NotFound();
        var mappings = await db.ModelClassMappings.Where(m => m.ProductId == id).ToListAsync(ct);
        return View(await Populate(new ProductEditModel
        {
            Id = p.Id, Revision = p.Revision, Sku = p.Sku, Name = p.Name, Brand = p.Brand,
            PackageSize = p.PackageSize, CategoryId = p.CategoryId, Color = p.Color, IsActive = p.IsActive,
            HasImage = p.ImagePath is not null,
            CreatedAtUtc = p.CreatedAtUtc,
            Mappings = mappings.Select(m => new ClassMappingInput { ModelVersionId = m.ModelVersionId, ClassIndex = m.ClassIndex }).ToList()
        }, ct));
    }

    [Authorize(Policy = "Admin"), HttpPost, RequestSizeLimit(5 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 5 * 1024 * 1024)]
    public Task<IActionResult> Create(ProductEditModel model, CancellationToken ct)
    {
        if (model.Id != 0) return Task.FromResult<IActionResult>(BadRequest());
        return Save(model, ct);
    }

    [Authorize(Policy = "Admin"), HttpPost, RequestSizeLimit(5 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 5 * 1024 * 1024)]
    public Task<IActionResult> Edit(int id, ProductEditModel model, CancellationToken ct) =>
        id != model.Id || id < 1 ? Task.FromResult<IActionResult>(BadRequest()) : Save(model, ct);

    private async Task<IActionResult> Save(ProductEditModel model, CancellationToken ct)
    {
        var product = model.Id == 0 ? new Product() : await db.Products.SingleOrDefaultAsync(p => p.Id == model.Id, ct);
        if (product is null) return NotFound();
        model.HasImage = product.ImagePath is not null;
        model.CreatedAtUtc = model.Id == 0 ? null : product.CreatedAtUtc;
        if (model.Id != 0 && product.Revision != model.Revision)
        {
            Response.StatusCode = 409;
            ModelState.AddModelError("", "Товар уже изменён. Откройте актуальную карточку и повторите изменения.");
        }
        if (await db.Products.AnyAsync(p => p.Sku == model.Sku && p.Id != model.Id, ct)) ModelState.AddModelError("Sku", "Этот SKU уже существует.");
        if (model.Id != 0 && product.Sku != model.Sku && await HasReferences(product, ct))
            ModelState.AddModelError("Sku", "SKU связанного товара менять нельзя. Создайте новый товар.");
        if (string.IsNullOrWhiteSpace(model.NewCategory) && !await db.Categories.AnyAsync(c => c.Id == model.CategoryId, ct))
            ModelState.AddModelError("CategoryId", "Выберите категорию или укажите новую.");
        if (model.Image is not null && (model.Image.Length is <= 0 or > ProductImageStore.MaxBytes))
            ModelState.AddModelError("Image", "Выберите изображение размером до 4 МБ.");
        if (model.Mappings is null || model.Mappings.Count > 100 || model.Mappings.Select(m => m.ModelVersionId).Distinct().Count() != model.Mappings.Count)
            ModelState.AddModelError("", "Некорректные привязки классов модели.");
        else
        {
            var validModels = await db.ModelVersions.Select(m => m.Id).ToListAsync(ct);
            foreach (var m in model.Mappings)
            {
                if (!validModels.Contains(m.ModelVersionId)) ModelState.AddModelError("", "Модель не найдена.");
                if (m.ClassIndex.HasValue && await db.ModelClassMappings.AnyAsync(x => x.ModelVersionId == m.ModelVersionId && x.ClassIndex == m.ClassIndex && x.ProductId != model.Id, ct))
                    ModelState.AddModelError("", $"Класс {m.ClassIndex} уже привязан к другому товару.");
            }
        }
        if (!ModelState.IsValid) return View("Edit", await Populate(model, ct));

        string? newImage = null;
        var oldImage = product.ImagePath;
        try
        {
            if (model.Image is not null)
            {
                await using var input = model.Image.OpenReadStream();
                newImage = await images.SaveAsync(input, ct);
            }
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            ProductCategory? category = null;
            if (!string.IsNullOrWhiteSpace(model.NewCategory))
            {
                var categoryName = model.NewCategory.Trim();
                category = await db.Categories.FirstOrDefaultAsync(c => c.Name == categoryName, ct) ?? new ProductCategory { Name = categoryName };
            }
            product.Sku = model.Sku;
            product.Name = model.Name.Trim();
            product.Brand = model.Brand.Trim();
            product.PackageSize = model.PackageSize.Trim();
            product.Color = model.Color;
            product.IsActive = model.IsActive;
            if (category is not null) product.Category = category;
            else product.CategoryId = model.CategoryId!.Value;
            product.ImagePath = newImage ?? (model.RemoveImage ? null : oldImage);
            if (model.Id == 0) db.Products.Add(product);
            else
            {
                product.Revision++;
                db.ModelClassMappings.RemoveRange(await db.ModelClassMappings.Where(m => m.ProductId == product.Id).ToListAsync(ct));
            }
            await db.SaveChangesAsync(ct);
            foreach (var m in model.Mappings!.Where(m => m.ClassIndex.HasValue))
                db.ModelClassMappings.Add(new() { ModelVersionId = m.ModelVersionId, ClassIndex = m.ClassIndex!.Value, ProductId = product.Id });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (InvalidDataException ex) { ModelState.AddModelError("Image", ex.Message); }
        catch (DbUpdateConcurrencyException) { Response.StatusCode = 409; ModelState.AddModelError("", "Товар изменился во время сохранения. Обновите карточку."); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Catalog constraint conflict for product {ProductId}", model.Id);
            ModelState.AddModelError("", "Не удалось сохранить: SKU или класс уже занят, либо связанные данные изменились.");
        }
        catch
        {
            DeleteImageSafely(newImage);
            throw;
        }
        if (!ModelState.IsValid)
        {
            DeleteImageSafely(newImage);
            db.ChangeTracker.Clear();
            return View("Edit", await Populate(model, ct));
        }
        if (oldImage != product.ImagePath) DeleteImageSafely(oldImage);
        TempData["Notice"] = "Товар сохранён.";
        return RedirectToAction(nameof(Edit), new { id = product.Id });
    }

    private async Task<ProductEditModel> Populate(ProductEditModel model, CancellationToken ct)
    {
        model.Categories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        model.Models = await db.ModelVersions.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        model.Mappings ??= [];
        foreach (var version in model.Models)
            if (!model.Mappings.Any(m => m.ModelVersionId == version.Id)) model.Mappings.Add(new() { ModelVersionId = version.Id });
        return model;
    }

    [Authorize(Policy = "Admin"), HttpGet]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? NotFound() : View(new ProductDeleteModel(product, await HasReferences(product, ct)));
    }

    [Authorize(Policy = "Admin"), HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, int revision, bool archive, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        var linked = await HasReferences(product, ct);
        if (product.Revision != revision || (!archive && linked))
        {
            Response.StatusCode = 409;
            ModelState.AddModelError("", linked ? "Товар связан с данными. Можно отключить его без удаления." : "Товар изменён. Обновите страницу.");
            return View("Delete", new ProductDeleteModel(product, linked));
        }
        if (archive) { product.IsActive = false; product.Revision++; }
        else db.Products.Remove(product);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            Response.StatusCode = 409;
            ModelState.AddModelError("", "Товар изменился или получил новые связи. Обновите страницу.");
            return View("Delete", new ProductDeleteModel(product, true));
        }
        if (!archive) DeleteImageSafely(product.ImagePath);
        TempData["Notice"] = archive ? "Товар отключён. Существующие планограммы сохранены." : "Товар удалён.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> HasReferences(Product product, CancellationToken ct) =>
        await db.Set<PlanogramSlot>().AnyAsync(s => s.ProductId == product.Id, ct) ||
        await db.ModelClassMappings.AnyAsync(m => m.ProductId == product.Id, ct) ||
        await db.Set<DetectedProduct>().AnyAsync(d => d.Sku == product.Sku, ct);

    // Uploaded files are private. Even active product images require administrative access.
    [Authorize(Policy = "Admin"), HttpGet]
    public async Task<IActionResult> Image(int id, CancellationToken ct)
    {
        var key = await db.Products.Where(p => p.Id == id).Select(p => p.ImagePath).SingleOrDefaultAsync(ct);
        var path = images.Resolve(key);
        if (path is null || !System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return PhysicalFile(path, "image/png");
    }
    private void DeleteImageSafely(string? key)
    {
        try { images.Delete(key); }
        catch (IOException ex) { logger.LogWarning(ex, "Unable to clean up an unused product image"); }
    }
}

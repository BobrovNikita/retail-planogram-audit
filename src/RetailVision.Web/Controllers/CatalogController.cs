using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailVision.Infrastructure;
using RetailVision.Web.Models;
namespace RetailVision.Web.Controllers;

public sealed class CatalogController(RetailVisionDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken ct)
    {
        if (search?.Length > 100) return BadRequest();
        var query = db.Products.AsNoTracking().Include(p => p.Category).Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.Name.Contains(search) || p.Sku.Contains(search) || p.Brand.Contains(search));
        return View(new CatalogViewModel(await query.OrderBy(p => p.Id).ToListAsync(ct), search));
    }
}

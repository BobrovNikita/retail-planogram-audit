using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailVision.Infrastructure;
namespace RetailVision.Web.Controllers;

public sealed class PlanogramsController(RetailVisionDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await db.Planograms.AsNoTracking()
        .Include(p => p.Shelves).ThenInclude(s => s.Slots).ThenInclude(s => s.Product).ToListAsync(ct));
}

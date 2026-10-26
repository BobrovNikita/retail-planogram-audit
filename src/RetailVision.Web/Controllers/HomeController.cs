using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailVision.Infrastructure;
using RetailVision.Web.Models;

namespace RetailVision.Web.Controllers;

public sealed class HomeController(RetailVisionDbContext db) : Controller
{
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var owner=User.FindFirstValue(ClaimTypes.NameIdentifier);
        var audits = db.Audits.AsNoTracking().Where(a => owner!=null && a.OwnerId==owner && a.Mode == "REAL" && a.SnapshotJson!="{}" && a.ArchivedAtUtc==null);
        var days=await audits.GroupBy(a=>a.CreatedAtUtc.Date).Select(g=>new {Date=g.Key,Count=g.Count(),Average=g.Average(a=>a.CompliancePercent)})
            .OrderByDescending(d=>d.Date).Take(14).ToListAsync(ct);
        return View(new DashboardViewModel(await audits.CountAsync(ct),
            await audits.Select(a => (double?)a.CompliancePercent).AverageAsync(ct),
            await audits.SumAsync(a=>a.ConfirmedViolations,ct),
            await audits.SumAsync(a=>a.DetectionCount,ct),
            await audits.OrderByDescending(a => a.CreatedAtUtc).Take(5).ToListAsync(ct),
            days.Select(d=>new AuditDay(d.Date,d.Count,d.Average)).ToArray()));
    }
    [Authorize(Policy = "Admin")]
    public IActionResult History() => RedirectToAction("Index","History");
    [Authorize(Policy = "Admin")]
    public IActionResult Analytics() => RedirectToAction("Analytics","History");
    [Authorize(Policy = "Admin")]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Settings([FromServices] PhotoVisionService vision, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().Where(p => vision.Skus.Contains(p.Sku)).ToDictionaryAsync(p => p.Sku, ct);
        var skus = vision.Skus.Select((sku, index) =>
        {
            products.TryGetValue(sku, out var product);
            return new SettingsSku(index, sku, product?.Id, product?.Name, product?.IsActive ?? false);
        }).ToArray();
        return View(new SettingsViewModel(vision.Ready, vision.Message, vision.ModelVersion, vision.ModelSha256, skus));
    }
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

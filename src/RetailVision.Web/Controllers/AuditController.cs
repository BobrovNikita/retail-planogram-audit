using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RetailVision.Core;
using RetailVision.Infrastructure;
using RetailVision.Vision;
using RetailVision.Web.Models;
namespace RetailVision.Web.Controllers;

[EnableRateLimiting("public")]
public sealed class AuditController(IProductDetector detector, IComplianceAnalyzer analyzer,
    RetailVisionDbContext db, ILogger<AuditController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
    [HttpPost]
    public async Task<IActionResult> Demo(string scenario, CancellationToken ct)
    {
        if (scenario is not ("correct" or "missing" or "sequence")) return BadRequest("Неизвестный DEMO-сценарий.");
        var result = await detector.DetectAsync(new DetectionRequest(scenario), ct);
        var compliance = analyzer.Analyze(FixtureProductDetector.Expected, result.Products);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Sku, ct);
        logger.LogInformation("DEMO {Scenario} completed: {CompliancePercent}% with {ViolationCount} issues", scenario, compliance.Percent, compliance.Violations.Count);
        return View("Result", new DemoViewModel(scenario, result, compliance, FixtureProductDetector.Expected, products));
    }
}

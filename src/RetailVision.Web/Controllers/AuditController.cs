using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RetailVision.Core;
using RetailVision.Vision;
using RetailVision.Web.Models;
namespace RetailVision.Web.Controllers;

[EnableRateLimiting("public")]
public sealed class AuditController(IProductDetector detector, IComplianceAnalyzer analyzer,
    ILogger<AuditController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
    [HttpPost]
    public async Task<IActionResult> Demo(string scenario, CancellationToken ct)
    {
        if (!FixtureProductDetector.Scenarios.Contains(scenario, StringComparer.Ordinal)) return BadRequest("Неизвестный DEMO-сценарий.");
        var result = await detector.DetectAsync(new DetectionRequest(scenario), ct);
        var products = FixtureProductDetector.CreateProducts();
        var compliance = analyzer.Analyze(FixtureProductDetector.Expected, result.Products, new ComplianceOptions
        {
            KnownSkus = products.Keys.ToHashSet(StringComparer.Ordinal),
            Shelves = [new(1, .05, .95, .05, .45), new(2, .05, .95, .50, .95)]
        });
        logger.LogInformation("DEMO {Scenario} completed: {CompliancePercent}% with {ViolationCount} issues", scenario, compliance.Percent, compliance.Violations.Count);
        return View("Result", new DemoViewModel(scenario, result, compliance, FixtureProductDetector.Expected, products));
    }
}

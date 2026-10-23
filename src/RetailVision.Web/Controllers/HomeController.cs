using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailVision.Infrastructure;
using RetailVision.Web.Models;

namespace RetailVision.Web.Controllers;

public sealed class HomeController(RetailVisionDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var audits = db.Audits.AsNoTracking().Where(a => a.Mode == "REAL");
        return View(new DashboardViewModel(await audits.CountAsync(ct),
            await audits.Select(a => (double?)a.CompliancePercent).AverageAsync(ct),
            await audits.SelectMany(a => a.Violations).CountAsync(ct),
            await audits.SelectMany(a => a.Detections).CountAsync(ct),
            await audits.OrderByDescending(a => a.CreatedAtUtc).Take(5).ToListAsync(ct)));
    }
    public IActionResult History() => Feature("История проверок", "Сохранение аудитов и фильтры появятся на этапе 5. DEMO-сеансы не записываются в историю.", "История пуста");
    public IActionResult Analytics() => Feature("Аналитика", "Графики и сравнение проверок появятся после реализации сохранения аудитов на этапе 5.", "Пока нет данных для анализа");
    [Authorize(Policy = "Admin")]
    public IActionResult Settings() => Feature("Настройки", "Вы вошли как администратор. Управление моделями и настройками будет добавлено на следующих этапах.", "Административный доступ активен");
    private IActionResult Feature(string title, string description, string heading) => View("Feature", new FeatureViewModel(title, description, heading));
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

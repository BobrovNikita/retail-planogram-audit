using System.ComponentModel.DataAnnotations;
using RetailVision.Core;
namespace RetailVision.Web.Models;

public record DashboardViewModel(int AuditCount, double? Average, int Violations, int Products, IReadOnlyList<Audit> Recent, IReadOnlyList<AuditDay> Days);
public record FeatureViewModel(string Title, string Description, string Heading);
public record CatalogViewModel(IReadOnlyList<Product> Products, string? Search,
    IReadOnlyList<ProductCategory> Categories, int? CategoryId, string Status, string Display);
public record DemoViewModel(string Scenario, DetectionResult Detection, ComplianceResult Compliance,
    IReadOnlyList<ExpectedPosition> Expected, IReadOnlyDictionary<string, Product> Products);
public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Укажите email."), EmailAddress(ErrorMessage = "Проверьте email.")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "Укажите пароль."), DataType(DataType.Password)]
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

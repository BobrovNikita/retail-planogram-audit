using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using RetailVision.Core;

namespace RetailVision.Web.Models;

public sealed class ProductEditModel
{
    public int Id { get; set; }
    public int Revision { get; set; } = 1;
    [Required(ErrorMessage = "Укажите SKU."), RegularExpression("^[A-Z0-9][A-Z0-9._-]{0,63}$", ErrorMessage = "SKU: латинские заглавные буквы, цифры, точка, дефис или подчёркивание; до 64 символов.")]
    public string Sku { get; set; } = "";
    [Required(ErrorMessage = "Укажите название."), StringLength(200)] public string Name { get; set; } = "";
    [Required, StringLength(100)] public string Brand { get; set; } = "";
    [Required, StringLength(80)] public string PackageSize { get; set; } = "";
    public int? CategoryId { get; set; }
    [StringLength(100)] public string? NewCategory { get; set; }
    [RegularExpression("^#[0-9a-fA-F]{6}$"), Required] public string Color { get; set; } = "#16a89b";
    public bool IsActive { get; set; } = true;
    public IFormFile? Image { get; set; }
    public bool RemoveImage { get; set; }
    public List<ClassMappingInput> Mappings { get; set; } = [];
    [ValidateNever] public IReadOnlyList<ProductCategory> Categories { get; set; } = [];
    [ValidateNever] public IReadOnlyList<ModelVersion> Models { get; set; } = [];
    [ValidateNever] public bool HasImage { get; set; }
    [ValidateNever] public DateTime? CreatedAtUtc { get; set; }
}

public sealed class ClassMappingInput
{
    public int ModelVersionId { get; set; }
    [Range(0, 100000)] public int? ClassIndex { get; set; }
}

public record ProductDeleteModel(Product Product, bool HasReferences);

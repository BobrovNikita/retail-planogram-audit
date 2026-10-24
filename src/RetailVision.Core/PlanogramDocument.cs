using System.ComponentModel.DataAnnotations;

namespace RetailVision.Core;

// Portable interchange uses SKU strings, never database identifiers.
public sealed class PlanogramDocument
{
    public int SchemaVersion { get; set; } = 1;
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [StringLength(2000)] public string Description { get; set; } = "";
    [StringLength(200)] public string Store { get; set; } = "";
    public List<ShelfDocument> Shelves { get; set; } = [];
}

public sealed class ShelfDocument
{
    public int Number { get; set; }
    public List<SlotDocument> Slots { get; set; } = [];
}

public sealed class SlotDocument
{
    public int Position { get; set; }
    public string Sku { get; set; } = "";
    public int Facings { get; set; } = 1;
}

public static class PlanogramValidator
{
    public static IReadOnlyList<string> Validate(PlanogramDocument? document, IReadOnlySet<string> availableSkus)
    {
        var errors = new List<string>();
        if (document is null) return ["Документ планограммы пуст."];
        if (document.SchemaVersion != 1) errors.Add("Поддерживается только schemaVersion = 1.");
        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 120) errors.Add("Название: от 1 до 120 символов.");
        if (document.Description is null || document.Description.Length > 2000) errors.Add("Описание: не более 2000 символов.");
        if (document.Store is null || document.Store.Length > 200) errors.Add("Торговая зона: не более 200 символов.");
        if (document.Shelves is null || document.Shelves.Count is < 1 or > 10) return [.. errors, "Укажите от 1 до 10 полок."];
        if (document.Shelves.Any(s => s is null)) return [.. errors, "Полка не может быть null."];
        if (!document.Shelves.Select(s => s.Number).Order().SequenceEqual(Enumerable.Range(1, document.Shelves.Count)))
            errors.Add("Номера полок должны быть уникальны и идти подряд от 1.");
        long facings = 0;
        foreach (var shelf in document.Shelves)
        {
            if (shelf.Slots is null || shelf.Slots.Count > 100) { errors.Add("На полке допускается не более 100 позиций."); continue; }
            if (shelf.Slots.Any(s => s is null)) { errors.Add("Позиция не может быть null."); continue; }
            if (!shelf.Slots.Select(s => s.Position).Order().SequenceEqual(Enumerable.Range(1, shelf.Slots.Count)))
                errors.Add($"Полка {shelf.Number}: позиции должны быть уникальны и идти подряд от 1.");
            foreach (var slot in shelf.Slots)
            {
                if (slot.Facings is < 1 or > 100) errors.Add("Количество фейсингов должно быть от 1 до 100.");
                facings += slot.Facings;
                if (string.IsNullOrWhiteSpace(slot.Sku) || !availableSkus.Contains(slot.Sku))
                    errors.Add($"SKU «{slot.Sku}» отсутствует или неактивен.");
            }
        }
        if (facings > 500) errors.Add("В планограмме допускается не более 500 фейсингов.");
        return errors.Distinct().ToArray();
    }
}

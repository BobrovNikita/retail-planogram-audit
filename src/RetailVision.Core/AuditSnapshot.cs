using System.Text.RegularExpressions;

namespace RetailVision.Core;

public sealed record ReviewedDetection(Detection Detection, bool ConfirmedByHuman = false)
{
    // Human confirmation is a decision, not a new model confidence. The raw value stays immutable.
    public Detection Effective => ConfirmedByHuman ? Detection with { Confidence = 1 } : Detection;
}

public sealed record AuditSnapshot(PlanogramDocument Planogram, IReadOnlyList<ExpectedPosition> Expected,
    IReadOnlyDictionary<string,string> ProductNames, IReadOnlyList<ShelfGeometry> Shelves,
    double ConfidenceThreshold, double ShelfTolerance, int Width, int Height,
    DetectionResult OriginalDetection, ComplianceResult OriginalResult, string? Scenario = null);

public sealed record DetectionCorrection(string Id, string? Sku, double X, double Y, double Width, double Height, bool Confirmed);

public static class AuditReview
{
    public static IReadOnlyList<ReviewedDetection> Apply(AuditSnapshot snapshot, IReadOnlyList<ReviewedDetection> current,
        IReadOnlyList<DetectionCorrection>? changes)
    {
        if (changes is null || changes.Count > 500 || changes.Any(c=>c is null)) throw new ArgumentException("Допускается до 500 объектов.");
        if (changes.Select(c=>c.Id).Distinct(StringComparer.Ordinal).Count()!=changes.Count) throw new ArgumentException("Идентификаторы объектов должны быть уникальны.");
        var existing = current.ToDictionary(c=>c.Detection.Id,StringComparer.Ordinal);
        var original = snapshot.OriginalDetection.Products.ToDictionary(c=>c.Id,StringComparer.Ordinal);
        var result = new List<ReviewedDetection>();
        foreach(var c in changes)
        {
            if(c.Id is null || !Regex.IsMatch(c.Id,"^[a-zA-Z0-9-]{1,80}$")) throw new ArgumentException("Некорректный идентификатор объекта.");
            existing.TryGetValue(c.Id,out var previous);
            if(previous is null && !Regex.IsMatch(c.Id,"^manual-[a-f0-9]{32}$")) throw new ArgumentException("Новый объект должен иметь ручной идентификатор.");
            if(c.Sku is not null && !snapshot.ProductNames.ContainsKey(c.Sku)) throw new ArgumentException("SKU отсутствует в снимке каталога.");
            if(c.Confirmed && c.Sku is null) throw new ArgumentException("Для подтверждения выберите SKU.");
            if(new[]{c.X,c.Y,c.Width,c.Height}.Any(v=>!double.IsFinite(v)) || c.X<0 || c.Y<0 || c.Width<=0 || c.Height<=0 || c.X+c.Width>1.000000001 || c.Y+c.Height>1.000000001)
                throw new ArgumentException("Рамка должна находиться внутри фотографии и иметь положительный размер.");
            var changed = previous is null || c.Sku!=previous.Detection.Sku || c.X!=previous.Detection.X || c.Y!=previous.Detection.Y || c.Width!=previous.Detection.Width || c.Height!=previous.Detection.Height;
            if(changed && !c.Confirmed) throw new ArgumentException("Изменённые и новые объекты нужно подтвердить вручную.");
            original.TryGetValue(c.Id,out var raw);
            if(!c.Confirmed && (raw is null || c.Sku!=raw.Sku || c.X!=raw.X || c.Y!=raw.Y || c.Width!=raw.Width || c.Height!=raw.Height))
                throw new ArgumentException("Изменённый объект нельзя выдать за исходную детекцию модели. Сохраните ручное подтверждение или восстановите исходные детекции.");
            // Only geometry/label can be submitted; original model confidence and class index cannot be forged.
            result.Add(new(new Detection(c.Id,c.Sku,raw?.Confidence ?? 0,0,c.X,c.Y,c.Width,c.Height,raw?.ClassIndex),c.Confirmed));
        }
        return result;
    }
    public static ComplianceResult Analyze(AuditSnapshot snapshot, IReadOnlyList<ReviewedDetection> current, IComplianceAnalyzer analyzer) =>
        analyzer.Analyze(snapshot.Expected,current.Select(c=>c.Effective).ToArray(),new ComplianceOptions
        { KnownSkus=snapshot.ProductNames.Keys.ToHashSet(StringComparer.Ordinal),Shelves=snapshot.Shelves,
          ConfidenceThreshold=snapshot.ConfidenceThreshold,ShelfTolerance=snapshot.ShelfTolerance });
}

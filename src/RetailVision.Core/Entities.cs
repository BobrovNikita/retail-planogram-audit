namespace RetailVision.Core;

public sealed class ProductCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    public string PackageSize { get; set; } = "";
    public string Color { get; set; } = "#16a89b";
    public string? ImagePath { get; set; }
    public int CategoryId { get; set; }
    public ProductCategory Category { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public int Revision { get; set; } = 1;
}

public sealed class SeedMarker
{
    public string Id { get; set; } = "";
}

public sealed class ModelVersion
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "DEMO";
}

public sealed class ModelClassMapping
{
    public int ModelVersionId { get; set; }
    public ModelVersion ModelVersion { get; set; } = null!;
    public int ClassIndex { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class Planogram
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Store { get; set; } = "";
    public int Version { get; set; } = 1;
    public List<PlanogramShelf> Shelves { get; set; } = [];
}

public sealed class PlanogramShelf
{
    public int Id { get; set; }
    public int PlanogramId { get; set; }
    public int Number { get; set; }
    public List<PlanogramSlot> Slots { get; set; } = [];
}

public sealed class PlanogramSlot
{
    public int Id { get; set; }
    public int PlanogramShelfId { get; set; }
    public int Position { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int Facings { get; set; } = 1;
}

// Only explicitly saved, owner-scoped reports are persisted. Public fixtures never insert rows.
public sealed class Audit
{
    public int Id { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? PlanogramId { get; set; }
    public Planogram? Planogram { get; set; }
    public int ModelVersionId { get; set; }
    public ModelVersion ModelVersion { get; set; } = null!;
    public string PlanogramSnapshotJson { get; set; } = "{}";
    public string OwnerId { get; set; } = "";
    public string SourceKey { get; set; } = Guid.NewGuid().ToString("N");
    public int Revision { get; set; } = 1;
    public string PlanogramName { get; set; } = "";
    public string Store { get; set; } = "";
    public int PlanogramVersion { get; set; }
    public string ModelName { get; set; } = "";
    public string? ModelSha256 { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string CurrentDetectionsJson { get; set; } = "[]";
    public string ResultJson { get; set; } = "{}";
    public double OriginalPercent { get; set; }
    public int ConfirmedViolations { get; set; }
    public int ReviewCases { get; set; }
    public int DetectionCount { get; set; }
    public bool HasCorrections { get; set; }
    public string Mode { get; set; } = "DEMO";
    public string? PhotoPath { get; set; }
    public double CompliancePercent { get; set; }
    public List<DetectedProduct> Detections { get; set; } = [];
    public List<AuditViolation> Violations { get; set; } = [];
    public List<AuditCorrection> Corrections { get; set; } = [];
}

public sealed class DetectedProduct
{
    public int Id { get; set; }
    public int AuditId { get; set; }
    public string? Sku { get; set; }
    public double Confidence { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Shelf { get; set; }
}

public sealed class AuditViolation
{
    public int Id { get; set; }
    public int AuditId { get; set; }
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class AuditCorrection
{
    public int Id { get; set; }
    public int AuditId { get; set; }
    public string ChangesJson { get; set; } = "{}";
    public string ActorId { get; set; } = "";
    public string Reason { get; set; } = "";
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

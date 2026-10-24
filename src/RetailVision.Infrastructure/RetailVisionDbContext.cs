using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RetailVision.Core;

namespace RetailVision.Infrastructure;

public sealed class RetailVisionDbContext(DbContextOptions<RetailVisionDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCategory> Categories => Set<ProductCategory>();
    public DbSet<Planogram> Planograms => Set<Planogram>();
    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();
    public DbSet<ModelClassMapping> ModelClassMappings => Set<ModelClassMapping>();
    public DbSet<Audit> Audits => Set<Audit>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
        builder.Entity<Product>().Property(p => p.Revision).IsConcurrencyToken();
        builder.Entity<Planogram>().Property(p => p.Version).IsConcurrencyToken();
        builder.Entity<SeedMarker>().HasKey(m => m.Id);
        builder.Entity<Product>().Property(p => p.Sku).HasMaxLength(64);
        builder.Entity<Product>().Property(p => p.Name).HasMaxLength(200);
        builder.Entity<Product>().HasOne(p => p.Category).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ModelClassMapping>().HasKey(m => new { m.ModelVersionId, m.ClassIndex });
        builder.Entity<ModelClassMapping>().HasIndex(m => new { m.ModelVersionId, m.ProductId }).IsUnique();
        builder.Entity<ModelClassMapping>().ToTable(t => t.HasCheckConstraint("CK_ModelClass_Index", "ClassIndex >= 0"));
        builder.Entity<ModelClassMapping>().HasOne(m => m.Product).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PlanogramShelf>().HasIndex(s => new { s.PlanogramId, s.Number }).IsUnique();
        builder.Entity<PlanogramShelf>().ToTable(t => t.HasCheckConstraint("CK_Shelf_Number", "Number > 0"));
        builder.Entity<PlanogramSlot>().HasIndex(s => new { s.PlanogramShelfId, s.Position }).IsUnique();
        builder.Entity<PlanogramSlot>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Slot_Position", "Position > 0");
            t.HasCheckConstraint("CK_Slot_Facings", "Facings > 0 AND Facings <= 100");
        });
        builder.Entity<PlanogramSlot>().HasOne(s => s.Product).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Audit>().HasOne(a => a.Planogram).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Audit>().HasOne(a => a.ModelVersion).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Audit>().HasIndex(a => a.CreatedAtUtc);
    }
}

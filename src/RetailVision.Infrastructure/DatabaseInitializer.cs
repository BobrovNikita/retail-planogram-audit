using Microsoft.EntityFrameworkCore;
using RetailVision.Core;

namespace RetailVision.Infrastructure;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(RetailVisionDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        if (await db.Products.AnyAsync(ct)) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var category = new ProductCategory { Name = "Напитки" };
        var data = new[]
        {
            ("RV-COLA", "Cola Original", "#a24a43"), ("RV-LIME", "Lime Fresh", "#459d7a"),
            ("RV-ORANGE", "Orange Soda", "#e5a048"), ("RV-WATER", "Pure Water", "#619acb"),
            ("RV-TEA", "Green Tea", "#739660"), ("RV-JUICE", "Apple Juice", "#c1a54c")
        };
        var products = data.Select(p => new Product
        {
            Sku = p.Item1,
            Name = p.Item2,
            Color = p.Item3,
            Category = category,
            Brand = "RV Studio",
            PackageSize = "500 мл"
        }).ToList();
        db.Products.AddRange(products);
        var model = new ModelVersion { Name = "fixture-v1", Mode = "DEMO" };
        db.ModelVersions.Add(model);
        for (var i = 0; i < products.Count; i++)
            db.ModelClassMappings.Add(new() { ModelVersion = model, ClassIndex = i, Product = products[i] });
        db.Planograms.Add(new Planogram
        {
            Name = "Напитки · базовая выкладка",
            Description = "Учебная планограмма для трёх DEMO-сценариев.",
            Store = "Демонстрационная торговая зона",
            Shelves =
            [
                new() { Number = 1, Slots = [Slot(products[0], 1, 2), Slot(products[1], 2), Slot(products[2], 3)] },
                new() { Number = 2, Slots = [Slot(products[3], 1, 2), Slot(products[4], 2), Slot(products[5], 3)] }
            ]
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static PlanogramSlot Slot(Product product, int position, int facings = 1) =>
        new() { Product = product, Position = position, Facings = facings };
}

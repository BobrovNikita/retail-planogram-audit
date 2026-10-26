using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailVision.Core;
using RetailVision.Infrastructure;
using RetailVision.Vision;

// Deliberately accepts only the reviewed v2 bundle. No arbitrary ZIP extraction or code execution.
const string bundleSha = "c7fffc4ece1b7c47027b3340d18bfdb37e7f788f87c9e4e54f46b4d0da8b0480";
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: RetailVision.DemoSetup <RetailVision-HoloSelecta-v2.zip> <new-data-directory>");
    return 2;
}
string? staging = null;
try
{
    var destination = Path.GetFullPath(args[1]);
    if (Path.Exists(destination)) throw new InvalidDataException("Destination must not exist; existing data is never modified.");
    var parent = Path.GetDirectoryName(destination);
    if (parent is null || !Directory.Exists(parent)) throw new InvalidDataException("Destination parent must already exist.");
    await using var source = new FileStream(Path.GetFullPath(args[0]), FileMode.Open, FileAccess.Read, FileShare.Read);
    if (source.Length != 53_294_010 || !Convert.ToHexString(await SHA256.HashDataAsync(source)).Equals(bundleSha, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Not the verified HoloSelecta v2 bundle (size/SHA-256 mismatch).");
    source.Position = 0;
    using var archive = new ZipArchive(source, ZipArchiveMode.Read, true);
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    async Task<byte[]> Read(string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException("Missing bundle entry: " + name);
        using var stream = entry.Open(); using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes); return bytes.ToArray();
    }
    var profile = JsonSerializer.Deserialize<BundleProfile>(await Read("catalog/profile.json"), jsonOptions)
        ?? throw new InvalidDataException("Missing profile.");
    var manifest = JsonSerializer.Deserialize<ModelManifest>(await Read("model/manifest.json"), jsonOptions)
        ?? throw new InvalidDataException("Missing manifest.");
    if (!profile.Products.Select(p => p.Sku).SequenceEqual(manifest.Classes)) throw new InvalidDataException("Catalog/class order mismatch.");
    var planBytes = await Read("sample/planogram.json");
    var plan = JsonSerializer.Deserialize<PlanogramDocument>(planBytes, jsonOptions);
    var errors = PlanogramValidator.Validate(plan, profile.Products.Select(p => p.Sku).ToHashSet());
    if (errors.Count != 0) throw new InvalidDataException(string.Join(" ", errors));
    staging = Path.Combine(parent, ".retailvision-setup-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(staging, "model"));
    Directory.CreateDirectory(Path.Combine(staging, "sample"));
    foreach (var name in new[] { "model/model.onnx", "model/manifest.json", "model/inspection.json", "sample/photo.jpg", "sample/planogram.json", "README.md" })
        await File.WriteAllBytesAsync(Path.Combine(staging, name), await Read(name));
    using (var detector = new OnnxProductDetector(Path.Combine(staging, "model", "manifest.json")))
    {
        using var photo = new MemoryStream(await Read("sample/photo.jpg"));
        var prepared = await PhotoPreprocessor.PrepareAsync(photo, default);
        var result = await detector.DetectAsync(new DetectionRequest(Image: prepared.Png), default);
        if (result.Mode != "REAL" || result.Products.Count == 0) throw new InvalidDataException("Model sample inference did not produce REAL detections.");
    }
    var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = Path.Combine(staging, "retailvision.db"), ForeignKeys = true, Pooling = false };
    await using (var db = new RetailVisionDbContext(new DbContextOptionsBuilder<RetailVisionDbContext>().UseSqlite(connection.ToString()).Options))
    {
        await DatabaseInitializer.InitializeAsync(db);
        using var images = new ProductImageStore(staging);
        var category = new ProductCategory { Name = "HoloSelecta · экспериментальные продукты" };
        var model = new ModelVersion { Name = manifest.Version, Mode = "REAL" };
        for (var i = 0; i < profile.Products.Count; i++)
        {
            var item = profile.Products[i];
            using var image = new MemoryStream(await Read("catalog/images/" + item.Sku + ".png"));
            var product = new Product { Sku = item.Sku, Name = item.Name, Brand = item.Brand, PackageSize = item.PackageSize,
                Color = item.Color, Category = category, ImagePath = await images.SaveAsync(image, default) };
            db.Products.Add(product);
            db.ModelClassMappings.Add(new ModelClassMapping { ModelVersion = model, Product = product, ClassIndex = i });
        }
        await db.SaveChangesAsync();
        var saved = await new PlanogramStore(db).SaveAsync(0, 0, plan, default);
        await File.WriteAllTextAsync(Path.Combine(staging, "setup.json"), JsonSerializer.Serialize(new
        { schemaVersion = 1, bundleSha256 = bundleSha, model = manifest.Version, planogramId = saved.Id,
            photo = "sample/photo.jpg", confidenceThreshold = .5,
            shelves = new[] { new ShelfGeometry(1,0,1,.18,.31), new ShelfGeometry(2,0,1,.31,.39), new ShelfGeometry(3,0,1,.39,.47), new ShelfGeometry(4,0,1,.47,.58) }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }
    // Same-volume move publishes a complete installation; it refuses an existing destination.
    Directory.Move(staging, destination); staging = null;
    Console.WriteLine("Ready: " + destination);
    Console.WriteLine("14 catalog products (6 DEMO + 8 HoloSelecta), 2 planograms, verified REAL model. No accounts or audit results were created.");
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or DbUpdateException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
{
    Console.Error.WriteLine(ex.Message);
    if (staging is not null) Console.Error.WriteLine("Incomplete staging directory retained for inspection: " + staging);
    return 1;
}

internal sealed record BundleProfile(List<BundleProduct> Products);
internal sealed record BundleProduct(string Sku, string Name, string Brand, string PackageSize, string Color);

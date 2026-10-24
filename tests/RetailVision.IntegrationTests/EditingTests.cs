using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using RetailVision.Core;
using RetailVision.Infrastructure;
using SkiaSharp;

namespace RetailVision.IntegrationTests;

public sealed class EditingTests : IDisposable
{
    private readonly AppFactory factory = new();
    private readonly HttpClient client;
    public EditingTests() => client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    public void Dispose() { client.Dispose(); factory.Dispose(); }
    private async Task<string> Token(string path)
    {
        var html = await client.GetStringAsync(path);
        return WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }
    private async Task Login()
    {
        var response = await Form("/account/login", new() { ["Email"] = factory.AdminEmail, ["Password"] = factory.AdminPassword }, "/account/login");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
    private async Task<HttpResponseMessage> Form(string path, Dictionary<string, string> values, string tokenPage)
    {
        values["__RequestVerificationToken"] = await Token(tokenPage);
        return await client.PostAsync(path, new FormUrlEncodedContent(values));
    }
    private static Dictionary<string, string> ProductFields(string sku = "TEST-NEW") => new()
    {
        ["Sku"] = sku, ["Name"] = "Тестовый товар", ["Brand"] = "RetailVision", ["PackageSize"] = "250 мл",
        ["CategoryId"] = "1", ["Color"] = "#123456", ["IsActive"] = "true", ["Revision"] = "1"
    };
    private async Task<int> CreateProduct(string sku = "TEST-NEW")
    {
        var response = await Form("/catalog/create", ProductFields(sku), "/catalog/create");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return int.Parse(response.Headers.Location!.ToString().Split('/').Last());
    }
    private static PlanogramDocument Document() => new()
    {
        Name = "Тестовая планограмма", Store = "Зона А", Description = "Тест",
        Shelves = [new() { Number = 1, Slots = [new() { Position = 1, Sku = "RV-COLA", Facings = 2 }, new() { Position = 2, Sku = "RV-LIME", Facings = 1 }] }, new() { Number = 2 }]
    };
    private async Task<HttpResponseMessage> Save(int id, int version, PlanogramDocument doc, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/planograms/save") { Content = JsonContent.Create(new { id, version, document = doc }) };
        if (csrf) request.Headers.Add("RequestVerificationToken", await Token("/planograms/create"));
        return await client.SendAsync(request);
    }
    private static async Task<(int Id, int Version)> Saved(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (data.RootElement.GetProperty("id").GetInt32(), data.RootElement.GetProperty("version").GetInt32());
    }

    [Theory]
    [InlineData("/catalog/create")]
    [InlineData("/catalog/edit/1")]
    [InlineData("/catalog/delete/1")]
    [InlineData("/catalog/image/1")]
    [InlineData("/planograms/create")]
    [InlineData("/planograms/edit/1")]
    [InlineData("/planograms/export/1")]
    [InlineData("/planograms/import")]
    [InlineData("/planograms/delete/1")]
    public async Task AnonymousCannotReachAdministrativePages(string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.ToString());
    }
    [Fact]
    public async Task AnonymousCannotWriteAndAdminNeedsCsrf()
    {
        Assert.Equal(HttpStatusCode.Redirect, (await Save(0, 0, Document(), false)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/catalog/create", new FormUrlEncodedContent(ProductFields()))).StatusCode);
        await Login();
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(0, 0, Document(), false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/catalog/create", new FormUrlEncodedContent(ProductFields()))).StatusCode);
    }
    [Fact]
    public async Task CatalogCreateEditConflictAndDelete()
    {
        await Login();
        var id = await CreateProduct();
        var fields = ProductFields(); fields["Id"] = id.ToString(); fields["Name"] = "Изменённый товар";
        Assert.Equal(HttpStatusCode.Redirect, (await Form("/catalog/edit/" + id, fields, "/catalog/edit/" + id)).StatusCode);
        fields["Name"] = "Устаревшая запись";
        Assert.Equal(HttpStatusCode.Conflict, (await Form("/catalog/edit/" + id, fields, "/catalog/edit/" + id)).StatusCode);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/catalog/edit/" + id));
        Assert.Contains("Изменённый товар", html);
        Assert.DoesNotContain("Устаревшая запись", html);
        Assert.Equal(HttpStatusCode.Redirect, (await Form("/catalog/delete/" + id, new() { ["revision"] = "2" }, "/catalog/delete/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/catalog/edit/" + id)).StatusCode);
    }
    [Fact]
    public async Task CatalogRejectsDuplicateSkuAndOccupiedClass()
    {
        await Login();
        var fields = ProductFields("RV-COLA");
        var response = await Form("/catalog/create", fields, "/catalog/create");
        Assert.Contains("SKU уже существует", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        fields = ProductFields();
        fields["Mappings[0].ModelVersionId"] = "1"; fields["Mappings[0].ClassIndex"] = "0";
        response = await Form("/catalog/create", fields, "/catalog/create");
        Assert.Contains("уже привязан", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(6, await scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>().Products.CountAsync());
    }
    [Fact]
    public async Task LinkedProductCannotBeDeletedButCanBeArchived()
    {
        await Login();
        var response = await Form("/catalog/delete/1", new() { ["revision"] = "1" }, "/catalog/delete/1");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        response = await Form("/catalog/delete/1", new() { ["revision"] = "1", ["archive"] = "true" }, "/catalog/delete/1");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.DoesNotContain("Cola Original", await anonymous.GetStringAsync("/catalog?status=all"));
        Assert.Contains("Cola Original", await client.GetStringAsync("/catalog?status=inactive&display=table"));
        // A retired SKU can be retained in its existing plan but cannot enter a new plan.
        var existing = await client.GetFromJsonAsync<PlanogramDocument>("/planograms/export/1");
        Assert.Equal(HttpStatusCode.OK, (await Save(1, 1, existing!)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Save(0, 0, existing!)).StatusCode);
        // Demo is frozen independently from the editable catalog.
        Assert.Equal(HttpStatusCode.OK, (await Form("/audit/demo", new() { ["scenario"] = "correct" }, "/audit")).StatusCode);
    }
    [Fact]
    public async Task PlanogramSaveRoundtripReorderAndStaleVersionAreAtomic()
    {
        await Login();
        var doc = Document();
        var first = await Saved(await Save(0, 0, doc));
        doc.Shelves[0].Slots.Reverse();
        for (var i = 0; i < doc.Shelves[0].Slots.Count; i++) doc.Shelves[0].Slots[i].Position = i + 1;
        doc.Shelves[1].Slots.Add(doc.Shelves[0].Slots[1]); doc.Shelves[0].Slots.RemoveAt(1); doc.Shelves[1].Slots[0].Position = 1;
        var second = await Saved(await Save(first.Id, first.Version, doc));
        Assert.Equal(2, second.Version);
        doc.Name = "Stale";
        Assert.Equal(HttpStatusCode.Conflict, (await Save(first.Id, first.Version, doc)).StatusCode);
        var actual = await client.GetFromJsonAsync<PlanogramDocument>("/planograms/export/" + first.Id);
        Assert.Equal("Тестовая планограмма", actual!.Name);
        Assert.Equal("RV-LIME", actual.Shelves[0].Slots[0].Sku);
        Assert.Equal("RV-COLA", actual.Shelves[1].Slots[0].Sku);
        doc.Name = "Invalid"; doc.Shelves[0].Slots[0].Sku = "DOES-NOT-EXIST";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Save(first.Id, 2, doc)).StatusCode);
        var after = await client.GetFromJsonAsync<PlanogramDocument>("/planograms/export/" + first.Id);
        Assert.Equal(JsonSerializer.Serialize(actual), JsonSerializer.Serialize(after));
    }
    private async Task<HttpResponseMessage> Import(string json)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(await Token("/planograms/import")), "__RequestVerificationToken");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(json)), "File", "plan.json");
        return await client.PostAsync("/planograms/import", form);
    }
    [Fact]
    public async Task ImportExportAndDuplicateCreateIndependentPlans()
    {
        await Login();
        var first = await Saved(await Save(0, 0, Document()));
        var json = await client.GetStringAsync("/planograms/export/" + first.Id);
        var imported = await Import(json);
        Assert.Equal(HttpStatusCode.Redirect, imported.StatusCode);
        var importedId = int.Parse(imported.Headers.Location!.ToString().Split('/').Last());
        Assert.NotEqual(first.Id, importedId);
        var actual = await client.GetFromJsonAsync<PlanogramDocument>("/planograms/export/" + importedId);
        Assert.Equal(JsonSerializer.Serialize(Document()), JsonSerializer.Serialize(actual));
        var duplicate = await Form("/planograms/duplicate/" + first.Id, new() { ["version"] = "1" }, "/planograms");
        Assert.Equal(HttpStatusCode.Redirect, duplicate.StatusCode);
        var duplicateId = int.Parse(duplicate.Headers.Location!.ToString().Split('/').Last());
        Assert.NotEqual(importedId, duplicateId);
        Assert.Equal(HttpStatusCode.Redirect, (await Form("/planograms/delete/" + first.Id, new() { ["version"] = "1" }, "/planograms/delete/" + first.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/planograms/edit/" + duplicateId)).StatusCode);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{broken")]
    [InlineData("""{"schemaVersion":2,"name":"Bad","shelves":[{"number":1}]}""")]
    [InlineData("""{"schemaVersion":1,"name":"Bad","unexpected":true,"shelves":[{"number":1}]}""")]
    [InlineData("""{"name":"Bad","shelves":[{"number":1,"slots":[{"position":1,"sku":"UNKNOWN","facings":1}]}]}""")]
    public async Task ImportRejectsInvalidDataWithoutWrites(string json)
    {
        await Login();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Import(json)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>().Planograms.CountAsync());
    }
    private async Task<HttpResponseMessage> Upload(byte[] bytes, string filename)
    {
        using var form = new MultipartFormDataContent();
        foreach (var field in ProductFields()) form.Add(new StringContent(field.Value), field.Key);
        form.Add(new StringContent(await Token("/catalog/create")), "__RequestVerificationToken");
        form.Add(new ByteArrayContent(bytes), "Image", filename);
        return await client.PostAsync("/catalog/create", form);
    }
    [Fact]
    public async Task UploadedImageIsReencodedPrivateAndDeletedWithProduct()
    {
        await Login();
        using var bitmap = new SKBitmap(20, 10); bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        var response = await Upload(jpeg.ToArray(), "../../misleading.svg");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var id = int.Parse(response.Headers.Location!.ToString().Split('/').Last());
        var photo = await client.GetAsync("/catalog/image/" + id);
        Assert.Equal("image/png", photo.Content.Headers.ContentType!.MediaType);
        Assert.True(photo.Headers.CacheControl!.NoStore);
        var bytes = await photo.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, bytes[..4]);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/catalog/image/" + id)).StatusCode);
        string? path;
        using (var scope = factory.Services.CreateScope())
        {
            var product = await scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>().Products.FindAsync(id);
            path = scope.ServiceProvider.GetRequiredService<ProductImageStore>().Resolve(product!.ImagePath);
            Assert.True(File.Exists(path));
        }
        Assert.Equal(HttpStatusCode.Redirect, (await Form("/catalog/delete/" + id, new() { ["revision"] = "1" }, "/catalog/delete/" + id)).StatusCode);
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task SvgAndOversizedDimensionsAreRejected()
    {
        await Login();
        Assert.Equal(HttpStatusCode.OK, (await Upload(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"), "test.png")).StatusCode);
        using var bitmap = new SKBitmap(4097, 1); bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var response = await Upload(png.ToArray(), "test.png");
        Assert.Contains("4096", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(6, await scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>().Products.CountAsync());
    }
    [Fact]
    public async Task UpgradePreservesExistingCatalogAndInitializesRevision()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new RetailVisionDbContext(new DbContextOptionsBuilder<RetailVisionDbContext>().UseSqlite(connection).Options);
        var initial = db.Database.GetMigrations().First();
        await db.GetService<IMigrator>().MigrateAsync(initial);
        await db.Database.ExecuteSqlRawAsync("""INSERT INTO Categories (Id,Name) VALUES (1,'Existing');""");
        await db.Database.ExecuteSqlRawAsync("""INSERT INTO Products (Sku,Name,Brand,PackageSize,Color,CategoryId,CreatedAtUtc,IsActive) VALUES ('OLD','Existing','Test','1','#123456',1,'2026-10-23',1);""");
        await DatabaseInitializer.InitializeAsync(db);
        var product = Assert.Single(await db.Products.ToListAsync());
        Assert.Equal("OLD", product.Sku);
        Assert.Equal(1, product.Revision);
        Assert.Single(await db.Set<SeedMarker>().ToListAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
    [Fact]
    public async Task EmptyCatalogStaysEmptyOnRestart()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>();
        db.Planograms.RemoveRange(db.Planograms); db.ModelClassMappings.RemoveRange(db.ModelClassMappings);
        await db.SaveChangesAsync();
        db.Products.RemoveRange(db.Products); await db.SaveChangesAsync();
        await DatabaseInitializer.InitializeAsync(db);
        Assert.Equal(0, await db.Products.CountAsync());
        Assert.Equal(0, await db.Planograms.CountAsync());
    }
}

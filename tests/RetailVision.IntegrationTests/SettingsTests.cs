using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailVision.Infrastructure;
using RetailVision.Vision;
using RetailVision.Web;

namespace RetailVision.IntegrationTests;

public sealed class SettingsTests
{
    [Fact]
    public void RejectedModelContractDisablesRealModeWithoutCrashingSettings()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Vision:ManifestPath"] = Path.Combine(AppContext.BaseDirectory, "Fixtures", "manifest.json") }).Build();
        using var vision = new PhotoVisionService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<PhotoVisionService>.Instance);
        Assert.False(vision.Ready);
        Assert.Empty(vision.Skus);
        Assert.Null(vision.ModelVersion);
        Assert.Contains("Модель недоступна", vision.Message);
    }
    private static async Task Login(HttpClient client, AppFactory factory)
    {
        var page = await client.GetStringAsync("/account/login");
        var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Email"] = factory.AdminEmail, ["Password"] = factory.AdminPassword, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("private-model-location/missing-manifest.json")]
    public async Task UnavailableModelShowsHonestStatusWithoutLeakingConfiguration(string path)
    {
        using var factory = new AppFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Vision:ManifestPath"] = path })));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Login(client, factory);
        var response = await client.GetAsync("/home/settings");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains("Недоступна", html);
        Assert.DoesNotContain("В разработке", html);
        Assert.DoesNotContain("private-model-location", html);
        Assert.DoesNotContain(factory.AdminPassword, html);
    }

    [Fact]
    public async Task LoadedModelReportsActualCatalogCoverageAndKeepsDemoSeparate()
    {
        using var factory = new AppFactory();
        using var detector = new OnnxProductDetector(Path.Combine(AppContext.BaseDirectory, "Fixtures", "manifest.json"), true);
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<PhotoVisionService>(); services.AddSingleton(new PhotoVisionService(detector)); }));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Login(client, factory);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>();
            var products = await db.Products.Where(p => detector.Manifest.Classes.Contains(p.Sku)).ToListAsync();
            Assert.NotEmpty(products);
            products[0].IsActive = false;
            // Rename another SKU to simulate a missing manifest class without deleting related data.
            Assert.True(products.Count >= 2);
            products[1].Sku = "TEST-UNSUPPORTED";
            await db.SaveChangesAsync();
        }
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/home/settings"));
        Assert.Contains(detector.Manifest.Version, html);
        Assert.Contains(detector.Manifest.Sha256, html);
        Assert.Contains("Отключён", html);
        Assert.Contains("Отсутствует", html);
        foreach (var sku in detector.Manifest.Classes) Assert.Contains(sku, html);
        var demo = WebUtility.HtmlDecode(await client.GetStringAsync("/audit"));
        Assert.Contains("Демонстрационные данные — нейросеть не использовалась", demo);
        Assert.DoesNotContain("модель пока не подключена", demo);
    }
}

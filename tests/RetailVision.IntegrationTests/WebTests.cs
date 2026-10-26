using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailVision.Infrastructure;

namespace RetailVision.IntegrationTests;

public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "retailvision-tests-" + Guid.NewGuid());
    public string AdminEmail { get; } = "integration@example.invalid";
    public string AdminPassword { get; } = Guid.NewGuid().ToString("N") + "!aA7";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:DataDirectory"] = directory,
            ["Admin:Email"] = AdminEmail,
            ["Admin:Password"] = AdminPassword
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<RetailVisionDbContext>>();
            services.AddDbContext<RetailVisionDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(directory, "test.db")};Foreign Keys=True"));
        });
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

public sealed class WebTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task<string> Token(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }
    [Theory]
    [InlineData("/")]
    [InlineData("/audit")]
    [InlineData("/catalog")]
    [InlineData("/planograms")]
    [InlineData("/account/login")]
    [InlineData("/health")]
    public async Task PublicPagesAreAvailable(string path)
    {
        using var client = Client();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    [Theory]
    [InlineData("correct", "100")]
    [InlineData("missing", "87,5")]
    [InlineData("sequence", "75")]
    [InlineData("replacement", "87,5")]
    [InlineData("wrong-shelf", "75")]
    [InlineData("extra", "88,9")]
    [InlineData("uncertain", "87,5")]
    public async Task DemoRunsWithoutPersistingVisitorData(string scenario, string expected)
    {
        using var client = Client();
        var token = await Token(client, "/audit");
        var response = await client.PostAsync("/audit/demo", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["scenario"] = scenario, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"data-testid=\"compliance\">{expected}", html);
        Assert.Contains("нейросеть не использовалась", html);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>().Audits.CountAsync());
    }
    [Fact]
    public async Task CsrfIsRequired()
    {
        using var client = Client();
        var response = await client.PostAsync("/audit/demo", new FormUrlEncodedContent(new Dictionary<string, string> { ["scenario"] = "correct" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task InvalidFixtureIsRejected()
    {
        using var client = Client();
        var token = await Token(client, "/audit");
        var response = await client.PostAsync("/audit/demo", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["scenario"] = "../../arbitrary.jpg", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task AnonymousCannotAccessSettings()
    {
        using var client = Client();
        var response = await client.GetAsync("/home/settings");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.ToString());
    }
    [Fact]
    public async Task AdminLoginRejectsExternalReturnUrl()
    {
        using var client = Client();
        var token = await Token(client, "/account/login");
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = factory.AdminEmail,
            ["Password"] = factory.AdminPassword,
            ["ReturnUrl"] = "https://evil.example",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/home/settings", response.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/home/settings")).StatusCode);
    }
    [Fact]
    public async Task MigrationsAndSeedAreIdempotent()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>();
        await DatabaseInitializer.InitializeAsync(db);
        await DatabaseInitializer.InitializeAsync(db);
        Assert.Equal(6, await db.Products.CountAsync());
        Assert.Equal(1, await db.Planograms.CountAsync());
        Assert.Equal(4, (await db.Database.GetAppliedMigrationsAsync()).Count());
        var mappings = await db.ModelClassMappings.OrderBy(m => m.ClassIndex).ToListAsync();
        Assert.Equal(0, mappings[0].ClassIndex);
        Assert.NotEqual(mappings[0].ClassIndex, mappings[0].ProductId);
    }
    [Fact]
    public async Task CatalogSearchFiltersProducts()
    {
        using var client = Client();
        var html = await client.GetStringAsync("/catalog?search=RV-COLA");
        Assert.Contains("Cola Original", html);
        Assert.DoesNotContain("Lime Fresh", html);
    }
    [Fact]
    public async Task RateLimitBoundsPublicOperations()
    {
        using var isolated = new AppFactory();
        using var client = isolated.CreateClient();
        HttpStatusCode status = HttpStatusCode.OK;
        for (var i = 0; i < 31; i++) status = (await client.GetAsync("/audit")).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, status);
    }
}

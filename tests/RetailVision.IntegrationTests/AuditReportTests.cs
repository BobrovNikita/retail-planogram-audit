using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailVision.Core;

namespace RetailVision.IntegrationTests;

public sealed class AuditReportTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static async Task<string> Run(HttpClient client, string scenario)
    {
        var form = await client.GetStringAsync("/audit");
        var token = WebUtility.HtmlDecode(Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var response = await client.PostAsync("/audit/demo", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["scenario"] = scenario, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("correct", "100", "100", "100", 0, 0, 8)]
    [InlineData("missing", "87,5", "83,3", "87,5", 1, 0, 8)]
    [InlineData("sequence", "75", "100", "100", 2, 0, 8)]
    [InlineData("replacement", "87,5", "83,3", "77,8", 1, 0, 8)]
    [InlineData("wrong-shelf", "75", "100", "60", 2, 0, 8)]
    [InlineData("extra", "88,9", "100", "88,9", 1, 0, 9)]
    [InlineData("uncertain", "87,5", "83,3", "77,8", 0, 1, 8)]
    public async Task ReportsMetricsMatchesAndResolvableHighlightTargets(string scenario, string positionPercent,
        string skuPercent, string facingPercent, int confirmed, int review, int rows)
    {
        using var client = factory.CreateClient();
        var html = await Run(client, scenario);
        Assert.Contains($"data-testid=\"compliance\">{positionPercent}<", html);
        Assert.Contains($"data-testid=\"sku-presence\">{skuPercent}<", html);
        Assert.Contains($"data-testid=\"facings\">{facingPercent}<", html);
        Assert.Contains($"data-testid=\"confirmed\">{confirmed}<", html);
        Assert.Contains($"data-testid=\"review\">{review}<", html);
        Assert.Contains("fixture-v2", html);
        var table = Regex.Match(html, """<table[^>]+data-testid="position-matches".*?</table>""", RegexOptions.Singleline).Value;
        Assert.Equal(rows + 1, Regex.Matches(table, "<tr>").Count);
        var ids = Regex.Matches(html, "\\bid=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        foreach (Match match in Regex.Matches(html, "data-highlight=\"([^\"]+)\""))
            Assert.Contains(match.Groups[1].Value, ids);
        if (scenario == "uncertain")
        {
            Assert.Contains("id=\"fixture-2\" class=\"detection box-unknown\"", html);
            Assert.DoesNotContain(">Товар не обнаружен", table);
        }
        if (scenario == "missing") Assert.Contains("data-highlight=\"expected-1-3\"", html);
        if (scenario == "replacement") Assert.Contains("id=\"fixture-2\" class=\"detection box-error\"", html);
    }
    [Fact]
    public async Task ReportHandlesNullSkuWithoutInventingAProduct()
    {
        using var isolated = new AppFactory();
        using var app = isolated.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProductDetector>();
            services.AddSingleton<IProductDetector, UnknownDetector>();
        }));
        using var client = app.CreateClient();
        var html = await Run(client, "uncertain");
        Assert.Contains("SKU неизвестен", html);
        Assert.Contains("detection box-unknown", html);
    }
    private sealed class UnknownDetector : IProductDetector
    {
        public Task<DetectionResult> DetectAsync(DetectionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DetectionResult("DEMO", "test-unknown", [new("unmapped", null, .9, 1, .53, .12, .1, .3)]));
    }
}

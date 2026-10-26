using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailVision.Web;

namespace RetailVision.IntegrationTests;

public sealed class DeploymentTests
{
    [Theory]
    [InlineData("172.30.85.2", true, "https", "203.0.113.7")]
    [InlineData("::ffff:172.30.85.2", true, "https", "203.0.113.7")]
    [InlineData("172.30.85.99", true, "http", "172.30.85.99")]
    [InlineData("127.0.0.1", true, "http", "127.0.0.1")]
    [InlineData("172.30.85.2", false, "http", "172.30.85.2")]
    public async Task OnlyExplicitProxyCanForwardSchemeAndClientIp(string remote, bool configured, string scheme, string client)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configured
            ? new Dictionary<string, string?> { ["Proxy:KnownProxies:0"] = "172.30.85.2" } : []).Build();
        var options = new ForwardedHeadersOptions();
        ReverseProxyConfiguration.Configure(options, configuration);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("retail.example");
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
        context.Request.Headers["X-Forwarded-Host"] = "attacker.example";
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        await middleware.Invoke(context);
        Assert.Equal(scheme, context.Request.Scheme);
        Assert.Equal(client, context.Connection.RemoteIpAddress!.ToString());
        Assert.Equal("retail.example", context.Request.Host.Value);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("172.30.85.0/24")]
    public void ProxyConfigurationRejectsBroadOrInvalidTrust(string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["Proxy:KnownProxies:0"] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => ReverseProxyConfiguration.Configure(new(), config));
    }

    [Fact]
    public async Task ProductionHealthWorksOverHttpButPagesRedirectDespiteForgedHeaders()
    {
        using var factory = new AppFactory();
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://localhost/", response.Headers.Location!.AbsoluteUri);
    }

    [Fact]
    public async Task BootstrapReadsSecretFileWithoutTrimmingPasswordSpaces()
    {
        var file = Path.GetTempFileName();
        try
        {
            using var factory = new AppFactory();
            var password = " " + factory.AdminPassword + " ";
            await File.WriteAllTextAsync(file, password + "\n");
            using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Password"] = null, ["Admin:PasswordFile"] = file })));
            using var client = configured.CreateClient();
            using var scope = configured.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
            var user = await users.FindByEmailAsync(factory.AdminEmail);
            Assert.NotNull(user);
            Assert.True(await users.CheckPasswordAsync(user, password));
            Assert.False(await users.CheckPasswordAsync(user, password.Trim()));
        }
        finally { File.Delete(file); }
    }
}

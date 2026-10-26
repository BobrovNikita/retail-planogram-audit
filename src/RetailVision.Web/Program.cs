using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RetailVision.Core;
using RetailVision.Infrastructure;
using RetailVision.Vision;
using RetailVision.Web;

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.GetValue<bool>("ASPNETCORE_FORWARDEDHEADERS_ENABLED"))
    throw new InvalidOperationException("Use Proxy:KnownProxies instead of ASPNETCORE_FORWARDEDHEADERS_ENABLED.");
builder.Services.Configure<ForwardedHeadersOptions>(options => ReverseProxyConfiguration.Configure(options, builder.Configuration));
builder.Services.AddHttpsRedirection(options => options.HttpsPort = builder.Configuration.GetValue<int?>("Proxy:HttpsPort") ?? 443);
var dataPath = Path.GetFullPath(builder.Configuration["Storage:DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
Directory.CreateDirectory(dataPath);
builder.Services.AddDbContext<RetailVisionDbContext>(options => options.UseSqlite(
    new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(dataPath, "retailvision.db"), ForeignKeys = true }.ToString()));
builder.Services.AddDataProtection().SetApplicationName("RetailVision")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
}).AddEntityFrameworkStores<RetailVisionDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/denied";
    options.Cookie.Name = "RetailVision.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddAuthorizationBuilder().AddPolicy("Admin", policy => policy.RequireRole("Administrator"));
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddSingleton<IComplianceAnalyzer, ComplianceAnalyzer>();
builder.Services.AddScoped<PlanogramStore>();
builder.Services.AddSingleton(new RetentionPolicy(builder.Configuration.GetValue("Retention:TrashDays", 30), builder.Configuration.GetValue("Retention:AutoPurge", false)));
builder.Services.AddScoped(sp => new AuditRetention(sp.GetRequiredService<RetailVisionDbContext>(), sp.GetRequiredService<RetentionPolicy>(), dataPath, sp.GetRequiredService<ILogger<AuditRetention>>()));
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddScoped(sp => new AuditStore(sp.GetRequiredService<RetailVisionDbContext>(), sp.GetRequiredService<IComplianceAnalyzer>(),
    sp.GetRequiredService<IConfiguration>()["Storage:DataDirectory"] ?? dataPath));
builder.Services.AddSingleton<IProductDetector, FixtureProductDetector>();
builder.Services.AddSingleton<PhotoVisionService>();
builder.Services.AddSingleton(sp => new PhotoDraftStore(sp.GetRequiredService<IConfiguration>()["Storage:DataDirectory"] ?? dataPath));
builder.Services.AddSingleton(sp => new ProductImageStore(
    sp.GetRequiredService<IConfiguration>()["Storage:DataDirectory"] ?? dataPath));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("photo", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("public", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Logging.AddJsonConsole();
var app = builder.Build();
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
using (var scope = app.Services.CreateScope())
{
    await DatabaseInitializer.InitializeAsync(scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>());
    await AdminBootstrap.InitializeAsync(scope.ServiceProvider, app.Configuration);
}
app.UseExceptionHandler("/home/error");
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    // The container probes only this minimal database readiness endpoint over its own loopback.
    app.UseWhen(context => context.Request.Path != "/health", branch => branch.UseHttpsRedirection());
}
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();
public partial class Program;

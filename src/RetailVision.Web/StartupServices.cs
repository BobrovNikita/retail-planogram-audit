using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RetailVision.Infrastructure;

namespace RetailVision.Web;

public sealed class DatabaseHealthCheck(RetailVisionDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
}

internal static class AdminBootstrap
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["Admin:Email"];
        if (string.IsNullOrWhiteSpace(email)) return;
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        if (await users.FindByEmailAsync(email) is not null) return;
        var password = configuration["Admin:Password"];
        var passwordFile = configuration["Admin:PasswordFile"];
        if (!string.IsNullOrWhiteSpace(passwordFile))
        {
            if (!string.IsNullOrEmpty(password)) throw new InvalidOperationException("Configure only one administrator password source.");
            var info = new FileInfo(passwordFile);
            if (!info.Exists || info.Length > 4096) throw new InvalidOperationException("Administrator secret file is missing or too large.");
            password = (await File.ReadAllTextAsync(passwordFile)).TrimEnd('\r', '\n');
        }
        if (string.IsNullOrWhiteSpace(password)) return;
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Administrator")) Ensure(await roles.CreateAsync(new IdentityRole("Administrator")));
        var user = new IdentityUser { UserName = email, Email = email };
        Ensure(await users.CreateAsync(user, password));
        Ensure(await users.AddToRoleAsync(user, "Administrator"));
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Administrator bootstrap failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
    }
}

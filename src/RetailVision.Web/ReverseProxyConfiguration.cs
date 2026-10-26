using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace RetailVision.Web;

public static class ReverseProxyConfiguration
{
    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        var addresses = configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [];
        options.ForwardedHeaders = ForwardedHeaders.None;
        // Do not clear the framework's trust lists unless a nonempty, validated allowlist exists.
        if (addresses.Length == 0) return;
        var parsed = addresses.Select(value => IPAddress.TryParse(value, out var address)
            && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any)
                ? address : throw new InvalidOperationException("Proxy:KnownProxies must contain explicit IP addresses.")).ToArray();
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var address in parsed)
        {
            options.KnownProxies.Add(address);
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                options.KnownProxies.Add(address.MapToIPv6());
        }
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.RequireHeaderSymmetry = true;
    }
}

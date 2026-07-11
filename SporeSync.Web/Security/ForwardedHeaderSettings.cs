using System.Net;
using Microsoft.AspNetCore.Builder;
using ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders;

namespace SporeSync.Web.Security;

public sealed class ForwardedHeaderSettings
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; set; }
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = [];
    public int ForwardLimit { get; set; } = 1;

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (ForwardLimit < 1)
        {
            throw new InvalidOperationException("ForwardedHeaders:ForwardLimit must be at least 1.");
        }

        if (KnownProxies.Length == 0 && KnownNetworks.Length == 0)
        {
            throw new InvalidOperationException(
                "ForwardedHeaders is enabled, but no trusted proxy or network is configured. " +
                "Set ForwardedHeaders:KnownProxies or ForwardedHeaders:KnownNetworks explicitly.");
        }

        _ = KnownProxies.Select(ParseIpAddress).ToArray();
        _ = KnownNetworks.Select(ParseNetwork).ToArray();
    }

    public void Configure(ForwardedHeadersOptions options)
    {
        Validate();

        if (!Enabled)
        {
            return;
        }

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = ForwardLimit;
        options.RequireHeaderSymmetry = true;

        options.KnownProxies.Clear();
        foreach (var proxy in KnownProxies.Select(ParseIpAddress))
        {
            options.KnownProxies.Add(proxy);
        }

        options.KnownIPNetworks.Clear();
        foreach (var network in KnownNetworks.Select(ParseNetwork))
        {
            options.KnownIPNetworks.Add(network);
        }
    }

    private static IPAddress ParseIpAddress(string value)
    {
        if (IPAddress.TryParse(value, out var address))
        {
            return address;
        }

        throw new InvalidOperationException($"ForwardedHeaders contains an invalid IP address: '{value}'.");
    }

    private static IPNetwork ParseNetwork(string value)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var prefixLength))
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders contains an invalid CIDR network: '{value}'. Use CIDR notation such as '172.18.0.0/16'.");
        }

        var maxPrefixLength = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxPrefixLength)
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders contains an invalid CIDR prefix length for '{value}'.");
        }

        return new IPNetwork(prefix, prefixLength);
    }
}

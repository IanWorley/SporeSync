using System.Net;
using Microsoft.AspNetCore.Builder;
using ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders;
using SporeSync.Web.Security;

namespace SporeSync.Business.Tests;

public sealed class ForwardedHeaderSettingsTests
{
    [Fact]
    public void Configure_TrustsOnlyExplicitProxiesAndNetworks()
    {
        var settings = new ForwardedHeaderSettings
        {
            Enabled = true,
            KnownProxies = ["10.0.0.10"],
            KnownNetworks = ["172.18.0.0/16"],
            ForwardLimit = 1
        };
        var options = new ForwardedHeadersOptions();

        settings.Configure(options);

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.True(options.RequireHeaderSymmetry);
        Assert.Equal(IPAddress.Parse("10.0.0.10"), Assert.Single(options.KnownProxies));

        var network = Assert.Single(options.KnownIPNetworks);
        Assert.Equal(IPAddress.Parse("172.18.0.0"), network.BaseAddress);
        Assert.Equal(16, network.PrefixLength);
    }

    [Fact]
    public void Validate_RejectsEnabledConfigurationWithoutTrustedProxyOrNetwork()
    {
        var settings = new ForwardedHeaderSettings { Enabled = true };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);

        Assert.Contains("no trusted proxy or network", exception.Message);
    }

    [Fact]
    public void Validate_RejectsInvalidKnownProxy()
    {
        var settings = new ForwardedHeaderSettings
        {
            Enabled = true,
            KnownProxies = ["not-an-ip"]
        };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);

        Assert.Contains("invalid IP address", exception.Message);
    }

    [Fact]
    public void Validate_RejectsInvalidKnownNetwork()
    {
        var settings = new ForwardedHeaderSettings
        {
            Enabled = true,
            KnownNetworks = ["172.18.0.0"]
        };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);

        Assert.Contains("invalid CIDR network", exception.Message);
    }
}

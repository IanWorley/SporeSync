using SporeSync.Web.Health;

namespace SporeSync.Business.Tests;

[Collection("Environment variables")]
public sealed class HealthProbeTests : IDisposable
{
    private readonly string? _originalAspNetCoreHttpPorts = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
    private readonly string? _originalAspNetCoreUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

    [Theory]
    [InlineData("http://localhost:5101", "5101")]
    [InlineData("http://+:5102", "5102")]
    [InlineData("http://*:5103", "5103")]
    [InlineData("http://0.0.0.0:5104", "5104")]
    [InlineData("http://[::]:5105", "5105")]
    [InlineData(" https://localhost:5106 ; http://localhost:5107 ", "5106")]
    [InlineData("http://localhost:5108/healthz/ready", "5108")]
    public void ResolveDefaultUrl_UsesAspNetCoreUrlsPort(string urls, string expectedPort)
    {
        SetHealthProbeEnvironment(urls: urls, httpPorts: null);

        var url = HealthProbe.ResolveDefaultUrl();

        Assert.Equal($"http://localhost:{expectedPort}/healthz/ready", url);
    }

    [Theory]
    [InlineData("http://localhost:5201", "6201", "5201")]
    [InlineData("http://*:5202;http://localhost:5203", "6202;6203", "5202")]
    public void ResolveDefaultUrl_AspNetCoreUrlsWinsOverAspNetCoreHttpPorts(
        string urls,
        string httpPorts,
        string expectedPort)
    {
        SetHealthProbeEnvironment(urls, httpPorts);

        var url = HealthProbe.ResolveDefaultUrl();

        Assert.Equal($"http://localhost:{expectedPort}/healthz/ready", url);
    }

    [Theory]
    [InlineData("6301", "6301")]
    [InlineData(" 6302 ; 6303 ", "6302")]
    public void ResolveDefaultUrl_FallsBackToAspNetCoreHttpPorts(string httpPorts, string expectedPort)
    {
        SetHealthProbeEnvironment(urls: null, httpPorts: httpPorts);

        var url = HealthProbe.ResolveDefaultUrl();

        Assert.Equal($"http://localhost:{expectedPort}/healthz/ready", url);
    }

    [Fact]
    public void ResolveDefaultUrl_UsesDefaultPortWhenNoBindingEnvironmentIsSet()
    {
        SetHealthProbeEnvironment(urls: null, httpPorts: null);

        var url = HealthProbe.ResolveDefaultUrl();

        Assert.Equal("http://localhost:8080/healthz/ready", url);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", _originalAspNetCoreHttpPorts);
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", _originalAspNetCoreUrls);
    }

    private static void SetHealthProbeEnvironment(string? urls, string? httpPorts)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", urls);
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", httpPorts);
    }
}

[CollectionDefinition("Environment variables", DisableParallelization = true)]
public sealed class EnvironmentVariableCollection;

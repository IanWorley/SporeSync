namespace SporeSync.Web.Health;

using System.Globalization;

/// <summary>
/// In-process HTTP health probe used as the container HEALTHCHECK command
/// (<c>dotnet SporeSync.Web.dll healthcheck</c>), because the ASP.NET Core
/// base images do not ship curl or wget.
/// </summary>
internal static class HealthProbe
{
    public static async Task<int> RunAsync(string? url)
    {
        var target = url ?? ResolveDefaultUrl();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            using var response = await client.GetAsync(target);
            if (response.IsSuccessStatusCode)
            {
                return 0;
            }

            Console.Error.WriteLine($"Health probe to {target} returned {(int)response.StatusCode}.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Health probe to {target} failed: {ex.Message}");
            return 1;
        }
    }

    internal static string ResolveDefaultUrl()
    {
        var port = ResolvePort();
        return $"http://localhost:{port}/healthz/ready";
    }

    private static string ResolvePort()
    {
        if (TryResolvePortFromUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"), out var urlsPort))
        {
            return urlsPort;
        }

        var httpPorts = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
        var firstPort = httpPorts?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstPort))
        {
            return firstPort;
        }

        return "8080";
    }

    private static bool TryResolvePortFromUrls(string? urls, out string port)
    {
        port = string.Empty;

        foreach (var url in urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
        {
            if (TryResolvePortFromUrl(url, out port))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolvePortFromUrl(string url, out string port)
    {
        port = string.Empty;

        if (!Uri.TryCreate(NormalizeBindingUrl(url), UriKind.Absolute, out var uri))
        {
            return false;
        }

        port = uri.Port.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static string NormalizeBindingUrl(string url)
    {
        var schemeSeparator = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0)
        {
            return url;
        }

        var authorityStart = schemeSeparator + 3;
        var authorityEnd = url.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = url.Length;
        }

        var authority = url[authorityStart..authorityEnd];
        var hostEnd = authority.StartsWith("[", StringComparison.Ordinal)
            ? authority.IndexOf(']') + 1
            : authority.IndexOf(':');

        if (hostEnd <= 0)
        {
            hostEnd = authority.Length;
        }

        var host = authority[..hostEnd];
        var normalizedHost = host switch
        {
            "+" or "*" or "0.0.0.0" or "[::]" => "localhost",
            _ => host
        };

        return string.Concat(url.AsSpan(0, authorityStart), normalizedHost, authority.AsSpan(hostEnd), url.AsSpan(authorityEnd));
    }
}

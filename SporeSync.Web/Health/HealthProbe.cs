namespace SporeSync.Web.Health;

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

    private static string ResolveDefaultUrl()
    {
        var port = ResolvePort();
        return $"http://localhost:{port}/healthz/ready";
    }

    private static string ResolvePort()
    {
        var httpPorts = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
        var firstPort = httpPorts?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstPort))
        {
            return firstPort;
        }

        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        var firstUrl = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstUrl)
            && Uri.TryCreate(firstUrl.Replace("+", "localhost").Replace("*", "localhost"), UriKind.Absolute, out var uri))
        {
            return uri.Port.ToString();
        }

        return "8080";
    }
}

using Testcontainers.PostgreSql;

namespace SporeSync.Web;

public sealed class TestcontainerDatabase : IAsyncDisposable
{
    private const string ConfigurationSection = "Testcontainers:PostgreSql";

    private readonly PostgreSqlContainer _container;

    private TestcontainerDatabase(PostgreSqlContainer container)
    {
        _container = container;
    }

    public static async Task<TestcontainerDatabase?> StartIfEnabledAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>($"{ConfigurationSection}:Enabled"))
        {
            return null;
        }

        var image = configuration[$"{ConfigurationSection}:Image"] ?? "postgres:16-alpine";
        var container = new PostgreSqlBuilder(image)
            .WithDatabase(configuration[$"{ConfigurationSection}:Database"] ?? "SporeSync")
            .WithUsername(configuration[$"{ConfigurationSection}:Username"] ?? "sporesync")
            .WithPassword(configuration[$"{ConfigurationSection}:Password"] ?? "sporesync")
            .Build();

        await container.StartAsync(cancellationToken);

        configuration["ConnectionStrings:DefaultConnection"] = container.GetConnectionString();

        return new TestcontainerDatabase(container);
    }

    public ValueTask DisposeAsync()
    {
        return _container.DisposeAsync();
    }
}

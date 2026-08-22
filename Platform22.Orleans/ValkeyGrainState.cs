namespace Platform22.Orleans;

using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

internal sealed class ValkeyGrainState
{
    private static readonly object Sync = new();
    private static string? configuredConnectionString;
    private static ConnectionMultiplexer? connection;
    private readonly IDatabase? database;
    private readonly string key;
    private string? memoryValue;

    public ValkeyGrainState(IConfiguration configuration, string key)
    {
        this.key = key;
        var connectionString = configuration.GetConnectionString("valkey")
            ?? configuration["ConnectionStrings:valkey"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            database = GetConnection(connectionString).GetDatabase();
        }
    }

    public async Task SetAsync(string value)
    {
        memoryValue = value;
        if (database is not null)
        {
            await database.StringSetAsync(key, value).ConfigureAwait(false);
        }
    }

    public async Task<string?> GetAsync()
    {
        if (database is null)
        {
            return memoryValue;
        }

        var value = await database.StringGetAsync(key).ConfigureAwait(false);
        return value.HasValue ? value.ToString() : memoryValue;
    }

    private static ConnectionMultiplexer GetConnection(string connectionString)
    {
        lock (Sync)
        {
            if (connection is null || configuredConnectionString != connectionString)
            {
                connection?.Dispose();
                connection = ConnectionMultiplexer.Connect(connectionString);
                configuredConnectionString = connectionString;
            }

            return connection;
        }
    }
}

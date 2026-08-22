namespace Platform22.Tui;

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaulsTransitData.Models;
using PaulsTransitData.Providers.Translink;
using Platform22.Orleans;

public sealed class TranslinkMapActor : ITransitMapClient, IAsyncDisposable
{
    private const string DirectoryKey = "translink";
    private static readonly TimeSpan SharedRefreshInterval = TimeSpan.FromSeconds(25);
    private readonly TranslinkPTDClient? providerClient;
    private readonly HashSet<string> knownLineIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> knownStationIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly Task<IClusterClient> clientTask;
    private readonly Task<IHost>? inProcessHostTask;
    private readonly Task<IHost>? externalClientHostTask;
    private readonly bool externalOrleans;

    public TranslinkMapActor(TranslinkPTDClient? providerClient = null)
    {
        this.providerClient = providerClient;
        externalOrleans = UseExternalOrleans();
        if (externalOrleans)
        {
            externalClientHostTask = StartExternalClientHostAsync();
            clientTask = GetClientFromHostAsync(externalClientHostTask);
        }
        else
        {
            inProcessHostTask = StartInProcessOrleansAsync();
            clientTask = GetClientFromHostAsync(inProcessHostTask);
        }
    }

    public string Name => "Translink";

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = await GetClusterClientAsync().ConfigureAwait(false);
            var directoryGrain = client.GetGrain<IStationDirectoryGrain>(DirectoryKey);
            var existingDirectoryJson = await directoryGrain.GetStationsJsonAsync().ConfigureAwait(false);
            if (TryReadDirectory(existingDirectoryJson, out _, out var updatedAt)
                && DateTimeOffset.UtcNow - updatedAt < SharedRefreshInterval)
            {
                return;
            }

            if (externalOrleans)
            {
                return;
            }

            var stations = await GetProviderClient().GetStationsAsync(cancellationToken).ConfigureAwait(false);
            await directoryGrain
                .SetStationsJsonAsync(JsonSerializer.Serialize(new StationDirectoryCache(stations, DateTimeOffset.UtcNow)))
                .ConfigureAwait(false);

            foreach (var lineId in knownLineIds.ToArray())
            {
                var snapshot = await FetchLineSnapshotAsync(lineId, cancellationToken).ConfigureAwait(false);
                await client.GetGrain<ILineSnapshotGrain>(lineId)
                    .SetSnapshotJsonAsync(JsonSerializer.Serialize(snapshot))
                    .ConfigureAwait(false);
            }

            foreach (var stationId in knownStationIds.ToArray())
            {
                var snapshot = await GetProviderClient().GetStationSnapshotAsync(stationId, cancellationToken).ConfigureAwait(false);
                await client.GetGrain<IStationSnapshotGrain>(stationId)
                    .SetSnapshotJsonAsync(JsonSerializer.Serialize(snapshot))
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(TranslinkRailLineCatalog.GetLines());
    }

    public async Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        var client = await GetClusterClientAsync().ConfigureAwait(false);
        var json = await client.GetGrain<IStationDirectoryGrain>(DirectoryKey).GetStationsJsonAsync().ConfigureAwait(false);
        return TryReadDirectory(json, out var stations, out _) ? stations : [];
    }

    public async Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId, CancellationToken cancellationToken = default)
    {
        knownLineIds.Add(lineId);
        var client = await GetClusterClientAsync().ConfigureAwait(false);
        var grain = client.GetGrain<ILineSnapshotGrain>(lineId);
        var json = await grain.GetSnapshotJsonAsync().ConfigureAwait(false);
        if (json is null)
        {
            if (externalOrleans)
            {
                throw new InvalidOperationException("Translink cache is warming. Try again shortly.");
            }

            var snapshot = WithCatalogLine(await FetchLineSnapshotAsync(lineId, cancellationToken).ConfigureAwait(false));
            json = JsonSerializer.Serialize(snapshot);
            await grain.SetSnapshotJsonAsync(json).ConfigureAwait(false);
        }

        return WithCatalogLine(JsonSerializer.Deserialize<PTDLineSnapshot>(json)!);
    }

    public async Task<PTDStationSnapshot> GetStationSnapshotAsync(string stationId, CancellationToken cancellationToken = default)
    {
        knownStationIds.Add(stationId);
        var client = await GetClusterClientAsync().ConfigureAwait(false);
        var grain = client.GetGrain<IStationSnapshotGrain>(stationId);
        var json = await grain.GetSnapshotJsonAsync().ConfigureAwait(false);
        if (json is null)
        {
            if (externalOrleans)
            {
                throw new InvalidOperationException("Translink cache is warming. Try again shortly.");
            }

            var snapshot = await GetProviderClient().GetStationSnapshotAsync(stationId, cancellationToken).ConfigureAwait(false);
            json = JsonSerializer.Serialize(snapshot);
            await grain.SetSnapshotJsonAsync(json).ConfigureAwait(false);
        }

        return JsonSerializer.Deserialize<PTDStationSnapshot>(json)!;
    }

    public async ValueTask DisposeAsync()
    {
        if (inProcessHostTask?.IsCompletedSuccessfully == true)
        {
            await inProcessHostTask.Result.StopAsync().ConfigureAwait(false);
            inProcessHostTask.Result.Dispose();
        }

        if (externalClientHostTask?.IsCompletedSuccessfully == true)
        {
            await externalClientHostTask.Result.StopAsync().ConfigureAwait(false);
            externalClientHostTask.Result.Dispose();
        }
    }

    private async Task<IClusterClient> GetClusterClientAsync()
    {
        return await clientTask.ConfigureAwait(false);
    }

    private Task<PTDLineSnapshot> FetchLineSnapshotAsync(string lineId, CancellationToken cancellationToken)
    {
        var shortNameAnyParts = TranslinkRailLineCatalog.GetShortNameAnyParts(lineId);
        if (shortNameAnyParts.Length > 0)
        {
            return GetProviderClient().GetLineSnapshotByShortNameAnyAsync(shortNameAnyParts, cancellationToken);
        }

        if (lineId.StartsWith(TranslinkRailLineCatalog.ShortNameContainsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return GetProviderClient().GetLineSnapshotByShortNameContainsAsync(lineId[TranslinkRailLineCatalog.ShortNameContainsPrefix.Length..], cancellationToken);
        }

        return GetProviderClient().GetLineSnapshotAsync(lineId, cancellationToken);
    }

    private TranslinkPTDClient GetProviderClient()
    {
        return providerClient ?? throw new InvalidOperationException("Direct Translink provider access is disabled for this client.");
    }

    private static PTDLineSnapshot WithCatalogLine(PTDLineSnapshot snapshot)
    {
        var catalogLine = TranslinkRailLineCatalog.FindLine(snapshot.Line.Id);
        return catalogLine is null ? snapshot : snapshot with { Line = catalogLine };
    }

    private static async Task<IHost> StartInProcessOrleansAsync()
    {
        var siloPort = GetFreeTcpPort();
        var gatewayPort = GetFreeTcpPort();
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseOrleans(silo =>
            {
                silo.UseLocalhostClustering(siloPort, gatewayPort);
                silo.AddMemoryGrainStorage("Default");
            })
            .Build();

        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    private static async Task<IClusterClient> GetClientFromHostAsync(Task<IHost> hostTask)
    {
        var host = await hostTask.ConfigureAwait(false);
        return host.Services.GetRequiredService<IClusterClient>();
    }

    private static async Task<IHost> StartExternalClientHostAsync()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseOrleansClient(client =>
            {
                var valkeyConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__valkey")
                    ?? Environment.GetEnvironmentVariable("ConnectionStrings:valkey");
                if (!string.IsNullOrWhiteSpace(valkeyConnectionString)
                    && !string.Equals(Environment.GetEnvironmentVariable("ORLEANS_CLUSTERING_MODE"), "kubernetes", StringComparison.OrdinalIgnoreCase))
                {
                    client.UseRedisClustering(options =>
                    {
                        options.ConfigurationOptions = StackExchange.Redis.ConfigurationOptions.Parse(valkeyConnectionString);
                        options.EntryExpiry = TimeSpan.FromSeconds(30);
                    });
                    return;
                }

                var gatewayHost = Environment.GetEnvironmentVariable("ORLEANS_GATEWAY_HOST");
                var gatewayPort = GetPort("ORLEANS_GATEWAY_PORT", 30000);
                if (Uri.TryCreate(gatewayHost, UriKind.Absolute, out var gatewayUri))
                {
                    gatewayHost = gatewayUri.Host;
                    if (!gatewayUri.IsDefaultPort)
                    {
                        gatewayPort = gatewayUri.Port;
                    }
                }

                if (string.IsNullOrWhiteSpace(gatewayHost) || string.Equals(gatewayHost, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    client.UseLocalhostClustering(gatewayPort: gatewayPort);
                }
                else
                {
                    var addresses = Dns.GetHostAddresses(gatewayHost);
                    if (addresses.Length == 0)
                    {
                        throw new InvalidOperationException($"Cannot resolve Orleans gateway host '{gatewayHost}'.");
                    }

                    client.UseStaticClustering(new IPEndPoint(addresses[0], gatewayPort));
                }
            })
            .Build();

        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    private static bool UseExternalOrleans()
    {
        return string.Equals(Environment.GetEnvironmentVariable("PLATFORM22_ORLEANS_MODE"), "external", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetPort(string name, int defaultValue)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var port) ? port : defaultValue;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static bool TryReadDirectory(string? json, out IReadOnlyList<PTDStationSummary> stations, out DateTimeOffset updatedAt)
    {
        stations = [];
        updatedAt = DateTimeOffset.MinValue;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var cache = JsonSerializer.Deserialize<StationDirectoryCache>(json);
            if (cache is not null)
            {
                stations = cache.Stations;
                updatedAt = cache.UpdatedAt;
                return true;
            }
        }
        catch (JsonException)
        {
            var legacyStations = JsonSerializer.Deserialize<PTDStationSummary[]>(json);
            if (legacyStations is not null)
            {
                stations = legacyStations;
                return true;
            }
        }

        return false;
    }

    private sealed record StationDirectoryCache(IReadOnlyList<PTDStationSummary> Stations, DateTimeOffset UpdatedAt);
}

namespace Platform22.Tui;

using System.Text.Json;
using Microsoft.Extensions.Hosting;
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
        externalOrleans = OrleansEnvironment.UseExternalOrleans();
        if (externalOrleans)
        {
            externalClientHostTask = Platform22OrleansHosting.StartExternalClientHostAsync();
            clientTask = Platform22OrleansHosting.GetClientFromHostAsync(externalClientHostTask);
        }
        else
        {
            inProcessHostTask = Platform22OrleansHosting.StartInProcessSiloHostAsync();
            clientTask = Platform22OrleansHosting.GetClientFromHostAsync(inProcessHostTask);
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
            if (StationDirectoryCacheReader.TryRead(existingDirectoryJson, out _, out var updatedAt)
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
        return StationDirectoryCacheReader.TryRead(json, out var stations, out _) ? stations : [];
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
                throw new TranslinkCacheWarmingException();
            }

            var snapshot = await FetchLineSnapshotAsync(lineId, cancellationToken).ConfigureAwait(false);
            json = JsonSerializer.Serialize(snapshot);
            await grain.SetSnapshotJsonAsync(json).ConfigureAwait(false);
        }

        return TranslinkRailLineCatalog.WithCatalogLine(JsonSerializer.Deserialize<PTDLineSnapshot>(json)!);
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
                throw new TranslinkCacheWarmingException();
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
        return TranslinkRailLineCatalog.GetLineSnapshotAsync(GetProviderClient(), lineId, cancellationToken);
    }

    private TranslinkPTDClient GetProviderClient()
    {
        return providerClient ?? throw new InvalidOperationException("Direct Translink provider access is disabled for this client.");
    }
}

public sealed class TranslinkCacheWarmingException : Exception
{
    public TranslinkCacheWarmingException()
        : base("Translink cache is warming.")
    {
    }
}

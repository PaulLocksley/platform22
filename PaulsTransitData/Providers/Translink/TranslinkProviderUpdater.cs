namespace PaulsTransitData.Providers.Translink;

using PaulsTransitData.Streams;

public sealed class TranslinkProviderUpdater
{
    private readonly TranslinkGtfsHttpClient providerClient;
    private readonly ILineStateStore lineStateStore;

    public TranslinkProviderUpdater(TranslinkGtfsHttpClient providerClient, ILineStateStore lineStateStore)
    {
        this.providerClient = providerClient;
        this.lineStateStore = lineStateStore;
    }

    public async Task RefreshLineAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var update = await providerClient.GetLineUpdateAsync(routeId, cancellationToken).ConfigureAwait(false);
        await lineStateStore.ApplyUpdateAsync(update, cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshLineByShortNameAsync(string routeShortName, CancellationToken cancellationToken = default)
    {
        var update = await providerClient.GetLineUpdateByShortNameAsync(routeShortName, cancellationToken).ConfigureAwait(false);
        await lineStateStore.ApplyUpdateAsync(update, cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshLineByShortNameContainsAsync(string routeShortNamePart, CancellationToken cancellationToken = default)
    {
        var update = await providerClient.GetLineUpdateByShortNameContainsAsync(routeShortNamePart, cancellationToken).ConfigureAwait(false);
        await lineStateStore.ApplyUpdateAsync(update, cancellationToken).ConfigureAwait(false);
    }

    public Task<Models.PTDStationSnapshot> GetStationSnapshotAsync(string stopId, CancellationToken cancellationToken = default)
    {
        return providerClient.GetStationSnapshotAsync(stopId, cancellationToken);
    }

    public Task<IReadOnlyList<Models.PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        return providerClient.GetStationsAsync(cancellationToken);
    }
}

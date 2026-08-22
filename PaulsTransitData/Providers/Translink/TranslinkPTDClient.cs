namespace PaulsTransitData.Providers.Translink;

using PaulsTransitData.Abstractions;
using PaulsTransitData.Models;
using PaulsTransitData.Streams;
using PaulsTransitData.Subscriptions;

public sealed class TranslinkPTDClient : IPTDClient, IPTDStationClient
{
    private readonly PTDClient client;
    private readonly TranslinkProviderUpdater updater;

    public TranslinkPTDClient(HttpClient httpClient, TranslinkProviderOptions? options = null)
        : this(httpClient, new InMemoryLineStateStore(), options)
    {
    }

    public TranslinkPTDClient(HttpClient httpClient, ILineStateStore lineStateStore, TranslinkProviderOptions? options = null)
    {
        client = new PTDClient(lineStateStore);
        updater = new TranslinkProviderUpdater(new TranslinkGtfsHttpClient(httpClient, options), lineStateStore);
    }

    public Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default)
    {
        return client.GetLinesAsync(cancellationToken);
    }

    public async Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId, CancellationToken cancellationToken = default)
    {
        var routeId = GetRouteId(lineId);
        await updater.RefreshLineAsync(routeId, cancellationToken).ConfigureAwait(false);
        return await client.GetLineSnapshotAsync(lineId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PTDLineSnapshot> GetLineSnapshotByShortNameAsync(string routeShortName, CancellationToken cancellationToken = default)
    {
        await updater.RefreshLineByShortNameAsync(routeShortName, cancellationToken).ConfigureAwait(false);
        return await client.GetLineSnapshotAsync(TranslinkLineIds.ToShortNameLineId(routeShortName), cancellationToken).ConfigureAwait(false);
    }

    public async Task<PTDLineSnapshot> GetLineSnapshotByShortNameContainsAsync(string routeShortNamePart, CancellationToken cancellationToken = default)
    {
        await updater.RefreshLineByShortNameContainsAsync(routeShortNamePart, cancellationToken).ConfigureAwait(false);
        return await client.GetLineSnapshotAsync(TranslinkLineIds.ToShortNameContainsLineId(routeShortNamePart), cancellationToken).ConfigureAwait(false);
    }

    public async Task<PTDLineSnapshot> GetLineSnapshotByShortNameAnyAsync(IEnumerable<string> routeShortNameParts, CancellationToken cancellationToken = default)
    {
        var parts = routeShortNameParts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        await updater.RefreshLineByShortNameAnyAsync(parts, cancellationToken).ConfigureAwait(false);
        return await client.GetLineSnapshotAsync(TranslinkLineIds.ToShortNameAnyLineId(parts), cancellationToken).ConfigureAwait(false);
    }

    public Task<PTDStationSnapshot> GetStationSnapshotAsync(string stopId, CancellationToken cancellationToken = default)
    {
        return updater.GetStationSnapshotAsync(stopId, cancellationToken);
    }

    public Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        return updater.GetStationsAsync(cancellationToken);
    }

    public async Task<IPTDStationSubscription> SubscribeToStationAsync(
        string stopId,
        TimeSpan pollingInterval,
        CancellationToken cancellationToken = default)
    {
        var current = await GetStationSnapshotAsync(stopId, cancellationToken).ConfigureAwait(false);
        return new PTDStationSubscription(stopId, current, pollingInterval, token => GetStationSnapshotAsync(stopId, token));
    }

    private static string GetRouteId(string lineId)
    {
        const string prefix = TranslinkLineIds.ProviderId + ":";
        if (!lineId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || lineId.Length == prefix.Length)
        {
            throw new ArgumentException($"Translink line IDs must use '{prefix}<route_id>'.", nameof(lineId));
        }

        return lineId[prefix.Length..];
    }
}

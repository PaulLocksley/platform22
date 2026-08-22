namespace Platform22.Tui;

using PaulsTransitData.Models;
using PaulsTransitData.Providers.Translink;

public sealed class TranslinkMapClient : ITransitMapClient
{
    private readonly TranslinkPTDClient client;

    public TranslinkMapClient(TranslinkPTDClient client)
    {
        this.client = client;
    }

    public string Name => "Translink";

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return client.GetStationsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(TranslinkRailLineCatalog.GetLines());
    }

    public Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        return client.GetStationsAsync(cancellationToken);
    }

    public Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId, CancellationToken cancellationToken = default)
    {
        var shortNameAnyParts = TranslinkRailLineCatalog.GetShortNameAnyParts(lineId);
        if (shortNameAnyParts.Length > 0)
        {
            return WithCatalogLineAsync(client.GetLineSnapshotByShortNameAnyAsync(shortNameAnyParts, cancellationToken));
        }

        if (lineId.StartsWith(TranslinkRailLineCatalog.ShortNameContainsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return WithCatalogLineAsync(client.GetLineSnapshotByShortNameContainsAsync(lineId[TranslinkRailLineCatalog.ShortNameContainsPrefix.Length..], cancellationToken));
        }

        return WithCatalogLineAsync(client.GetLineSnapshotAsync(lineId, cancellationToken));
    }

    private static async Task<PTDLineSnapshot> WithCatalogLineAsync(Task<PTDLineSnapshot> snapshotTask)
    {
        var snapshot = await snapshotTask.ConfigureAwait(false);
        var catalogLine = TranslinkRailLineCatalog.FindLine(snapshot.Line.Id);
        return catalogLine is null ? snapshot : snapshot with { Line = catalogLine };
    }

    public Task<PTDStationSnapshot> GetStationSnapshotAsync(string stationId, CancellationToken cancellationToken = default)
    {
        return client.GetStationSnapshotAsync(stationId, cancellationToken);
    }
}

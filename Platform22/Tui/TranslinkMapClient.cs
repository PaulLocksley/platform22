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
        return TranslinkRailLineCatalog.GetLineSnapshotAsync(client, lineId, cancellationToken);
    }

    public Task<PTDStationSnapshot> GetStationSnapshotAsync(string stationId, CancellationToken cancellationToken = default)
    {
        return client.GetStationSnapshotAsync(stationId, cancellationToken);
    }
}

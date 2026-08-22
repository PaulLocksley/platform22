namespace Platform22.Tui;

using PaulsTransitData.Models;
using PaulsTransitData.Providers.Translink;

public sealed class TranslinkMapClient : ITransitMapClient
{
    private readonly TranslinkMapActor actor;

    public TranslinkMapClient(TranslinkPTDClient client)
    {
        actor = new TranslinkMapActor(client);
    }

    public string Name => "Translink";

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return actor.RefreshAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PTDLineSummary>>(
        [
            new PTDLineSummary("translink:short-name-contains:VL", "Varsity Lakes services", "translink", "FFC425")
        ]);
    }

    public Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        return actor.GetStationsAsync();
    }

    public Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId, CancellationToken cancellationToken = default)
    {
        return actor.GetLineSnapshotAsync(lineId);
    }

    public Task<PTDStationSnapshot> GetStationSnapshotAsync(string stationId, CancellationToken cancellationToken = default)
    {
        return actor.GetStationSnapshotAsync(stationId);
    }
}

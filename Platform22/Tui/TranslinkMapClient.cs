namespace Platform22.Tui;

using PaulsTransitData.Models;
using PaulsTransitData.Providers.Translink;

public sealed class TranslinkMapClient : ITransitMapClient
{
    private const string ShortNameContainsPrefix = "translink:short-name-contains:";
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
        return Task.FromResult<IReadOnlyList<PTDLineSummary>>(
        [
            new PTDLineSummary("translink:short-name-contains:VL", "Varsity Lakes services", "translink", "FFC425")
        ]);
    }

    public Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        return client.GetStationsAsync(cancellationToken);
    }

    public Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId, CancellationToken cancellationToken = default)
    {
        if (lineId.StartsWith(ShortNameContainsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return client.GetLineSnapshotByShortNameContainsAsync(lineId[ShortNameContainsPrefix.Length..], cancellationToken);
        }

        return client.GetLineSnapshotAsync(lineId, cancellationToken);
    }

    public Task<PTDStationSnapshot> GetStationSnapshotAsync(string stationId, CancellationToken cancellationToken = default)
    {
        return client.GetStationSnapshotAsync(stationId, cancellationToken);
    }
}

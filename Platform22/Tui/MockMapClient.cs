namespace Platform22.Tui;

using PaulsTransitData.Models;
using PaulsTransitData.Providers.Mock;

public sealed class MockMapClient : ITransitMapClient
{
    private readonly MockPTDClient client;

    public MockMapClient(MockPTDClient client)
    {
        this.client = client;
    }

    public string Name => "Mock PTD";

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default)
    {
        return client.GetLinesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        return client.GetStationsAsync(cancellationToken);
    }

    public Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId, CancellationToken cancellationToken = default)
    {
        return client.GetLineSnapshotAsync(lineId, cancellationToken);
    }

    public Task<PTDStationSnapshot> GetStationSnapshotAsync(string stationId, CancellationToken cancellationToken = default)
    {
        return client.GetStationSnapshotAsync(stationId, cancellationToken);
    }
}

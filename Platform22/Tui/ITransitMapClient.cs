namespace Platform22.Tui;

using PaulsTransitData.Models;

public interface ITransitMapClient
{
    string Name { get; }

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default);

    Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId, CancellationToken cancellationToken = default);

    Task<PTDStationSnapshot> GetStationSnapshotAsync(string stationId, CancellationToken cancellationToken = default);
}

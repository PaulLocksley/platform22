namespace PaulsTransitData.Abstractions;

using PaulsTransitData.Models;

public interface IPTDStationClient
{
    Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default);

    Task<PTDStationSnapshot> GetStationSnapshotAsync(string stopId, CancellationToken cancellationToken = default);
}

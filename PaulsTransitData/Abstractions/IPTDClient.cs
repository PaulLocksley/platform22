namespace PaulsTransitData.Abstractions;

using PaulsTransitData.Models;

public interface IPTDClient
{
    Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default);

    Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId, CancellationToken cancellationToken = default);
}

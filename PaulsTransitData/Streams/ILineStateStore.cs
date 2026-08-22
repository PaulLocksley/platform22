namespace PaulsTransitData.Streams;

using PaulsTransitData.Models;

public interface ILineStateStore
{
    Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default);

    Task<PTDLineSnapshot?> GetSnapshotAsync(string lineId, CancellationToken cancellationToken = default);

    Task ApplyUpdateAsync(PTDProviderLineUpdate update, CancellationToken cancellationToken = default);
}

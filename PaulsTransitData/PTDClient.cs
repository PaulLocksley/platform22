namespace PaulsTransitData;

using PaulsTransitData.Abstractions;
using PaulsTransitData.Models;
using PaulsTransitData.Streams;

public sealed class PTDClient : IPTDClient
{
    private readonly ILineStateStore lineStateStore;

    public PTDClient(ILineStateStore lineStateStore)
    {
        this.lineStateStore = lineStateStore;
    }

    public Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default)
    {
        return lineStateStore.GetLinesAsync(cancellationToken);
    }

    public async Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId, CancellationToken cancellationToken = default)
    {
        var snapshot = await lineStateStore.GetSnapshotAsync(lineId, cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
        {
            throw new InvalidOperationException($"Line '{lineId}' has no current data.");
        }

        return snapshot;
    }
}

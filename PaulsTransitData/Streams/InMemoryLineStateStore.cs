namespace PaulsTransitData.Streams;

using System.Collections.Concurrent;
using PaulsTransitData.Models;

public sealed class InMemoryLineStateStore : ILineStateStore
{
    private readonly ConcurrentDictionary<string, PTDLineSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default)
    {
        var lines = snapshots.Values
            .Select(snapshot => snapshot.Line)
            .OrderBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<PTDLineSummary>>(lines);
    }

    public Task<PTDLineSnapshot?> GetSnapshotAsync(string lineId, CancellationToken cancellationToken = default)
    {
        snapshots.TryGetValue(lineId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task ApplyUpdateAsync(PTDProviderLineUpdate update, CancellationToken cancellationToken = default)
    {
        snapshots[update.LineId] = update.Snapshot;
        return Task.CompletedTask;
    }
}

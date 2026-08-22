namespace PaulsTransitData.Subscriptions;

using System.Runtime.CompilerServices;
using PaulsTransitData.Abstractions;
using PaulsTransitData.Models;

public sealed class PTDStationSubscription : IPTDStationSubscription
{
    private readonly Func<CancellationToken, Task<PTDStationSnapshot>> getSnapshot;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly TimeSpan pollingInterval;

    public PTDStationSubscription(
        string stopId,
        PTDStationSnapshot current,
        TimeSpan pollingInterval,
        Func<CancellationToken, Task<PTDStationSnapshot>> getSnapshot)
    {
        StopId = stopId;
        Current = current;
        this.pollingInterval = pollingInterval;
        this.getSnapshot = getSnapshot;
        Updates = GetUpdatesAsync(cancellationTokenSource.Token);
    }

    public string StopId { get; }

    public PTDStationSnapshot Current { get; private set; }

    public IAsyncEnumerable<PTDStationSnapshot> Updates { get; }

    public ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }

    private async IAsyncEnumerable<PTDStationSnapshot> GetUpdatesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return Current;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(pollingInterval, cancellationToken).ConfigureAwait(false);
            Current = await getSnapshot(cancellationToken).ConfigureAwait(false);
            yield return Current;
        }
    }
}

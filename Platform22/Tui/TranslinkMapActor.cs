namespace Platform22.Tui;

using System.Threading.Channels;
using PaulsTransitData.Models;
using PaulsTransitData.Providers.Translink;

public sealed class TranslinkMapActor : IAsyncDisposable
{
    private const string ShortNameContainsPrefix = "translink:short-name-contains:";
    private readonly Channel<IActorMessage> mailbox = Channel.CreateUnbounded<IActorMessage>();
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly Task loop;

    public TranslinkMapActor(TranslinkPTDClient client)
    {
        loop = Task.Run(() => RunAsync(new State(client), cancellationTokenSource.Token));
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync<object?>(async state =>
        {
            await state.RefreshAsync(cancellationToken).ConfigureAwait(false);
            return null;
        });
    }

    public Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync()
    {
        return InvokeAsync(state => Task.FromResult(state.Stations));
    }

    public Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId)
    {
        return InvokeAsync(state => state.GetLineSnapshotAsync(lineId));
    }

    public Task<PTDStationSnapshot> GetStationSnapshotAsync(string stationId)
    {
        return InvokeAsync(state => state.GetStationSnapshotAsync(stationId));
    }

    public async ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();
        mailbox.Writer.TryComplete();
        await loop.ConfigureAwait(false);
        cancellationTokenSource.Dispose();
    }

    private async Task<T> InvokeAsync<T>(Func<State, Task<T>> action)
    {
        var message = new ActorMessage<T>(action);
        await mailbox.Writer.WriteAsync(message, cancellationTokenSource.Token).ConfigureAwait(false);
        return await message.Task.ConfigureAwait(false);
    }

    private async Task RunAsync(State state, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in mailbox.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await message.ExecuteAsync(state).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private interface IActorMessage
    {
        Task ExecuteAsync(State state);
    }

    private sealed class ActorMessage<T> : IActorMessage
    {
        private readonly Func<State, Task<T>> action;
        private readonly TaskCompletionSource<T> taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ActorMessage(Func<State, Task<T>> action)
        {
            this.action = action;
        }

        public Task<T> Task => taskCompletionSource.Task;

        public async Task ExecuteAsync(State state)
        {
            try
            {
                taskCompletionSource.SetResult(await action(state).ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                taskCompletionSource.SetException(exception);
            }
        }
    }

    private sealed class State
    {
        private readonly TranslinkPTDClient client;
        private readonly Dictionary<string, PTDLineSnapshot> lineSnapshots = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PTDStationSnapshot> stationSnapshots = new(StringComparer.OrdinalIgnoreCase);

        public State(TranslinkPTDClient client)
        {
            this.client = client;
        }

        public IReadOnlyList<PTDStationSummary> Stations { get; private set; } = [];

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            Stations = await client.GetStationsAsync(cancellationToken).ConfigureAwait(false);

            foreach (var lineId in lineSnapshots.Keys.ToArray())
            {
                lineSnapshots[lineId] = await FetchLineSnapshotAsync(lineId, cancellationToken).ConfigureAwait(false);
            }

            foreach (var stationId in stationSnapshots.Keys.ToArray())
            {
                stationSnapshots[stationId] = await client.GetStationSnapshotAsync(stationId, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId)
        {
            if (!lineSnapshots.TryGetValue(lineId, out var snapshot))
            {
                snapshot = await FetchLineSnapshotAsync(lineId, CancellationToken.None).ConfigureAwait(false);
                lineSnapshots[lineId] = snapshot;
            }

            return snapshot;
        }

        public async Task<PTDStationSnapshot> GetStationSnapshotAsync(string stationId)
        {
            if (!stationSnapshots.TryGetValue(stationId, out var snapshot))
            {
                snapshot = await client.GetStationSnapshotAsync(stationId).ConfigureAwait(false);
                stationSnapshots[stationId] = snapshot;
            }

            return snapshot;
        }

        private Task<PTDLineSnapshot> FetchLineSnapshotAsync(string lineId, CancellationToken cancellationToken)
        {
            if (lineId.StartsWith(ShortNameContainsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return client.GetLineSnapshotByShortNameContainsAsync(lineId[ShortNameContainsPrefix.Length..], cancellationToken);
            }

            return client.GetLineSnapshotAsync(lineId, cancellationToken);
        }
    }
}

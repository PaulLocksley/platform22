namespace Platform22.Orleans;

using global::Orleans.Runtime;

public sealed class StationSnapshotGrain : Grain, IStationSnapshotGrain
{
    private readonly IPersistentState<JsonGrainState> state;

    public StationSnapshotGrain([PersistentState("snapshot", Platform22OrleansHosting.GrainStorageName)] IPersistentState<JsonGrainState> state)
    {
        this.state = state;
    }

    public async Task SetSnapshotJsonAsync(string snapshotJson)
    {
        state.State = new JsonGrainState { Value = snapshotJson };
        await state.WriteStateAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<string?> GetSnapshotJsonAsync()
    {
        return Task.FromResult(state.State?.Value);
    }
}

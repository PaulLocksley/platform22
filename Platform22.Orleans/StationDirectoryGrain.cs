namespace Platform22.Orleans;

using global::Orleans.Runtime;

public sealed class StationDirectoryGrain : Grain, IStationDirectoryGrain
{
    private readonly IPersistentState<JsonGrainState> state;

    public StationDirectoryGrain([PersistentState("directory", Platform22OrleansHosting.GrainStorageName)] IPersistentState<JsonGrainState> state)
    {
        this.state = state;
    }

    public async Task SetStationsJsonAsync(string stationsJson)
    {
        state.State = new JsonGrainState { Value = stationsJson };
        await state.WriteStateAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<string?> GetStationsJsonAsync()
    {
        return Task.FromResult(state.State?.Value);
    }
}

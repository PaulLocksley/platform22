namespace Platform22.Orleans;

using Microsoft.Extensions.Configuration;

public sealed class StationSnapshotGrain : Grain, IStationSnapshotGrain
{
    private readonly ValkeyGrainState state;

    public StationSnapshotGrain(IConfiguration configuration)
    {
        state = new ValkeyGrainState(configuration, $"platform22:stations:{this.GetPrimaryKeyString()}:snapshot");
    }

    public Task SetSnapshotJsonAsync(string snapshotJson)
    {
        return state.SetAsync(snapshotJson);
    }

    public Task<string?> GetSnapshotJsonAsync()
    {
        return state.GetAsync();
    }
}

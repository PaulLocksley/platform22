namespace Platform22.Orleans;

using Microsoft.Extensions.Configuration;

public sealed class LineSnapshotGrain : Grain, ILineSnapshotGrain
{
    private readonly ValkeyGrainState state;

    public LineSnapshotGrain(IConfiguration configuration)
    {
        state = new ValkeyGrainState(configuration, $"platform22:lines:{this.GetPrimaryKeyString()}:snapshot");
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

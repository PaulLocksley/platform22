namespace Platform22.Orleans;

public sealed class StationSnapshotGrain : Grain, IStationSnapshotGrain
{
    private string? snapshotJson;

    public Task SetSnapshotJsonAsync(string snapshotJson)
    {
        this.snapshotJson = snapshotJson;
        return Task.CompletedTask;
    }

    public Task<string?> GetSnapshotJsonAsync()
    {
        return Task.FromResult(snapshotJson);
    }
}

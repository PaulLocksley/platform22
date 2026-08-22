namespace Platform22.Orleans;

public interface IStationSnapshotGrain : IGrainWithStringKey
{
    Task SetSnapshotJsonAsync(string snapshotJson);

    Task<string?> GetSnapshotJsonAsync();
}

namespace Platform22.Orleans;

public interface ILineSnapshotGrain : IGrainWithStringKey
{
    Task SetSnapshotJsonAsync(string snapshotJson);

    Task<string?> GetSnapshotJsonAsync();
}

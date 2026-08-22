namespace Platform22.Orleans;

public sealed class StationDirectoryGrain : Grain, IStationDirectoryGrain
{
    private string? stationsJson;

    public Task SetStationsJsonAsync(string stationsJson)
    {
        this.stationsJson = stationsJson;
        return Task.CompletedTask;
    }

    public Task<string?> GetStationsJsonAsync()
    {
        return Task.FromResult(stationsJson);
    }
}

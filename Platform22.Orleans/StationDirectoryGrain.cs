namespace Platform22.Orleans;

using Microsoft.Extensions.Configuration;

public sealed class StationDirectoryGrain : Grain, IStationDirectoryGrain
{
    private readonly ValkeyGrainState state;

    public StationDirectoryGrain(IConfiguration configuration)
    {
        state = new ValkeyGrainState(configuration, "platform22:stations:directory");
    }

    public Task SetStationsJsonAsync(string stationsJson)
    {
        return state.SetAsync(stationsJson);
    }

    public Task<string?> GetStationsJsonAsync()
    {
        return state.GetAsync();
    }
}

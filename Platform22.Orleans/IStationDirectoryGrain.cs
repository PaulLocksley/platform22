namespace Platform22.Orleans;

public interface IStationDirectoryGrain : IGrainWithStringKey
{
    Task SetStationsJsonAsync(string stationsJson);

    Task<string?> GetStationsJsonAsync();
}

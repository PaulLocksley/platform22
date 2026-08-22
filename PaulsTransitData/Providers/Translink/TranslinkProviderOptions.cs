namespace PaulsTransitData.Providers.Translink;

public sealed class TranslinkProviderOptions
{
    public Uri StaticGtfsUrl { get; init; } = new("https://gtfsrt.api.translink.com.au/GTFS/SEQ_GTFS.zip");

    public Uri RailVehiclePositionsUrl { get; init; } = new("https://gtfsrt.api.translink.com.au/api/realtime/SEQ/VehiclePositions/Rail");
}

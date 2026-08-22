namespace PaulsTransitData.Providers.Translink.Gtfs;

public sealed record TranslinkGtfsRealtimeResponse(
    DateTimeOffset Timestamp,
    IReadOnlyList<TranslinkGtfsVehiclePosition> Vehicles);

namespace PaulsTransitData.Providers.Translink.Gtfs;

public sealed record TranslinkGtfsVehiclePosition(
    string VehicleId,
    string PtdLineId,
    string? LastStopId,
    string? NextStopId,
    double? Latitude,
    double? Longitude,
    DateTimeOffset Timestamp);

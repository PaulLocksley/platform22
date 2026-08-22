namespace PaulsTransitData.Providers.Translink.Gtfs;

public sealed record TranslinkGtfsStop(
    string Id,
    string Name,
    double? Latitude,
    double? Longitude);

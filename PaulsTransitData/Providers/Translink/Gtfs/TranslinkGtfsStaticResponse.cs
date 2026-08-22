namespace PaulsTransitData.Providers.Translink.Gtfs;

public sealed record TranslinkGtfsStaticResponse(
    IReadOnlyList<TranslinkGtfsRoute> Routes,
    IReadOnlyList<TranslinkGtfsStop> Stops);

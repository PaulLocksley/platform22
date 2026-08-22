namespace PaulsTransitData.Providers.Translink.Gtfs;

public sealed record TranslinkGtfsRoute(
    string ProviderRouteId,
    string PtdLineId,
    string Name,
    string? Color,
    IReadOnlyList<string> StopIds);

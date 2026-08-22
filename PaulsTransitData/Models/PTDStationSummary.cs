namespace PaulsTransitData.Models;

public sealed record PTDStationSummary(
    string Id,
    string Name,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<string> LineIds);

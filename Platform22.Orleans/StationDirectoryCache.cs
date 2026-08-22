namespace Platform22.Orleans;

using PaulsTransitData.Models;

/// <summary>
/// Shared station directory cache entry stored in the directory grain.
/// JSON payload versioned by shape; readers fall back to a bare station array.
/// </summary>
public sealed record StationDirectoryCache(IReadOnlyList<PTDStationSummary> Stations, DateTimeOffset UpdatedAt);

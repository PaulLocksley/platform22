namespace Platform22.Orleans;

using System.Text.Json;
using PaulsTransitData.Models;

/// <summary>
/// Reads the versioned station-directory JSON payload, falling back to the
/// legacy bare station-array format written by older builds.
/// </summary>
public static class StationDirectoryCacheReader
{
    public static bool TryRead(string? json, out IReadOnlyList<PTDStationSummary> stations, out DateTimeOffset updatedAt)
    {
        stations = [];
        updatedAt = DateTimeOffset.MinValue;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var cache = JsonSerializer.Deserialize<StationDirectoryCache>(json);
            if (cache is not null)
            {
                stations = cache.Stations;
                updatedAt = cache.UpdatedAt;
                return true;
            }
        }
        catch (JsonException)
        {
            try
            {
                var legacyStations = JsonSerializer.Deserialize<PTDStationSummary[]>(json);
                if (legacyStations is not null)
                {
                    stations = legacyStations;
                    return true;
                }
            }
            catch (JsonException)
            {
                // Not the current format nor the legacy array.
            }
        }

        return false;
    }
}

namespace PaulsTransitData.Providers.Translink;

using System.IO.Compression;

/// <summary>
/// Indexes over the parsed static GTFS zip shared by all query paths.
/// </summary>
internal sealed record ParsedStaticGtfs(
    byte[] SourceBytes,
    IReadOnlyList<Dictionary<string, string>> Routes,
    IReadOnlyList<Dictionary<string, string>> Stops,
    IReadOnlyDictionary<string, Dictionary<string, string>> RouteById,
    IReadOnlyDictionary<string, Dictionary<string, string>> StopById,
    IReadOnlyDictionary<string, string> TripRouteIdByTripId,
    IReadOnlyDictionary<string, IReadOnlyList<StopTimeRow>> StopTimesByTripId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> TripIdsByStopId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ChildStopIdsByParent,
    IReadOnlyDictionary<string, string[]> LineIdsByStopId)
{
    public static ParsedStaticGtfs Create(
        byte[] sourceBytes,
        IReadOnlyList<Dictionary<string, string>> routes,
        IReadOnlyList<Dictionary<string, string>> trips,
        IReadOnlyList<Dictionary<string, string>> stopTimes,
        IReadOnlyList<Dictionary<string, string>> stops)
    {
        var routeById = routes.ToDictionary(route => route["route_id"], StringComparer.OrdinalIgnoreCase);
        var stopById = stops.ToDictionary(stop => stop["stop_id"], StringComparer.OrdinalIgnoreCase);
        var railRouteIds = routes.Where(TranslinkGtfsRows.IsRailRoute).Select(route => route["route_id"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tripRouteIdByTripId = trips
            .GroupBy(trip => trip["trip_id"], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First()["route_id"], StringComparer.OrdinalIgnoreCase);
        var stopTimesByTripId = new Dictionary<string, List<StopTimeRow>>(StringComparer.OrdinalIgnoreCase);
        var tripIdsByStopId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var lineIdsByStopId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var stopTime in stopTimes)
        {
            var tripId = stopTime["trip_id"];
            if (!tripRouteIdByTripId.TryGetValue(tripId, out var routeId))
            {
                continue;
            }

            var stopId = stopTime["stop_id"];
            var row = new StopTimeRow(stopId, int.Parse(stopTime["stop_sequence"]));
            if (!stopTimesByTripId.TryGetValue(tripId, out var tripStopTimes))
            {
                tripStopTimes = [];
                stopTimesByTripId[tripId] = tripStopTimes;
            }

            tripStopTimes.Add(row);

            if (!tripIdsByStopId.TryGetValue(stopId, out var stopTripIds))
            {
                stopTripIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                tripIdsByStopId[stopId] = stopTripIds;
            }

            stopTripIds.Add(tripId);

            if (!railRouteIds.Contains(routeId))
            {
                continue;
            }

            if (!lineIdsByStopId.TryGetValue(stopId, out var lineIds))
            {
                lineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                lineIdsByStopId[stopId] = lineIds;
            }

            lineIds.Add(TranslinkLineIds.ToPtdLineId(routeId));
        }

        var childStopIdsByParent = stops
            .Where(stop => !string.IsNullOrWhiteSpace(stop.GetValueOrDefault("parent_station")))
            .GroupBy(stop => stop["parent_station"], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(stop => stop["stop_id"]).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return new ParsedStaticGtfs(
            sourceBytes,
            routes,
            stops,
            routeById,
            stopById,
            tripRouteIdByTripId,
            stopTimesByTripId.ToDictionary(row => row.Key, row => (IReadOnlyList<StopTimeRow>)row.Value, StringComparer.OrdinalIgnoreCase),
            tripIdsByStopId.ToDictionary(row => row.Key, row => (IReadOnlyList<string>)row.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            childStopIdsByParent,
            lineIdsByStopId.ToDictionary(row => row.Key, row => row.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
    }
}

internal sealed record StopTimeRow(string StopId, int Sequence);

/// <summary>CSV readers and row helpers for GTFS zips.</summary>
internal static class TranslinkGtfsCsv
{
    public static IReadOnlyList<Dictionary<string, string>> ReadCsv(ZipArchive archive, string fileName)
    {
        var entry = archive.GetEntry(fileName) ?? throw new InvalidOperationException($"GTFS file '{fileName}' was not found.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var headerLine = reader.ReadLine() ?? throw new InvalidOperationException($"GTFS file '{fileName}' is empty.");
        var headers = ParseCsvLine(headerLine).ToArray();
        var rows = new List<Dictionary<string, string>>();

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line).ToArray();
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                row[headers[i]] = i < values.Length ? values[i] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static IEnumerable<string> ParseCsvLine(string line)
    {
        var value = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                yield return value.ToString();
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }

        yield return value.ToString();
    }
}

/// <summary>Shared GTFS row accessors.</summary>
internal static class TranslinkGtfsRows
{
    public static bool IsParentStation(IReadOnlyDictionary<string, string> stop)
    {
        return stop.GetValueOrDefault("location_type") == "1";
    }

    public static bool IsRailRoute(IReadOnlyDictionary<string, string> route)
    {
        return route.GetValueOrDefault("route_type") == "2";
    }

    public static string GetRouteName(IReadOnlyDictionary<string, string> row)
    {
        if (row.TryGetValue("route_long_name", out var longName) && !string.IsNullOrWhiteSpace(longName))
        {
            return longName;
        }

        return row.GetValueOrDefault("route_short_name") ?? string.Empty;
    }

    public static double? ParseNullableDouble(string? value)
    {
        return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}

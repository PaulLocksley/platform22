namespace PaulsTransitData.Providers.Translink;

using System.Globalization;
using System.IO.Compression;
using System.Text;
using PaulsTransitData.Models;
using PaulsTransitData.Providers.GtfsRealtime;
using PaulsTransitData.Providers.Translink.Gtfs;
using PaulsTransitData.Streams;

public sealed class TranslinkGtfsHttpClient
{
    private readonly HttpClient httpClient;
    private readonly TranslinkProviderOptions options;
    private readonly TranslinkGtfsMapper mapper = new();

    public TranslinkGtfsHttpClient(HttpClient httpClient, TranslinkProviderOptions? options = null)
    {
        this.httpClient = httpClient;
        this.options = options ?? new TranslinkProviderOptions();
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var staticBytes = await httpClient.GetByteArrayAsync(options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        var realtimeBytes = await httpClient.GetByteArrayAsync(options.RailVehiclePositionsUrl, cancellationToken).ConfigureAwait(false);
        var lineId = TranslinkLineIds.ToPtdLineId(routeId);

        var staticResponse = ParseStaticGtfsByRouteIds(staticBytes, [routeId], lineId, routeId);
        var realtimeResponse = ParseRealtime(realtimeBytes, new HashSet<string>([routeId], StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateByShortNameAsync(string routeShortName, CancellationToken cancellationToken = default)
    {
        var staticBytes = await httpClient.GetByteArrayAsync(options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        var realtimeBytes = await httpClient.GetByteArrayAsync(options.RailVehiclePositionsUrl, cancellationToken).ConfigureAwait(false);
        var lineId = TranslinkLineIds.ToShortNameLineId(routeShortName);
        var routes = ReadRoutes(staticBytes)
            .Where(route => string.Equals(route.GetValueOrDefault("route_short_name"), routeShortName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (routes.Length == 0)
        {
            throw new InvalidOperationException($"No Translink routes matched short name '{routeShortName}'.");
        }

        var routeIds = routes.Select(route => route["route_id"]).ToArray();
        var staticResponse = ParseStaticGtfsByRouteIds(staticBytes, routeIds, lineId, routeShortName);
        var realtimeResponse = ParseRealtime(realtimeBytes, routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateByShortNameContainsAsync(string routeShortNamePart, CancellationToken cancellationToken = default)
    {
        var staticBytes = await httpClient.GetByteArrayAsync(options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        var realtimeBytes = await httpClient.GetByteArrayAsync(options.RailVehiclePositionsUrl, cancellationToken).ConfigureAwait(false);
        var lineId = TranslinkLineIds.ToShortNameContainsLineId(routeShortNamePart);
        var routes = ReadRoutes(staticBytes)
            .Where(route => route.GetValueOrDefault("route_short_name")?.Contains(routeShortNamePart, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        if (routes.Length == 0)
        {
            throw new InvalidOperationException($"No Translink routes matched short name part '{routeShortNamePart}'.");
        }

        var routeIds = routes.Select(route => route["route_id"]).ToArray();
        var staticResponse = ParseStaticGtfsByRouteIds(staticBytes, routeIds, lineId, routeShortNamePart);
        var realtimeResponse = ParseRealtime(realtimeBytes, routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDStationSnapshot> GetStationSnapshotAsync(string stopId, CancellationToken cancellationToken = default)
    {
        var staticBytes = await httpClient.GetByteArrayAsync(options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        var realtimeBytes = await httpClient.GetByteArrayAsync(options.RailVehiclePositionsUrl, cancellationToken).ConfigureAwait(false);

        return ParseStationSnapshot(staticBytes, realtimeBytes, stopId);
    }

    public async Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        var staticBytes = await httpClient.GetByteArrayAsync(options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        return ParseStations(staticBytes);
    }

    private static TranslinkGtfsStaticResponse ParseStaticGtfsByRouteIds(
        byte[] zipBytes,
        IReadOnlyCollection<string> routeIds,
        string lineId,
        string lineName)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var routes = ReadCsv(archive, "routes.txt");
        var trips = ReadCsv(archive, "trips.txt");
        var stopTimes = ReadCsv(archive, "stop_times.txt");
        var stops = ReadCsv(archive, "stops.txt");
        var routeIdSet = routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchingRoutes = routes.Where(row => routeIdSet.Contains(row["route_id"])).ToArray();

        if (matchingRoutes.Length == 0)
        {
            throw new InvalidOperationException($"No Translink routes matched '{lineName}'.");
        }

        var tripIds = trips
            .Where(row => routeIdSet.Contains(row["route_id"]))
            .Select(row => row["trip_id"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var routeStopTimes = stopTimes
            .Where(row => tripIds.Contains(row["trip_id"]))
            .Select(row => new
            {
                StopId = row["stop_id"],
                Sequence = int.Parse(row["stop_sequence"])
            })
            .GroupBy(row => row.StopId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { StopId = group.Key, Sequence = group.Min(row => row.Sequence) })
            .OrderBy(row => row.Sequence)
            .ToArray();

        var stopIds = routeStopTimes.Select(row => row.StopId).ToArray();
        var stopIdSet = stopIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parsedStops = stops
            .Where(row => stopIdSet.Contains(row["stop_id"]))
            .Select(row => new TranslinkGtfsStop(
                row["stop_id"],
                row["stop_name"],
                ParseNullableDouble(row.GetValueOrDefault("stop_lat")),
                ParseNullableDouble(row.GetValueOrDefault("stop_lon"))))
            .ToArray();
        var colors = matchingRoutes
            .Select(route => route.GetValueOrDefault("route_color"))
            .Where(color => !string.IsNullOrWhiteSpace(color))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var displayName = matchingRoutes.Length == 1
            ? GetRouteName(matchingRoutes[0])
            : string.Join(" / ", matchingRoutes.Select(GetRouteName).Distinct(StringComparer.OrdinalIgnoreCase).Order());
        var parsedRoute = new TranslinkGtfsRoute(lineName, lineId, displayName, colors.Length == 1 ? colors[0] : null, stopIds);

        return new TranslinkGtfsStaticResponse([parsedRoute], parsedStops);
    }

    private static IReadOnlyList<Dictionary<string, string>> ReadRoutes(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return ReadCsv(archive, "routes.txt");
    }

    private static PTDStationSnapshot ParseStationSnapshot(byte[] zipBytes, byte[] protobufBytes, string stopId)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var routes = ReadCsv(archive, "routes.txt");
        var trips = ReadCsv(archive, "trips.txt");
        var stopTimes = ReadCsv(archive, "stop_times.txt");
        var stops = ReadCsv(archive, "stops.txt");
        var stop = stops.Single(row => string.Equals(row["stop_id"], stopId, StringComparison.OrdinalIgnoreCase));
        var stationStopIds = stops
            .Where(row => string.Equals(row["stop_id"], stopId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.GetValueOrDefault("parent_station"), stopId, StringComparison.OrdinalIgnoreCase))
            .Select(row => row["stop_id"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tripIdsServingStop = stopTimes
            .Where(row => stationStopIds.Contains(row["stop_id"]))
            .Select(row => row["trip_id"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var routeIds = trips
            .Where(row => tripIdsServingStop.Contains(row["trip_id"]))
            .Select(row => row["route_id"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var routeById = routes
            .Where(row => routeIds.Contains(row["route_id"]))
            .ToDictionary(row => row["route_id"], StringComparer.OrdinalIgnoreCase);
        var feed = FeedMessage.Parser.ParseFrom(protobufBytes);
        var feedTimestamp = feed.Header is { HasTimestamp: true }
            ? DateTimeOffset.FromUnixTimeSeconds((long)feed.Header.Timestamp)
            : DateTimeOffset.UtcNow;
        var trainPositions = feed.Entity
            .Where(entity => !entity.IsDeleted && entity.Vehicle is not null)
            .Select(entity => ToStationTrainPosition(entity, routeById, feedTimestamp))
            .Where(position => position is not null)
            .Cast<PTDStationTrainPosition>()
            .OrderBy(position => position.Line.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(position => position.TrainPosition.TrainId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var station = new PTDStop(
            stop["stop_id"],
            stop["stop_name"],
            ParseNullableDouble(stop.GetValueOrDefault("stop_lat")),
            ParseNullableDouble(stop.GetValueOrDefault("stop_lon")),
            0);
        var updatedAt = trainPositions.Length > 0
            ? trainPositions.Max(position => position.TrainPosition.Timestamp)
            : feedTimestamp;

        return new PTDStationSnapshot(station, trainPositions, updatedAt);
    }

    private static IReadOnlyList<PTDStationSummary> ParseStations(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var stops = ReadCsv(archive, "stops.txt");
        var stopTimes = ReadCsv(archive, "stop_times.txt");
        var trips = ReadCsv(archive, "trips.txt");
        var routes = ReadCsv(archive, "routes.txt");
        var routeIds = routes.Select(route => route["route_id"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var routeIdByTripId = trips
            .Where(trip => routeIds.Contains(trip["route_id"]))
            .GroupBy(trip => trip["trip_id"], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First()["route_id"], StringComparer.OrdinalIgnoreCase);
        var lineIdsByStopId = stopTimes
            .Where(stopTime => routeIdByTripId.ContainsKey(stopTime["trip_id"]))
            .GroupBy(stopTime => stopTime["stop_id"], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(stopTime => TranslinkLineIds.ToPtdLineId(routeIdByTripId[stopTime["trip_id"]]))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var stopById = stops.ToDictionary(stop => stop["stop_id"], StringComparer.OrdinalIgnoreCase);
        var stationIds = stops
            .Where(stop => IsParentStation(stop) || string.IsNullOrWhiteSpace(stop.GetValueOrDefault("parent_station")))
            .Select(stop => stop["stop_id"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return stationIds
            .Select(stationId => ToStationSummary(stationId, stops, stopById, lineIdsByStopId))
            .Where(station => station.LineIds.Count > 0)
            .OrderBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PTDStationSummary ToStationSummary(
        string stationId,
        IReadOnlyList<Dictionary<string, string>> stops,
        IReadOnlyDictionary<string, Dictionary<string, string>> stopById,
        IReadOnlyDictionary<string, string[]> lineIdsByStopId)
    {
        var station = stopById[stationId];
        var childStopIds = stops
            .Where(stop => string.Equals(stop.GetValueOrDefault("parent_station"), stationId, StringComparison.OrdinalIgnoreCase))
            .Select(stop => stop["stop_id"]);
        var relatedStopIds = childStopIds.Prepend(stationId).ToArray();
        var lineIds = relatedStopIds
            .Where(lineIdsByStopId.ContainsKey)
            .SelectMany(stopId => lineIdsByStopId[stopId])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToArray();

        return new PTDStationSummary(
            station["stop_id"],
            station["stop_name"],
            ParseNullableDouble(station.GetValueOrDefault("stop_lat")),
            ParseNullableDouble(station.GetValueOrDefault("stop_lon")),
            lineIds);
    }

    private static bool IsParentStation(IReadOnlyDictionary<string, string> stop)
    {
        return stop.GetValueOrDefault("location_type") == "1";
    }

    private static PTDStationTrainPosition? ToStationTrainPosition(
        FeedEntity entity,
        IReadOnlyDictionary<string, Dictionary<string, string>> routeById,
        DateTimeOffset feedTimestamp)
    {
        var vehicle = entity.Vehicle;
        if (vehicle is null || vehicle.Trip is null || string.IsNullOrWhiteSpace(vehicle.Trip.RouteId))
        {
            return null;
        }

        if (!routeById.TryGetValue(vehicle.Trip.RouteId, out var route))
        {
            return null;
        }

        var trainId = vehicle.Vehicle is not null && !string.IsNullOrWhiteSpace(vehicle.Vehicle.Id)
            ? vehicle.Vehicle.Id
            : entity.Id;
        var timestamp = vehicle.HasTimestamp
            ? DateTimeOffset.FromUnixTimeSeconds((long)vehicle.Timestamp)
            : feedTimestamp;
        var stopId = vehicle.HasStopId ? vehicle.StopId : null;
        var lastStopId = vehicle.CurrentStatus == VehiclePosition.Types.VehicleStopStatus.StoppedAt ? stopId : null;
        var nextStopId = vehicle.CurrentStatus == VehiclePosition.Types.VehicleStopStatus.StoppedAt ? null : stopId;
        var line = new PTDLineSummary(
            TranslinkLineIds.ToPtdLineId(vehicle.Trip.RouteId),
            GetRouteName(route),
            TranslinkLineIds.ProviderId,
            route.GetValueOrDefault("route_color"));
        var trainPosition = new PTDTrainPosition(
            trainId,
            lastStopId,
            nextStopId,
            vehicle.Position is not null ? vehicle.Position.Latitude : null,
            vehicle.Position is not null ? vehicle.Position.Longitude : null,
            timestamp);

        return new PTDStationTrainPosition(line, trainPosition);
    }

    private static TranslinkGtfsRealtimeResponse ParseRealtime(byte[] protobufBytes, ISet<string> routeIds, string lineId)
    {
        var feed = FeedMessage.Parser.ParseFrom(protobufBytes);
        var feedTimestamp = feed.Header is { HasTimestamp: true }
            ? DateTimeOffset.FromUnixTimeSeconds((long)feed.Header.Timestamp)
            : DateTimeOffset.UtcNow;
        var vehicles = feed.Entity
            .Where(entity => !entity.IsDeleted && entity.Vehicle is not null)
            .Select(entity => ToVehiclePosition(entity, routeIds, lineId, feedTimestamp))
            .Where(vehicle => vehicle is not null)
            .Cast<TranslinkGtfsVehiclePosition>()
            .ToArray();

        return new TranslinkGtfsRealtimeResponse(feedTimestamp, vehicles);
    }

    private static TranslinkGtfsVehiclePosition? ToVehiclePosition(
        FeedEntity entity,
        ISet<string> routeIds,
        string lineId,
        DateTimeOffset feedTimestamp)
    {
        var vehicle = entity.Vehicle;
        if (vehicle is null || vehicle.Trip is null || string.IsNullOrWhiteSpace(vehicle.Trip.RouteId))
        {
            return null;
        }

        if (!routeIds.Contains(vehicle.Trip.RouteId))
        {
            return null;
        }

        var trainId = vehicle.Vehicle is not null && !string.IsNullOrWhiteSpace(vehicle.Vehicle.Id)
            ? vehicle.Vehicle.Id
            : entity.Id;
        var timestamp = vehicle.HasTimestamp
            ? DateTimeOffset.FromUnixTimeSeconds((long)vehicle.Timestamp)
            : feedTimestamp;
        var latitude = vehicle.Position is not null ? vehicle.Position.Latitude : (double?)null;
        var longitude = vehicle.Position is not null ? vehicle.Position.Longitude : (double?)null;
        var stopId = vehicle.HasStopId ? vehicle.StopId : null;
        var lastStopId = vehicle.CurrentStatus == VehiclePosition.Types.VehicleStopStatus.StoppedAt ? stopId : null;
        var nextStopId = vehicle.CurrentStatus == VehiclePosition.Types.VehicleStopStatus.StoppedAt ? null : stopId;

        return new TranslinkGtfsVehiclePosition(trainId, lineId, lastStopId, nextStopId, latitude, longitude, timestamp);
    }

    private static IReadOnlyList<Dictionary<string, string>> ReadCsv(ZipArchive archive, string fileName)
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
        var value = new StringBuilder();
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

    private static string GetRouteName(IReadOnlyDictionary<string, string> row)
    {
        if (row.TryGetValue("route_long_name", out var longName) && !string.IsNullOrWhiteSpace(longName))
        {
            return longName;
        }

        return row.GetValueOrDefault("route_short_name") ?? string.Empty;
    }

    private static double? ParseNullableDouble(string? value)
    {
        return double.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}

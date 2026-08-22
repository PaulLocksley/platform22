namespace PaulsTransitData.Providers.Translink;

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using PaulsTransitData.Models;
using PaulsTransitData.Providers.GtfsRealtime;
using PaulsTransitData.Providers.Translink.Gtfs;
using PaulsTransitData.Streams;

public sealed class TranslinkGtfsHttpClient
{
    private static readonly TimeSpan StaticGtfsTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan RealtimeTtl = TimeSpan.FromSeconds(10);
    private readonly HttpClient httpClient;
    private readonly TranslinkProviderOptions options;
    private readonly TranslinkGtfsMapper mapper = new();
    private readonly SemaphoreSlim fetchLock = new(1, 1);
    private readonly object staticGtfsLock = new();
    private CachedFeed? staticGtfs;
    private CachedFeed? railVehiclePositions;
    private ParsedStaticGtfs? parsedStaticGtfs;

    public TranslinkGtfsHttpClient(HttpClient httpClient, TranslinkProviderOptions? options = null)
    {
        this.httpClient = httpClient;
        this.options = options ?? new TranslinkProviderOptions();
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var staticBytes = await FetchBytesAsync("static GTFS", options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        var realtimeBytes = await FetchBytesAsync("rail vehicle positions", options.RailVehiclePositionsUrl, cancellationToken).ConfigureAwait(false);
        var staticGtfs = GetParsedStaticGtfs(staticBytes);
        var lineId = TranslinkLineIds.ToPtdLineId(routeId);

        var staticResponse = ParseStaticGtfsByRouteIds(staticGtfs, [routeId], lineId, routeId);
        var realtimeResponse = ParseRealtime(realtimeBytes, new HashSet<string>([routeId], StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateByShortNameAsync(string routeShortName, CancellationToken cancellationToken = default)
    {
        var staticBytes = await FetchBytesAsync("static GTFS", options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        var realtimeBytes = await FetchBytesAsync("rail vehicle positions", options.RailVehiclePositionsUrl, cancellationToken).ConfigureAwait(false);
        var staticGtfs = GetParsedStaticGtfs(staticBytes);
        var lineId = TranslinkLineIds.ToShortNameLineId(routeShortName);
        var routes = staticGtfs.Routes
            .Where(route => IsRailRoute(route) && string.Equals(route.GetValueOrDefault("route_short_name"), routeShortName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (routes.Length == 0)
        {
            throw new InvalidOperationException($"No Translink routes matched short name '{routeShortName}'.");
        }

        var routeIds = routes.Select(route => route["route_id"]).ToArray();
        var staticResponse = ParseStaticGtfsByRouteIds(staticGtfs, routeIds, lineId, routeShortName);
        var realtimeResponse = ParseRealtime(realtimeBytes, routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateByShortNameContainsAsync(string routeShortNamePart, CancellationToken cancellationToken = default)
    {
        var staticBytes = await FetchBytesAsync("static GTFS", options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        var realtimeBytes = await FetchBytesAsync("rail vehicle positions", options.RailVehiclePositionsUrl, cancellationToken).ConfigureAwait(false);
        var staticGtfs = GetParsedStaticGtfs(staticBytes);
        var lineId = TranslinkLineIds.ToShortNameContainsLineId(routeShortNamePart);
        var routes = staticGtfs.Routes
            .Where(route => IsRailRoute(route) && route.GetValueOrDefault("route_short_name")?.Contains(routeShortNamePart, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        if (routes.Length == 0)
        {
            throw new InvalidOperationException($"No Translink routes matched short name part '{routeShortNamePart}'.");
        }

        var routeIds = routes.Select(route => route["route_id"]).ToArray();
        var staticResponse = ParseStaticGtfsByRouteIds(staticGtfs, routeIds, lineId, routeShortNamePart);
        var realtimeResponse = ParseRealtime(realtimeBytes, routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateByShortNameAnyAsync(IReadOnlyCollection<string> routeShortNameParts, CancellationToken cancellationToken = default)
    {
        var parts = routeShortNameParts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        if (parts.Length == 0)
        {
            throw new ArgumentException("At least one route short name part is required.", nameof(routeShortNameParts));
        }

        var staticBytes = await FetchBytesAsync("static GTFS", options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        var realtimeBytes = await FetchBytesAsync("rail vehicle positions", options.RailVehiclePositionsUrl, cancellationToken).ConfigureAwait(false);
        var staticGtfs = GetParsedStaticGtfs(staticBytes);
        var lineId = TranslinkLineIds.ToShortNameAnyLineId(parts);
        var routes = staticGtfs.Routes
            .Where(route => IsRailRoute(route) && parts.Any(part => route.GetValueOrDefault("route_short_name")?.Contains(part, StringComparison.OrdinalIgnoreCase) == true))
            .ToArray();

        if (routes.Length == 0)
        {
            throw new InvalidOperationException($"No Translink routes matched short name parts '{string.Join(", ", parts)}'.");
        }

        var routeIds = routes.Select(route => route["route_id"]).ToArray();
        var staticResponse = ParseStaticGtfsByRouteIds(staticGtfs, routeIds, lineId, string.Join(" / ", parts));
        var realtimeResponse = ParseRealtime(realtimeBytes, routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDStationSnapshot> GetStationSnapshotAsync(string stopId, CancellationToken cancellationToken = default)
    {
        var staticBytes = await FetchBytesAsync("static GTFS", options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        var realtimeBytes = await FetchBytesAsync("rail vehicle positions", options.RailVehiclePositionsUrl, cancellationToken).ConfigureAwait(false);
        var staticGtfs = GetParsedStaticGtfs(staticBytes);

        return ParseStationSnapshot(staticGtfs, realtimeBytes, stopId);
    }

    public async Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        var staticBytes = await FetchBytesAsync("static GTFS", options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        return ParseStations(GetParsedStaticGtfs(staticBytes));
    }

    private async Task<byte[]> FetchBytesAsync(string name, Uri uri, CancellationToken cancellationToken)
    {
        var ttl = string.Equals(uri.AbsoluteUri, options.StaticGtfsUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
            ? StaticGtfsTtl
            : RealtimeTtl;
        var cached = GetCachedFeed(uri);
        if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < ttl)
        {
            return cached.Bytes;
        }

        await fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = GetCachedFeed(uri);
            if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < ttl)
            {
                return cached.Bytes;
            }

            var bytes = await FetchBytesUncachedAsync(name, uri, cancellationToken).ConfigureAwait(false);
            SetCachedFeed(uri, new CachedFeed(bytes, DateTimeOffset.UtcNow));
            return bytes;
        }
        finally
        {
            fetchLock.Release();
        }
    }

    private async Task<byte[]> FetchBytesUncachedAsync(string name, Uri uri, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        Console.Error.WriteLine($"Translink fetch start: {name} {uri} at {startedAt:O}");

        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        Console.Error.WriteLine(
            $"Translink fetch end: {name} status={(int)response.StatusCode} {response.ReasonPhrase} bytes={bytes.Length} durationMs={stopwatch.ElapsedMilliseconds} {GetRateLimitText(response)}");
        response.EnsureSuccessStatusCode();

        return bytes;
    }

    private CachedFeed? GetCachedFeed(Uri uri)
    {
        return string.Equals(uri.AbsoluteUri, options.StaticGtfsUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
            ? staticGtfs
            : railVehiclePositions;
    }

    private void SetCachedFeed(Uri uri, CachedFeed feed)
    {
        if (string.Equals(uri.AbsoluteUri, options.StaticGtfsUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
        {
            staticGtfs = feed;
        }
        else
        {
            railVehiclePositions = feed;
        }
    }

    private static string GetRateLimitText(HttpResponseMessage response)
    {
        var headers = new[]
            {
                "RateLimit-Limit",
                "RateLimit-Remaining",
                "RateLimit-Reset",
                "X-RateLimit-Limit",
                "X-RateLimit-Remaining",
                "X-RateLimit-Reset",
                "Retry-After"
            }
            .Select(header => TryGetHeader(response, header))
            .Where(value => value is not null)
            .ToArray();

        return headers.Length == 0 ? "rateLimitHeaders=none" : string.Join(' ', headers);
    }

    private static string? TryGetHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
        {
            return $"{name}={string.Join(',', values)}";
        }

        return null;
    }

    private ParsedStaticGtfs GetParsedStaticGtfs(byte[] zipBytes)
    {
        lock (staticGtfsLock)
        {
            if (parsedStaticGtfs is not null && ReferenceEquals(parsedStaticGtfs.SourceBytes, zipBytes))
            {
                return parsedStaticGtfs;
            }

            var stopwatch = Stopwatch.StartNew();
            using var stream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var routes = ReadCsv(archive, "routes.txt");
            var trips = ReadCsv(archive, "trips.txt");
            var stopTimes = ReadCsv(archive, "stop_times.txt");
            var stops = ReadCsv(archive, "stops.txt");
            parsedStaticGtfs = ParsedStaticGtfs.Create(zipBytes, routes, trips, stopTimes, stops);
            Console.Error.WriteLine($"Translink static GTFS parsed in {stopwatch.ElapsedMilliseconds} ms");
            return parsedStaticGtfs;
        }
    }

    private static TranslinkGtfsStaticResponse ParseStaticGtfsByRouteIds(
        ParsedStaticGtfs staticGtfs,
        IReadOnlyCollection<string> routeIds,
        string lineId,
        string lineName)
    {
        var routes = staticGtfs.Routes;
        var routeIdSet = routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchingRoutes = routes.Where(row => routeIdSet.Contains(row["route_id"])).ToArray();

        if (matchingRoutes.Length == 0)
        {
            throw new InvalidOperationException($"No Translink routes matched '{lineName}'.");
        }

        var representativeTripId = staticGtfs.TripRouteIdByTripId
            .Where(row => routeIdSet.Contains(row.Value))
            .Select(row => row.Key)
            .OrderByDescending(tripId => staticGtfs.StopTimesByTripId.TryGetValue(tripId, out var matchingStopTimes) ? matchingStopTimes.Count : 0)
            .ThenBy(tripId => tripId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (representativeTripId is null)
        {
            throw new InvalidOperationException($"No Translink trips matched '{lineName}'.");
        }

        var routeStopTimes = staticGtfs.StopTimesByTripId[representativeTripId]
            .OrderBy(row => row.Sequence)
            .ToArray();

        var stopIds = routeStopTimes.Select(row => row.StopId).ToArray();
        var parsedStops = stopIds
            .Where(staticGtfs.StopById.ContainsKey)
            .Select(stopId => staticGtfs.StopById[stopId])
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

    private static PTDStationSnapshot ParseStationSnapshot(ParsedStaticGtfs staticGtfs, byte[] protobufBytes, string stopId)
    {
        var stop = staticGtfs.StopById[stopId];
        var stationStopIds = staticGtfs.ChildStopIdsByParent.TryGetValue(stopId, out var childStopIds)
            ? childStopIds.Prepend(stopId).ToArray()
            : [stopId];
        var tripIdsServingStop = stationStopIds
            .Where(staticGtfs.TripIdsByStopId.ContainsKey)
            .SelectMany(stationStopId => staticGtfs.TripIdsByStopId[stationStopId])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var routeById = tripIdsServingStop
            .Where(staticGtfs.TripRouteIdByTripId.ContainsKey)
            .Select(tripId => staticGtfs.TripRouteIdByTripId[tripId])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(staticGtfs.RouteById.ContainsKey)
            .ToDictionary(routeId => routeId, routeId => staticGtfs.RouteById[routeId], StringComparer.OrdinalIgnoreCase);
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

    private static IReadOnlyList<PTDStationSummary> ParseStations(ParsedStaticGtfs staticGtfs)
    {
        var stationIds = staticGtfs.Stops
            .Where(stop => IsParentStation(stop) || string.IsNullOrWhiteSpace(stop.GetValueOrDefault("parent_station")))
            .Select(stop => stop["stop_id"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return stationIds
            .Select(stationId => ToStationSummary(stationId, staticGtfs))
            .Where(station => station.LineIds.Count > 0)
            .OrderBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PTDStationSummary ToStationSummary(
        string stationId,
        ParsedStaticGtfs staticGtfs)
    {
        var station = staticGtfs.StopById[stationId];
        var childStopIds = staticGtfs.ChildStopIdsByParent.GetValueOrDefault(stationId, []);
        var relatedStopIds = childStopIds.Prepend(stationId).ToArray();
        var lineIds = relatedStopIds
            .Where(staticGtfs.LineIdsByStopId.ContainsKey)
            .SelectMany(stopId => staticGtfs.LineIdsByStopId[stopId])
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

    private static bool IsRailRoute(IReadOnlyDictionary<string, string> route)
    {
        return route.GetValueOrDefault("route_type") == "2";
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

    private sealed record CachedFeed(byte[] Bytes, DateTimeOffset FetchedAt);

    private sealed record ParsedStaticGtfs(
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
            var railRouteIds = routes.Where(IsRailRoute).Select(route => route["route_id"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    private sealed record StopTimeRow(string StopId, int Sequence);
}

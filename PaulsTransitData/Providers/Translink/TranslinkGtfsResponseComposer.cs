namespace PaulsTransitData.Providers.Translink;

using PaulsTransitData.Models;
using PaulsTransitData.Providers.GtfsRealtime;
using PaulsTransitData.Providers.Translink.Gtfs;

/// <summary>
/// Composes static GTFS indexes and realtime feeds into PTD line and station
/// responses.
/// </summary>
internal static class TranslinkGtfsResponseComposer
{
    public static TranslinkGtfsStaticResponse ParseStaticGtfsByRouteIds(
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
                TranslinkGtfsRows.ParseNullableDouble(row.GetValueOrDefault("stop_lat")),
                TranslinkGtfsRows.ParseNullableDouble(row.GetValueOrDefault("stop_lon"))))
            .ToArray();
        var colors = matchingRoutes
            .Select(route => route.GetValueOrDefault("route_color"))
            .Where(color => !string.IsNullOrWhiteSpace(color))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var displayName = matchingRoutes.Length == 1
            ? TranslinkGtfsRows.GetRouteName(matchingRoutes[0])
            : string.Join(" / ", matchingRoutes.Select(TranslinkGtfsRows.GetRouteName).Distinct(StringComparer.OrdinalIgnoreCase).Order());
        var parsedRoute = new TranslinkGtfsRoute(lineName, lineId, displayName, colors.Length == 1 ? colors[0] : null, stopIds);

        return new TranslinkGtfsStaticResponse([parsedRoute], parsedStops);
    }

    public static PTDStationSnapshot ParseStationSnapshot(ParsedStaticGtfs staticGtfs, byte[] protobufBytes, string stopId)
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
        var feedTimestamp = TranslinkGtfsRealtimeParser.GetFeedTimestamp(feed);
        var trainPositions = feed.Entity
            .Where(entity => !entity.IsDeleted && entity.Vehicle is not null)
            .Select(entity => TranslinkGtfsRealtimeParser.ToStationTrainPosition(entity, routeById, feedTimestamp))
            .Where(position => position is not null)
            .Cast<PTDStationTrainPosition>()
            .OrderBy(position => position.Line.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(position => position.TrainPosition.TrainId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var station = new PTDStop(
            stop["stop_id"],
            stop["stop_name"],
            TranslinkGtfsRows.ParseNullableDouble(stop.GetValueOrDefault("stop_lat")),
            TranslinkGtfsRows.ParseNullableDouble(stop.GetValueOrDefault("stop_lon")),
            0);
        var updatedAt = trainPositions.Length > 0
            ? trainPositions.Max(position => position.TrainPosition.Timestamp)
            : feedTimestamp;

        return new PTDStationSnapshot(station, trainPositions, updatedAt);
    }

    public static IReadOnlyList<PTDStationSummary> ParseStations(ParsedStaticGtfs staticGtfs)
    {
        var stationIds = staticGtfs.Stops
            .Where(stop => TranslinkGtfsRows.IsParentStation(stop) || string.IsNullOrWhiteSpace(stop.GetValueOrDefault("parent_station")))
            .Select(stop => stop["stop_id"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return stationIds
            .Select(stationId => ToStationSummary(stationId, staticGtfs))
            .Where(station => station.LineIds.Count > 0)
            .OrderBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PTDStationSummary ToStationSummary(string stationId, ParsedStaticGtfs staticGtfs)
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
            TranslinkGtfsRows.ParseNullableDouble(station.GetValueOrDefault("stop_lat")),
            TranslinkGtfsRows.ParseNullableDouble(station.GetValueOrDefault("stop_lon")),
            lineIds);
    }
}

namespace PaulsTransitData.Providers.Translink;

using PaulsTransitData.Models;
using PaulsTransitData.Providers.GtfsRealtime;
using PaulsTransitData.Providers.Translink.Gtfs;

/// <summary>
/// Maps GTFS-Realtime vehicle positions to PTD vehicles and station positions.
/// </summary>
internal static class TranslinkGtfsRealtimeParser
{
    public static TranslinkGtfsRealtimeResponse ParseRealtime(byte[] protobufBytes, ISet<string> routeIds, string lineId)
    {
        var feed = FeedMessage.Parser.ParseFrom(protobufBytes);
        var feedTimestamp = GetFeedTimestamp(feed);
        var vehicles = feed.Entity
            .Where(entity => !entity.IsDeleted && entity.Vehicle is not null)
            .Select(entity => ToVehiclePosition(entity, routeIds, lineId, feedTimestamp))
            .Where(vehicle => vehicle is not null)
            .Cast<TranslinkGtfsVehiclePosition>()
            .ToArray();

        return new TranslinkGtfsRealtimeResponse(feedTimestamp, vehicles);
    }

    public static PTDStationTrainPosition? ToStationTrainPosition(
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

        var trainId = GetTrainId(entity, vehicle);
        var timestamp = GetVehicleTimestamp(vehicle, feedTimestamp);
        var stopId = vehicle.HasStopId ? vehicle.StopId : null;
        var lastStopId = vehicle.CurrentStatus == VehiclePosition.Types.VehicleStopStatus.StoppedAt ? stopId : null;
        var nextStopId = vehicle.CurrentStatus == VehiclePosition.Types.VehicleStopStatus.StoppedAt ? null : stopId;
        var line = new PTDLineSummary(
            TranslinkLineIds.ToPtdLineId(vehicle.Trip.RouteId),
            TranslinkGtfsRows.GetRouteName(route),
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

        var trainId = GetTrainId(entity, vehicle);
        var timestamp = GetVehicleTimestamp(vehicle, feedTimestamp);
        var latitude = vehicle.Position is not null ? vehicle.Position.Latitude : (double?)null;
        var longitude = vehicle.Position is not null ? vehicle.Position.Longitude : (double?)null;
        var stopId = vehicle.HasStopId ? vehicle.StopId : null;
        var lastStopId = vehicle.CurrentStatus == VehiclePosition.Types.VehicleStopStatus.StoppedAt ? stopId : null;
        var nextStopId = vehicle.CurrentStatus == VehiclePosition.Types.VehicleStopStatus.StoppedAt ? null : stopId;

        return new TranslinkGtfsVehiclePosition(trainId, lineId, lastStopId, nextStopId, latitude, longitude, timestamp);
    }

    public static DateTimeOffset GetFeedTimestamp(FeedMessage feed)
    {
        return feed.Header is { HasTimestamp: true }
            ? DateTimeOffset.FromUnixTimeSeconds((long)feed.Header.Timestamp)
            : DateTimeOffset.UtcNow;
    }

    private static string GetTrainId(FeedEntity entity, VehiclePosition vehicle)
    {
        return vehicle.Vehicle is not null && !string.IsNullOrWhiteSpace(vehicle.Vehicle.Id)
            ? vehicle.Vehicle.Id
            : entity.Id;
    }

    private static DateTimeOffset GetVehicleTimestamp(VehiclePosition vehicle, DateTimeOffset feedTimestamp)
    {
        return vehicle.HasTimestamp
            ? DateTimeOffset.FromUnixTimeSeconds((long)vehicle.Timestamp)
            : feedTimestamp;
    }
}

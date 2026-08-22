namespace PaulsTransitData.Providers.Translink;

using PaulsTransitData.Models;
using PaulsTransitData.Providers;
using PaulsTransitData.Providers.Translink.Gtfs;
using PaulsTransitData.Streams;

public sealed class TranslinkGtfsMapper : ITransitProviderMapper<TranslinkGtfsStaticResponse, TranslinkGtfsRealtimeResponse>
{
    public PTDProviderLineUpdate MapLineUpdate(
        TranslinkGtfsStaticResponse staticResponse,
        TranslinkGtfsRealtimeResponse realtimeResponse,
        string lineId)
    {
        var route = staticResponse.Routes.Single(route => route.PtdLineId == lineId);
        var stopById = staticResponse.Stops.ToDictionary(stop => stop.Id, StringComparer.OrdinalIgnoreCase);
        var stops = route.StopIds
            .Select((stopId, index) => ToStop(stopById[stopId], index + 1))
            .ToArray();

        var trainPositions = realtimeResponse.Vehicles
            .Where(vehicle => string.Equals(vehicle.PtdLineId, lineId, StringComparison.OrdinalIgnoreCase))
            .Select(ToTrainPosition)
            .OrderBy(position => position.TrainId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var line = new PTDLineSummary(route.PtdLineId, route.Name, TranslinkLineIds.ProviderId, route.Color);
        var updatedAt = trainPositions.Length > 0
            ? trainPositions.Max(position => position.Timestamp)
            : realtimeResponse.Timestamp;
        var snapshot = new PTDLineSnapshot(line, stops, trainPositions, updatedAt);
        var messageId = $"{TranslinkLineIds.ProviderId}:{lineId}:{updatedAt:O}:{trainPositions.Length}";

        return new PTDProviderLineUpdate("1", TranslinkLineIds.ProviderId, lineId, messageId, updatedAt, snapshot);
    }

    private static PTDStop ToStop(TranslinkGtfsStop stop, int sequence)
    {
        return new PTDStop(stop.Id, stop.Name, stop.Latitude, stop.Longitude, sequence);
    }

    private static PTDTrainPosition ToTrainPosition(TranslinkGtfsVehiclePosition vehicle)
    {
        return new PTDTrainPosition(
            vehicle.VehicleId,
            vehicle.LastStopId,
            vehicle.NextStopId,
            vehicle.Latitude,
            vehicle.Longitude,
            vehicle.Timestamp);
    }
}

namespace PaulsTransitData.Models;

public sealed record PTDTrainPosition(
    string TrainId,
    string? LastStopId,
    string? NextStopId,
    double? Latitude,
    double? Longitude,
    DateTimeOffset Timestamp);

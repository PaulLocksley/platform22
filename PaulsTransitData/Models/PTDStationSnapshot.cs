namespace PaulsTransitData.Models;

public sealed record PTDStationSnapshot(
    PTDStop Station,
    IReadOnlyList<PTDStationTrainPosition> TrainPositions,
    DateTimeOffset UpdatedAt);

namespace PaulsTransitData.Models;

public sealed record PTDLineSnapshot(
    PTDLineSummary Line,
    IReadOnlyList<PTDStop> Stops,
    IReadOnlyList<PTDTrainPosition> TrainPositions,
    DateTimeOffset UpdatedAt);

namespace PaulsTransitData.Models;

public sealed record PTDStationTrainPosition(
    PTDLineSummary Line,
    PTDTrainPosition TrainPosition);

namespace PaulsTransitData.Models;

public sealed record PTDStop(
    string Id,
    string Name,
    double? Latitude,
    double? Longitude,
    int Sequence);

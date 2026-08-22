namespace PaulsTransitData.Models;

public sealed record PTDLineSummary(
    string Id,
    string Name,
    string ProviderId,
    string? Color);

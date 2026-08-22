namespace Platform22.Tui;

using PaulsTransitData.Models;

public static class TransitFilter
{
    public static IReadOnlyList<PTDLineSummary> FilterLines(IEnumerable<PTDLineSummary> lines, string filter)
    {
        return lines
            .Where(line => Matches(line.Id, filter) || Matches(line.Name, filter))
            .OrderBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<PTDStationSummary> FilterStations(IEnumerable<PTDStationSummary> stations, string filter)
    {
        return stations
            .Where(station => Matches(station.Id, filter) || Matches(station.Name, filter))
            .OrderBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool Matches(string value, string filter)
    {
        return string.IsNullOrWhiteSpace(filter) || value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}

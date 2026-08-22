namespace Platform22.Tui;

using PaulsTransitData.Models;
using PaulsTransitData.Providers.Translink;

public static class TranslinkRailLineCatalog
{
    public const string ShortNameAnyPrefix = "translink:short-name-any:";
    public const string ShortNameContainsPrefix = "translink:short-name-contains:";

    private static readonly PTDLineSummary[] Lines =
        TranslinkRailLineDefinitions.Lines
            .Select(line => new PTDLineSummary(line.LineId, line.Name, TranslinkLineIds.ProviderId, line.Color))
            .ToArray();

    public static IReadOnlyList<PTDLineSummary> GetLines()
    {
        return Lines;
    }

    public static string[] GetShortNameAnyParts(string lineId)
    {
        if (!lineId.StartsWith(ShortNameAnyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return lineId[ShortNameAnyPrefix.Length..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static PTDLineSummary? FindLine(string lineId)
    {
        return Lines.FirstOrDefault(line => string.Equals(line.Id, lineId, StringComparison.OrdinalIgnoreCase));
    }

}

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

    /// <summary>
    /// Resolves a UI line id (catalog id, short-name-any id, or contains id) against
    /// the provider. Single definition shared by direct client and Orleans actor.
    /// </summary>
    public static Task<PTDLineSnapshot> GetLineSnapshotAsync(TranslinkPTDClient client, string lineId, CancellationToken cancellationToken = default)
    {
        var shortNameAnyParts = GetShortNameAnyParts(lineId);
        if (shortNameAnyParts.Length > 0)
        {
            return WithCatalogLineAsync(client.GetLineSnapshotByShortNameAnyAsync(shortNameAnyParts, cancellationToken));
        }

        if (lineId.StartsWith(ShortNameContainsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return WithCatalogLineAsync(client.GetLineSnapshotByShortNameContainsAsync(lineId[ShortNameContainsPrefix.Length..], cancellationToken));
        }

        return WithCatalogLineAsync(client.GetLineSnapshotAsync(lineId, cancellationToken));
    }

    public static PTDLineSnapshot WithCatalogLine(PTDLineSnapshot snapshot)
    {
        var catalogLine = FindLine(snapshot.Line.Id);
        return catalogLine is null ? snapshot : snapshot with { Line = catalogLine };
    }

    private static async Task<PTDLineSnapshot> WithCatalogLineAsync(Task<PTDLineSnapshot> snapshotTask)
    {
        return WithCatalogLine(await snapshotTask.ConfigureAwait(false));
    }
}

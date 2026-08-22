namespace PaulsTransitData.Providers.Translink;

public static class TranslinkRailLineDefinitions
{
    public static readonly IReadOnlyList<TranslinkRailLineDefinition> Lines =
    [
        new("Airport / Gold Coast", "FFC425", ["BD", "VL"]),
        new("Beenleigh / Ferny Grove", "EF464E", ["BN", "FG"]),
        new("Caboolture / Sunshine Coast", "2E9D6A", ["CA", "NA", "GY"]),
        new("Cleveland / Shorncliffe", "4E84C4", ["CL", "SH"]),
        new("Doomben", "A968B4", ["DB"]),
        new("Ipswich / Rosewood", "2E9D6A", ["IP", "RW"]),
        new("Redcliffe Peninsula / Springfield", "6CC6E9", ["RP", "SP"])
    ];
}

public sealed record TranslinkRailLineDefinition(string Name, string Color, IReadOnlyList<string> RouteShortNameParts)
{
    public string LineId => TranslinkLineIds.ToShortNameAnyLineId(RouteShortNameParts.ToArray());
}

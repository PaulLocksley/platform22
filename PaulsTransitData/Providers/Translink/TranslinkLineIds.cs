namespace PaulsTransitData.Providers.Translink;

public static class TranslinkLineIds
{
    public const string ProviderId = "translink";

    public static string ToPtdLineId(string routeId)
    {
        return $"{ProviderId}:{routeId}";
    }

    public static string ToShortNameLineId(string routeShortName)
    {
        return $"{ProviderId}:short-name:{routeShortName}";
    }

    public static string ToShortNameContainsLineId(string routeShortNamePart)
    {
        return $"{ProviderId}:short-name-contains:{routeShortNamePart}";
    }
}

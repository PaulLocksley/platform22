namespace PaulsTransitData.Tests;

using PaulsTransitData.Providers.Translink;
using PaulsTransitData.Providers.Translink.Gtfs;

internal static class MockTranslinkGtfsProtobufResponses
{
    public const string GoldCoastRouteId = "GOLD";

    public static readonly string GoldCoastLineId = TranslinkLineIds.ToPtdLineId(GoldCoastRouteId);

    public static readonly TranslinkGtfsStaticResponse StaticSchedule = new(
        Routes:
        [
            new TranslinkGtfsRoute(
                GoldCoastRouteId,
                GoldCoastLineId,
                "Gold Coast line",
                "#f6c343",
                ["roma-street", "park-road", "helensvale", "nerang", "varsity-lakes"])
        ],
        Stops:
        [
            new TranslinkGtfsStop("roma-street", "Roma Street station", -27.4661, 153.0180),
            new TranslinkGtfsStop("park-road", "Park Road station", -27.4996, 153.0362),
            new TranslinkGtfsStop("helensvale", "Helensvale station", -27.9256, 153.3381),
            new TranslinkGtfsStop("nerang", "Nerang station", -27.9890, 153.3405),
            new TranslinkGtfsStop("varsity-lakes", "Varsity Lakes station", -28.0897, 153.3892)
        ]);

    public static readonly TranslinkGtfsRealtimeResponse RealtimeVehiclePositions = new(
        new DateTimeOffset(2026, 8, 22, 8, 30, 0, TimeSpan.Zero),
        [
            new TranslinkGtfsVehiclePosition(
                "T123",
                GoldCoastLineId,
                "park-road",
                "helensvale",
                -27.7150,
                153.2020,
                new DateTimeOffset(2026, 8, 22, 8, 30, 10, TimeSpan.Zero)),
            new TranslinkGtfsVehiclePosition(
                "T456",
                GoldCoastLineId,
                "nerang",
                "varsity-lakes",
                -28.0310,
                153.3650,
                new DateTimeOffset(2026, 8, 22, 8, 30, 12, TimeSpan.Zero))
        ]);
}

namespace PaulsTransitData.Tests;

using PaulsTransitData.Providers.Translink;
using PaulsTransitData.Streams;
using Xunit;

public sealed class TranslinkGoldCoastClientTests
{
    [Fact]
    public async Task ClientGetsStopsAndTrainPositionsForGoldCoastLine()
    {
        var store = new InMemoryLineStateStore();
        var mapper = new TranslinkGtfsMapper();
        var update = mapper.MapLineUpdate(
            MockTranslinkGtfsProtobufResponses.StaticSchedule,
            MockTranslinkGtfsProtobufResponses.RealtimeVehiclePositions,
            MockTranslinkGtfsProtobufResponses.GoldCoastLineId);
        await store.ApplyUpdateAsync(update);

        var client = new PTDClient(store);

        var snapshot = await client.GetLineSnapshotAsync(MockTranslinkGtfsProtobufResponses.GoldCoastLineId);

        Assert.Equal(MockTranslinkGtfsProtobufResponses.GoldCoastLineId, snapshot.Line.Id);
        Assert.Equal("Gold Coast line", snapshot.Line.Name);
        Assert.Equal(5, snapshot.Stops.Count);
        Assert.Collection(
            snapshot.Stops,
            stop => Assert.Equal("roma-street", stop.Id),
            stop => Assert.Equal("park-road", stop.Id),
            stop => Assert.Equal("helensvale", stop.Id),
            stop => Assert.Equal("nerang", stop.Id),
            stop => Assert.Equal("varsity-lakes", stop.Id));
        Assert.Equal(2, snapshot.TrainPositions.Count);
        Assert.Contains(snapshot.TrainPositions, train => train.TrainId == "T123" && train.NextStopId == "helensvale");
        Assert.Contains(snapshot.TrainPositions, train => train.TrainId == "T456" && train.LastStopId == "nerang");
    }

    [Fact]
    public async Task ClientListsSeededLines()
    {
        var store = new InMemoryLineStateStore();
        var mapper = new TranslinkGtfsMapper();
        var update = mapper.MapLineUpdate(
            MockTranslinkGtfsProtobufResponses.StaticSchedule,
            MockTranslinkGtfsProtobufResponses.RealtimeVehiclePositions,
            MockTranslinkGtfsProtobufResponses.GoldCoastLineId);
        await store.ApplyUpdateAsync(update);

        var client = new PTDClient(store);

        var lines = await client.GetLinesAsync();

        var line = Assert.Single(lines);
        Assert.Equal("Gold Coast line", line.Name);
        Assert.Equal(TranslinkLineIds.ProviderId, line.ProviderId);
    }

    [Fact]
    public async Task ClientFailsWhenLineHasNoCurrentData()
    {
        var client = new PTDClient(new InMemoryLineStateStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetLineSnapshotAsync(MockTranslinkGtfsProtobufResponses.GoldCoastLineId));

        Assert.Contains("has no current data", exception.Message);
    }
}

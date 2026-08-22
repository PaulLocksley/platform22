namespace PaulsTransitData.Tests;

using PaulsTransitData.Models;
using PaulsTransitData.Providers.Mock;
using Xunit;

public sealed class MockPTDClientTests
{
    [Fact]
    public async Task MockProviderHasThreeLinesWithSharedCoreStops()
    {
        var client = new MockPTDClient(new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var lines = await client.GetLinesAsync();

        Assert.Equal(3, lines.Count);
        foreach (var line in lines)
        {
            var snapshot = await client.GetLineSnapshotAsync(line.Id);
            Assert.Equal(9, snapshot.Stops.Count);
            Assert.Equal(3, snapshot.Stops.Count(stop => !stop.Id.StartsWith("mock:core-", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(6, snapshot.Stops.Count(stop => stop.Id.StartsWith("mock:core-", StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public async Task MockProviderListsStationsWithLineIdsOnly()
    {
        var client = new MockPTDClient(new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var stations = await client.GetStationsAsync();

        Assert.Equal(15, stations.Count);
        var coreStation = stations.Single(station => station.Id == "mock:core-1");
        Assert.Equal(3, coreStation.LineIds.Count);
        Assert.Contains(MockPTDLineIds.Red, coreStation.LineIds);
        Assert.Contains(MockPTDLineIds.Blue, coreStation.LineIds);
        Assert.Contains(MockPTDLineIds.Green, coreStation.LineIds);

        var branchStation = stations.Single(station => station.Id == "mock:red-1");
        var lineId = Assert.Single(branchStation.LineIds);
        Assert.Equal(MockPTDLineIds.Red, lineId);
    }

    [Fact]
    public async Task MockTrainsMoveAtConstantSpeedAndStopForThirtySeconds()
    {
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UnixEpoch);
        var client = new MockPTDClient(timeProvider);

        var atStop = await client.GetLineSnapshotAsync(MockPTDLineIds.Red);
        timeProvider.UtcNow = DateTimeOffset.UnixEpoch.AddSeconds(30);
        var startMoving = await client.GetLineSnapshotAsync(MockPTDLineIds.Red);
        timeProvider.UtcNow = DateTimeOffset.UnixEpoch.AddSeconds(75);
        var halfWay = await client.GetLineSnapshotAsync(MockPTDLineIds.Red);

        var stoppedTrain = atStop.TrainPositions[0];
        var startMovingTrain = startMoving.TrainPositions[0];
        var halfWayTrain = halfWay.TrainPositions[0];
        var firstStop = atStop.Stops[0];
        var secondStop = atStop.Stops[1];

        Assert.Equal(firstStop.Id, stoppedTrain.LastStopId);
        Assert.Equal(firstStop.Latitude, stoppedTrain.Latitude);
        Assert.Equal(firstStop.Latitude, startMovingTrain.Latitude);
        Assert.Equal((firstStop.Latitude + secondStop.Latitude) / 2, halfWayTrain.Latitude);
        Assert.Equal((firstStop.Longitude + secondStop.Longitude) / 2, halfWayTrain.Longitude);
    }

    [Fact]
    public async Task MockLineTrainStopIdsExistInLineStops()
    {
        var client = new MockPTDClient();
        var line = (await client.GetLinesAsync()).First();

        var snapshot = await client.GetLineSnapshotAsync(line.Id);

        var stopIds = snapshot.Stops.Select(stop => stop.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(snapshot.TrainPositions, train =>
        {
            Assert.True(train.LastStopId is null || stopIds.Contains(train.LastStopId));
            Assert.True(train.NextStopId is null || stopIds.Contains(train.NextStopId));
        });
    }

    [Fact]
    public async Task CoreStationSnapshotHasTrainsFromAllLines()
    {
        var client = new MockPTDClient(new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var snapshot = await client.GetStationSnapshotAsync("mock:core-3");

        Assert.Equal("Core 3", snapshot.Station.Name);
        Assert.Equal(6, snapshot.TrainPositions.Count);
        Assert.Equal(3, snapshot.TrainPositions.Select(position => position.Line.Id).Distinct().Count());
    }

    [Fact]
    public async Task BranchStationSnapshotOnlyHasItsLine()
    {
        var client = new MockPTDClient(new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var snapshot = await client.GetStationSnapshotAsync("mock:red-2");

        Assert.Equal(2, snapshot.TrainPositions.Count);
        Assert.All(snapshot.TrainPositions, position => Assert.Equal(MockPTDLineIds.Red, position.Line.Id));
    }

    [Fact]
    public async Task StationSubscriptionStartsWithCurrentMockData()
    {
        var client = new MockPTDClient(new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        await using var subscription = await client.SubscribeToStationAsync("mock:core-1", TimeSpan.FromMinutes(1));

        PTDStationSnapshot? firstUpdate = null;
        await foreach (var update in subscription.Updates)
        {
            firstUpdate = update;
            break;
        }

        Assert.NotNull(firstUpdate);
        Assert.Equal(subscription.Current, firstUpdate);
        Assert.Equal(6, firstUpdate.TrainPositions.Count);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }
    }
}

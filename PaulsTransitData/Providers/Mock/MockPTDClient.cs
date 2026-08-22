namespace PaulsTransitData.Providers.Mock;

using PaulsTransitData.Abstractions;
using PaulsTransitData.Models;
using PaulsTransitData.Subscriptions;

public sealed class MockPTDClient : IPTDClient, IPTDStationClient
{
    private static readonly TimeSpan StopDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SegmentDuration = TimeSpan.FromSeconds(90);
    private readonly DateTimeOffset epoch;
    private readonly IReadOnlyList<MockLine> lines;
    private readonly TimeProvider timeProvider;

    public MockPTDClient(TimeProvider? timeProvider = null, DateTimeOffset? epoch = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.epoch = epoch ?? DateTimeOffset.UnixEpoch;
        lines = CreateLines();
    }

    public Task<IReadOnlyList<PTDLineSummary>> GetLinesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PTDLineSummary>>(lines.Select(line => line.Summary).ToArray());
    }

    public Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        var stations = lines
            .SelectMany(line => line.Stops.Select(stop => new { Stop = stop, LineId = line.Summary.Id }))
            .GroupBy(item => item.Stop.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var stop = group.First().Stop;
                var lineIds = group.Select(item => item.LineId).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
                return new PTDStationSummary(stop.Id, stop.Name, stop.Latitude, stop.Longitude, lineIds);
            })
            .OrderBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<PTDStationSummary>>(stations);
    }

    public Task<PTDLineSnapshot> GetLineSnapshotAsync(string lineId, CancellationToken cancellationToken = default)
    {
        var line = lines.Single(line => string.Equals(line.Summary.Id, lineId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(CreateLineSnapshot(line));
    }

    public Task<PTDStationSnapshot> GetStationSnapshotAsync(string stopId, CancellationToken cancellationToken = default)
    {
        var matchingLines = lines
            .Where(line => line.Stops.Any(stop => string.Equals(stop.Id, stopId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (matchingLines.Length == 0)
        {
            throw new InvalidOperationException($"Mock stop '{stopId}' was not found.");
        }

        var station = matchingLines
            .SelectMany(line => line.Stops)
            .First(stop => string.Equals(stop.Id, stopId, StringComparison.OrdinalIgnoreCase));
        var timestamp = timeProvider.GetUtcNow();
        var trains = matchingLines
            .SelectMany(line => CreateTrainPositions(line).Select(position => new PTDStationTrainPosition(line.Summary, position)))
            .OrderBy(position => position.Line.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(position => position.TrainPosition.TrainId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(new PTDStationSnapshot(station, trains, timestamp));
    }

    public async Task<IPTDStationSubscription> SubscribeToStationAsync(
        string stopId,
        TimeSpan pollingInterval,
        CancellationToken cancellationToken = default)
    {
        var current = await GetStationSnapshotAsync(stopId, cancellationToken).ConfigureAwait(false);
        return new PTDStationSubscription(stopId, current, pollingInterval, token => GetStationSnapshotAsync(stopId, token));
    }

    private PTDLineSnapshot CreateLineSnapshot(MockLine line)
    {
        var timestamp = timeProvider.GetUtcNow();
        return new PTDLineSnapshot(line.Summary, line.Stops, CreateTrainPositions(line), timestamp);
    }

    private IReadOnlyList<PTDTrainPosition> CreateTrainPositions(MockLine line)
    {
        var timestamp = timeProvider.GetUtcNow();
        return line.TrainOffsets
            .Select((offset, index) => CreateTrainPosition(line, index + 1, timestamp, offset))
            .ToArray();
    }

    private PTDTrainPosition CreateTrainPosition(MockLine line, int trainNumber, DateTimeOffset timestamp, TimeSpan offset)
    {
        var stopCount = line.Stops.Count;
        var cycleDuration = TimeSpan.FromTicks(stopCount * (StopDuration + SegmentDuration).Ticks);
        var elapsedTicks = (timestamp - epoch + offset).Ticks % cycleDuration.Ticks;
        if (elapsedTicks < 0)
        {
            elapsedTicks += cycleDuration.Ticks;
        }

        var stepDuration = StopDuration + SegmentDuration;
        var stepIndex = (int)(elapsedTicks / stepDuration.Ticks);
        var stepElapsed = TimeSpan.FromTicks(elapsedTicks % stepDuration.Ticks);
        var currentStop = line.Stops[stepIndex];
        var nextStop = line.Stops[(stepIndex + 1) % stopCount];

        if (stepElapsed < StopDuration)
        {
            return new PTDTrainPosition(
                $"{line.Summary.Id}:train-{trainNumber}",
                currentStop.Id,
                nextStop.Id,
                currentStop.Latitude,
                currentStop.Longitude,
                timestamp);
        }

        var progress = (stepElapsed - StopDuration).TotalSeconds / SegmentDuration.TotalSeconds;
        var latitude = Lerp(currentStop.Latitude!.Value, nextStop.Latitude!.Value, progress);
        var longitude = Lerp(currentStop.Longitude!.Value, nextStop.Longitude!.Value, progress);

        return new PTDTrainPosition(
            $"{line.Summary.Id}:train-{trainNumber}",
            currentStop.Id,
            nextStop.Id,
            latitude,
            longitude,
            timestamp);
    }

    private static IReadOnlyList<MockLine> CreateLines()
    {
        var coreStops = Enumerable.Range(1, 6)
            .Select(index => new PTDStop($"mock:core-{index}", $"Core {index}", -27.0 + index * 0.01, 153.0 + index * 0.01, index + 3))
            .ToArray();

        return
        [
            CreateLine(MockPTDLineIds.Red, "Mock Red", "#d13f3f", "red", -27.20, 152.80, coreStops),
            CreateLine(MockPTDLineIds.Blue, "Mock Blue", "#3f6fd1", "blue", -27.30, 153.20, coreStops),
            CreateLine(MockPTDLineIds.Green, "Mock Green", "#3fa35c", "green", -26.90, 153.30, coreStops)
        ];
    }

    private static MockLine CreateLine(
        string lineId,
        string name,
        string color,
        string branchName,
        double branchLatitude,
        double branchLongitude,
        IReadOnlyList<PTDStop> coreStops)
    {
        var branchStops = Enumerable.Range(1, 3)
            .Select(index => new PTDStop(
                $"mock:{branchName}-{index}",
                $"{name} Branch {index}",
                branchLatitude + index * 0.01,
                branchLongitude + index * 0.01,
                index))
            .ToArray();
        var stops = branchStops.Concat(coreStops).ToArray();
        var summary = new PTDLineSummary(lineId, name, MockPTDLineIds.ProviderId, color);

        return new MockLine(summary, stops, [TimeSpan.Zero, TimeSpan.FromMinutes(6)]);
    }

    private static double Lerp(double start, double end, double progress)
    {
        return start + (end - start) * progress;
    }

    private sealed record MockLine(
        PTDLineSummary Summary,
        IReadOnlyList<PTDStop> Stops,
        IReadOnlyList<TimeSpan> TrainOffsets);
}

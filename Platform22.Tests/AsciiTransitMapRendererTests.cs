namespace Platform22.Tests;

using Platform22.Tui;
using PaulsTransitData.Models;
using Xunit;

public sealed class AsciiTransitMapRendererTests
{
    [Fact]
    public void RenderLineIncludesStopsTrainsAndTrack()
    {
        var snapshot = new PTDLineSnapshot(
            new PTDLineSummary("line-1", "Line One", "test", null),
            [
                new PTDStop("stop-1", "Stop 1", -27.1, 153.1, 1),
                new PTDStop("stop-2", "Stop 2", -27.2, 153.2, 2)
            ],
            [
                new PTDTrainPosition("train-1", "stop-1", "stop-2", -27.15, 153.15, DateTimeOffset.UnixEpoch)
            ],
            DateTimeOffset.UnixEpoch);
        var renderer = new AsciiTransitMapRenderer();

        var output = renderer.RenderLine(snapshot, 40, 10);

        Assert.Contains("Line: Line One", output);
        Assert.Contains("o  1: Stop 1", output);
        Assert.Contains("o  2: Stop 2", output);
        Assert.Contains("train-1", output);
        Assert.Contains("Legend: o stop, > outbound/train forward", output);
    }

    [Fact]
    public void RenderStationIncludesStationAndTrains()
    {
        var snapshot = new PTDStationSnapshot(
            new PTDStop("station-1", "Station One", -27.1, 153.1, 0),
            [
                new PTDStationTrainPosition(
                    new PTDLineSummary("line-1", "Line One", "test", null),
                    new PTDTrainPosition("train-1", "stop-1", "station-1", -27.15, 153.15, DateTimeOffset.UnixEpoch))
            ],
            DateTimeOffset.UnixEpoch);
        var renderer = new AsciiTransitMapRenderer();

        var output = renderer.RenderStation(snapshot, 40, 10);

        Assert.Contains("Station: Station One", output);
        Assert.Contains("S station", output);
        Assert.Contains("train-1", output);
        Assert.Contains("Line One", output);
        Assert.Contains('S', output);
    }
}

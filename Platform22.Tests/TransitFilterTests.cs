namespace Platform22.Tests;

using Platform22.Tui;
using PaulsTransitData.Models;
using Xunit;

public sealed class TransitFilterTests
{
    [Fact]
    public void FilterLinesMatchesNameOrId()
    {
        var lines = new[]
        {
            new PTDLineSummary("mock:red", "Mock Red", "mock", null),
            new PTDLineSummary("mock:blue", "Mock Blue", "mock", null)
        };

        var filtered = TransitFilter.FilterLines(lines, "red");

        var line = Assert.Single(filtered);
        Assert.Equal("mock:red", line.Id);
    }

    [Fact]
    public void FilterStationsMatchesNameOrId()
    {
        var stations = new[]
        {
            new PTDStationSummary("mock:core-1", "Core 1", null, null, ["mock:red"]),
            new PTDStationSummary("mock:blue-1", "Mock Blue Branch 1", null, null, ["mock:blue"])
        };

        var filtered = TransitFilter.FilterStations(stations, "core");

        var station = Assert.Single(filtered);
        Assert.Equal("mock:core-1", station.Id);
    }
}

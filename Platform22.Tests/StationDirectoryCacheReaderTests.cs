namespace Platform22.Tests;

using Platform22.Orleans;
using PaulsTransitData.Models;
using Xunit;

public sealed class StationDirectoryCacheReaderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPayload_IsNotReadable(string? json)
    {
        Assert.False(StationDirectoryCacheReader.TryRead(json, out _, out _));
    }

    [Fact]
    public void VersionedPayload_ReadsWithTimestamp()
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new StationDirectoryCache(
            [new PTDStationSummary("place-romsta", "Roma Street station", -27.4661, 153.0180, ["BDVL"])],
            new DateTimeOffset(2026, 8, 22, 1, 2, 3, TimeSpan.Zero)));

        Assert.True(StationDirectoryCacheReader.TryRead(payload, out var stations, out var updatedAt));
        Assert.Single(stations);
        Assert.Equal("place-romsta", stations[0].Id);
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 1, 2, 3, TimeSpan.Zero), updatedAt);
    }

    [Fact]
    public void LegacyArrayPayload_FallsBackWithoutTimestamp()
    {
        const string json = """[{"id":"place-romsta","name":"Roma Street station","latitude":-27.4661,"longitude":153.018,"lineIds":["BDVL"]}]""";

        Assert.True(StationDirectoryCacheReader.TryRead(json, out var stations, out var updatedAt));
        Assert.Single(stations);
        Assert.Equal(DateTimeOffset.MinValue, updatedAt);
    }

    [Fact]
    public void GarbagePayload_IsNotReadable()
    {
        Assert.False(StationDirectoryCacheReader.TryRead("{not-json", out _, out _));
    }
}

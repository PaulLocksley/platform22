namespace Platform22.Tests;

using Platform22.Orleans;
using Xunit;

[Collection("OrleansSilo")]
public sealed class GrainRoundTripTests(OrleansSiloFixture fixture)
{
    [Fact]
    public async Task StationDirectoryGrain_RoundTripsJson()
    {
        var grain = fixture.Client.GetGrain<IStationDirectoryGrain>($"test-directory-{Guid.NewGuid():N}");
        Assert.Null(await grain.GetStationsJsonAsync());

        await grain.SetStationsJsonAsync("""{"stations":[],"updatedAt":"2026-08-22T00:00:00Z"}""");
        Assert.Equal("""{"stations":[],"updatedAt":"2026-08-22T00:00:00Z"}""", await grain.GetStationsJsonAsync());
    }

    [Fact]
    public async Task LineSnapshotGrain_IsolatesByKey()
    {
        var lineA = fixture.Client.GetGrain<ILineSnapshotGrain>("line-a");
        var lineB = fixture.Client.GetGrain<ILineSnapshotGrain>("line-b");

        await lineA.SetSnapshotJsonAsync("{\"a\":1}");
        Assert.Null(await lineB.GetSnapshotJsonAsync());
        Assert.Equal("{\"a\":1}", await lineA.GetSnapshotJsonAsync());
    }

    [Fact]
    public async Task StationSnapshotGrain_OverwritesValue()
    {
        var grain = fixture.Client.GetGrain<IStationSnapshotGrain>("place-romsta");

        await grain.SetSnapshotJsonAsync("first");
        await grain.SetSnapshotJsonAsync("second");
        Assert.Equal("second", await grain.GetSnapshotJsonAsync());
    }
}

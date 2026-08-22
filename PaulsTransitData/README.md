# PaulsTransitData

PaulsTransitData provides live transit line data through provider-neutral PTD models.

Consumers should not need to know GTFS, GTFS Realtime, or provider API details.

## Public API

- `IPTDClient.GetLinesAsync()` returns known PTD lines.
- `IPTDClient.GetLineSnapshotAsync(lineId)` returns the current stops and train positions for a line.
- If a line has no current data, the client fails with a clear error.
- Translink station APIs can return all live rail vehicles on routes that serve a station.
- Station lists use IDs to reference lines. They do not nest full line objects.

## Architecture Direction

- One external poller per provider owns API polling and rate limits.
- Provider pollers map source data into PTD snapshots.
- Provider updates will be published through Valkey Streams.
- Orleans line grains will own current line state and subscriber fan-out.
- Client-facing code must not poll provider APIs directly.

## Current Slice

The first implementation includes:

- PTD public models for lines, stops, snapshots, and train positions.
- A `PTDClient` that reads current line state.
- Translink Gold Coast line mapping from mocked GTFS/static and GTFS-realtime response models.
- An in-memory line state store used by tests.

## Example

```csharp
using PaulsTransitData.Providers.Translink;

var client = new TranslinkPTDClient(new HttpClient());
var snapshot = await client.GetLineSnapshotByShortNameContainsAsync("VL");

var stops = snapshot.Stops;
var trains = snapshot.TrainPositions;

var stations = await client.GetStationsAsync();
var station = await client.GetStationSnapshotAsync("place_romsta");
var stationTrains = station.TrainPositions;

await using var subscription = await client.SubscribeToStationAsync("place_romsta", TimeSpan.FromSeconds(30));
await foreach (var update in subscription.Updates)
{
    var currentTrains = update.TrainPositions;
}
```

The `VL` value matches current Translink `route_short_name` values that contain `VL`, such as `BDVL` and `BNVL`.
Use `GetLineSnapshotByShortNameAsync("BDVL")` for an exact `route_short_name` match.
The `place_romsta` value is the Translink parent station ID for Roma Street station.

## Mock Provider

Use `MockPTDClient` in application tests.

```csharp
using PaulsTransitData.Providers.Mock;

var client = new MockPTDClient();
var lines = await client.GetLinesAsync();
var stations = await client.GetStationsAsync();
var redLine = await client.GetLineSnapshotAsync(MockPTDLineIds.Red);
var coreStation = await client.GetStationSnapshotAsync("mock:core-1");
```

The mock network has three lines. Each line has three branch stops and a shared six-stop core. Mock trains move at a constant speed and stop for 30 seconds at each stop.

namespace PaulsTransitData.Tests;

using System.IO.Compression;
using System.Net;
using Google.Protobuf;
using PaulsTransitData.Models;
using PaulsTransitData.Providers.GtfsRealtime;
using PaulsTransitData.Providers.Translink;
using PaulsTransitData.Streams;
using Xunit;

public sealed class TranslinkProviderUpdaterTests
{
    [Fact]
    public async Task RefreshGoldCoastLineLoadsRealGtfsShapeIntoClient()
    {
        var options = new TranslinkProviderOptions
        {
            StaticGtfsUrl = new Uri("https://example.test/SEQ_GTFS.zip"),
            RailVehiclePositionsUrl = new Uri("https://example.test/VehiclePositions/Rail")
        };
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == options.StaticGtfsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateStaticGtfsZip())
                };
            }

            if (request.RequestUri == options.RailVehiclePositionsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateVehiclePositionsFeed())
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var store = new InMemoryLineStateStore();
        var updater = new TranslinkProviderUpdater(new TranslinkGtfsHttpClient(httpClient, options), store);

        await updater.RefreshLineAsync(MockTranslinkGtfsProtobufResponses.GoldCoastRouteId);

        var client = new PTDClient(store);
        var snapshot = await client.GetLineSnapshotAsync(MockTranslinkGtfsProtobufResponses.GoldCoastLineId);
        Assert.Equal("Gold Coast line", snapshot.Line.Name);
        Assert.Equal(5, snapshot.Stops.Count);
        Assert.Equal(2, snapshot.TrainPositions.Count);
        Assert.Contains(snapshot.TrainPositions, train => train.TrainId == "T123" && train.NextStopId == "helensvale");
        Assert.Contains(snapshot.TrainPositions, train => train.TrainId == "T456" && train.LastStopId == "nerang");
    }

    [Fact]
    public async Task TranslinkClientGetsGoldCoastLineWithOneCall()
    {
        var options = new TranslinkProviderOptions
        {
            StaticGtfsUrl = new Uri("https://example.test/SEQ_GTFS.zip"),
            RailVehiclePositionsUrl = new Uri("https://example.test/VehiclePositions/Rail")
        };
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == options.StaticGtfsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateStaticGtfsZip())
                };
            }

            if (request.RequestUri == options.RailVehiclePositionsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateVehiclePositionsFeed())
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var client = new TranslinkPTDClient(httpClient, options);

        var snapshot = await client.GetLineSnapshotAsync(MockTranslinkGtfsProtobufResponses.GoldCoastLineId);

        Assert.Equal("Gold Coast line", snapshot.Line.Name);
        Assert.Equal(5, snapshot.Stops.Count);
        Assert.Equal(2, snapshot.TrainPositions.Count);
    }

    [Fact]
    public async Task TranslinkClientMatchesShortNameExactly()
    {
        var options = new TranslinkProviderOptions
        {
            StaticGtfsUrl = new Uri("https://example.test/SEQ_GTFS.zip"),
            RailVehiclePositionsUrl = new Uri("https://example.test/VehiclePositions/Rail")
        };
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == options.StaticGtfsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateStaticGtfsZipForShortName())
                };
            }

            if (request.RequestUri == options.RailVehiclePositionsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateVehiclePositionsFeedForShortName())
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var client = new TranslinkPTDClient(httpClient, options);

        var snapshot = await client.GetLineSnapshotByShortNameAsync("BDVL");

        Assert.Equal(TranslinkLineIds.ToShortNameLineId("BDVL"), snapshot.Line.Id);
        Assert.Equal(5, snapshot.Stops.Count);
        var train = Assert.Single(snapshot.TrainPositions);
        Assert.Equal("T123", train.TrainId);
        Assert.DoesNotContain(snapshot.TrainPositions, train => train.TrainId == "T456");
    }

    [Fact]
    public async Task TranslinkClientCanAggregateRoutesByShortNameContains()
    {
        var options = new TranslinkProviderOptions
        {
            StaticGtfsUrl = new Uri("https://example.test/SEQ_GTFS.zip"),
            RailVehiclePositionsUrl = new Uri("https://example.test/VehiclePositions/Rail")
        };
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == options.StaticGtfsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateStaticGtfsZipForShortName())
                };
            }

            if (request.RequestUri == options.RailVehiclePositionsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateVehiclePositionsFeedForShortName())
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var client = new TranslinkPTDClient(httpClient, options);

        var snapshot = await client.GetLineSnapshotByShortNameContainsAsync("VL");

        Assert.Equal(TranslinkLineIds.ToShortNameContainsLineId("VL"), snapshot.Line.Id);
        Assert.Equal(5, snapshot.Stops.Count);
        Assert.Equal(2, snapshot.TrainPositions.Count);
        Assert.Contains(snapshot.TrainPositions, train => train.TrainId == "T123");
        Assert.Contains(snapshot.TrainPositions, train => train.TrainId == "T456");
    }

    [Fact]
    public async Task TranslinkClientGetsTrainsComingThroughStation()
    {
        var options = new TranslinkProviderOptions
        {
            StaticGtfsUrl = new Uri("https://example.test/SEQ_GTFS.zip"),
            RailVehiclePositionsUrl = new Uri("https://example.test/VehiclePositions/Rail")
        };
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == options.StaticGtfsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateStaticGtfsZipForShortName())
                };
            }

            if (request.RequestUri == options.RailVehiclePositionsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateVehiclePositionsFeedForShortName())
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var client = new TranslinkPTDClient(httpClient, options);

        var snapshot = await client.GetStationSnapshotAsync("place-romsta");

        Assert.Equal("Roma Street station", snapshot.Station.Name);
        var train = Assert.Single(snapshot.TrainPositions);
        Assert.Equal("T123", train.TrainPosition.TrainId);
        Assert.Equal("Airport - Varsity Lakes", train.Line.Name);
    }

    [Fact]
    public async Task TranslinkClientListsParentStationsWithLineIds()
    {
        var options = new TranslinkProviderOptions
        {
            StaticGtfsUrl = new Uri("https://example.test/SEQ_GTFS.zip"),
            RailVehiclePositionsUrl = new Uri("https://example.test/VehiclePositions/Rail")
        };
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == options.StaticGtfsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateStaticGtfsZipForShortName())
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var client = new TranslinkPTDClient(httpClient, options);

        var stations = await client.GetStationsAsync();

        var romaStreet = stations.Single(station => station.Id == "place-romsta");
        Assert.Equal("Roma Street station", romaStreet.Name);
        Assert.Contains(TranslinkLineIds.ToPtdLineId("BDVL-4997"), romaStreet.LineIds);
        Assert.DoesNotContain(stations, station => station.Id == "roma-street");
        Assert.DoesNotContain(stations, station => station.Id == "place-bus");
    }

    [Fact]
    public async Task StationSubscriptionStartsWithCurrentData()
    {
        var options = new TranslinkProviderOptions
        {
            StaticGtfsUrl = new Uri("https://example.test/SEQ_GTFS.zip"),
            RailVehiclePositionsUrl = new Uri("https://example.test/VehiclePositions/Rail")
        };
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == options.StaticGtfsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateStaticGtfsZipForShortName())
                };
            }

            if (request.RequestUri == options.RailVehiclePositionsUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(CreateVehiclePositionsFeedForShortName())
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var client = new TranslinkPTDClient(httpClient, options);
        await using var subscription = await client.SubscribeToStationAsync("place-romsta", TimeSpan.FromMinutes(1));

        PTDStationSnapshot? firstUpdate = null;
        await foreach (var update in subscription.Updates)
        {
            firstUpdate = update;
            break;
        }

        Assert.Equal("Roma Street station", subscription.Current.Station.Name);
        Assert.NotNull(firstUpdate);
        Assert.Equal(subscription.Current, firstUpdate);
        Assert.Single(firstUpdate.TrainPositions);
    }

    private static byte[] CreateStaticGtfsZip()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "routes.txt", "route_id,route_short_name,route_long_name,route_color\nGOLD,Gold Coast,Gold Coast line,f6c343\n");
            AddEntry(archive, "trips.txt", "route_id,service_id,trip_id\nGOLD,WEEKDAY,gold-trip-1\n");
            AddEntry(archive, "stop_times.txt", string.Join('\n',
                "trip_id,arrival_time,departure_time,stop_id,stop_sequence",
                "gold-trip-1,08:00:00,08:00:00,roma-street,1",
                "gold-trip-1,08:05:00,08:05:00,park-road,2",
                "gold-trip-1,08:50:00,08:50:00,helensvale,3",
                "gold-trip-1,09:00:00,09:00:00,nerang,4",
                "gold-trip-1,09:15:00,09:15:00,varsity-lakes,5") + "\n");
            AddEntry(archive, "stops.txt", string.Join('\n',
                "stop_id,stop_name,stop_lat,stop_lon,parent_station,location_type",
                "place-romsta,Roma Street station,-27.4661,153.0180,,1",
                "roma-street,Roma Street station platform 7,-27.4661,153.0180,place-romsta,0",
                "park-road,Park Road station,-27.4996,153.0362,,0",
                "helensvale,Helensvale station,-27.9256,153.3381,,0",
                "nerang,Nerang station,-27.9890,153.3405,,0",
                "varsity-lakes,Varsity Lakes station,-28.0897,153.3892,,0") + "\n");
        }

        return stream.ToArray();
    }

    private static byte[] CreateVehiclePositionsFeed()
    {
        var feed = new FeedMessage
        {
            Header = new FeedHeader
            {
                GtfsRealtimeVersion = "2.0",
                Timestamp = 1787387400
            }
        };
        feed.Entity.Add(CreateVehicleEntity("entity-1", "T123", "helensvale", VehiclePosition.Types.VehicleStopStatus.InTransitTo, -27.7150f, 153.2020f, 1787387410));
        feed.Entity.Add(CreateVehicleEntity("entity-2", "T456", "nerang", VehiclePosition.Types.VehicleStopStatus.StoppedAt, -28.0310f, 153.3650f, 1787387412));

        return feed.ToByteArray();
    }

    private static byte[] CreateStaticGtfsZipForShortName()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "routes.txt", string.Join('\n',
                "route_id,route_short_name,route_long_name,route_color,route_type",
                "BDVL-4997,BDVL,Airport - Varsity Lakes,FFC425,2",
                "BNVL-5068,BNVL,Beenleigh - Varsity Lakes,FFC425,2",
                "BUS-1,100,Bus Route,005EB8,3") + "\n");
            AddEntry(archive, "trips.txt", string.Join('\n',
                "route_id,service_id,trip_id",
                "BDVL-4997,WEEKDAY,bdvl-trip-1",
                "BNVL-5068,WEEKDAY,bnvl-trip-1",
                "BUS-1,WEEKDAY,bus-trip-1") + "\n");
            AddEntry(archive, "stop_times.txt", string.Join('\n',
                "trip_id,arrival_time,departure_time,stop_id,stop_sequence",
                "bdvl-trip-1,08:00:00,08:00:00,roma-street,1",
                "bdvl-trip-1,08:05:00,08:05:00,park-road,2",
                "bdvl-trip-1,08:50:00,08:50:00,helensvale,3",
                "bdvl-trip-1,09:00:00,09:00:00,nerang,4",
                "bdvl-trip-1,09:15:00,09:15:00,varsity-lakes,5",
                "bnvl-trip-1,08:20:00,08:20:00,park-road,1",
                "bnvl-trip-1,09:00:00,09:00:00,helensvale,2",
                "bnvl-trip-1,09:10:00,09:10:00,nerang,3",
                "bnvl-trip-1,09:25:00,09:25:00,varsity-lakes,4",
                "bus-trip-1,08:00:00,08:00:00,bus-stop,1") + "\n");
            AddEntry(archive, "stops.txt", string.Join('\n',
                "stop_id,stop_name,stop_lat,stop_lon,parent_station,location_type",
                "place-romsta,Roma Street station,-27.4661,153.0180,,1",
                "roma-street,Roma Street station platform 7,-27.4661,153.0180,place-romsta,0",
                "park-road,Park Road station,-27.4996,153.0362,,0",
                "helensvale,Helensvale station,-27.9256,153.3381,,0",
                "nerang,Nerang station,-27.9890,153.3405,,0",
                "varsity-lakes,Varsity Lakes station,-28.0897,153.3892,,0",
                "place-bus,Bus station,-27.0000,153.0000,,1",
                "bus-stop,Bus platform,-27.0001,153.0001,place-bus,0") + "\n");
        }

        return stream.ToArray();
    }

    private static byte[] CreateVehiclePositionsFeedForShortName()
    {
        var feed = new FeedMessage
        {
            Header = new FeedHeader
            {
                GtfsRealtimeVersion = "2.0",
                Timestamp = 1787387400
            }
        };
        feed.Entity.Add(CreateVehicleEntity("entity-1", "T123", "BDVL-4997", "helensvale", VehiclePosition.Types.VehicleStopStatus.InTransitTo, -27.7150f, 153.2020f, 1787387410));
        feed.Entity.Add(CreateVehicleEntity("entity-2", "T456", "BNVL-5068", "nerang", VehiclePosition.Types.VehicleStopStatus.StoppedAt, -28.0310f, 153.3650f, 1787387412));

        return feed.ToByteArray();
    }

    private static FeedEntity CreateVehicleEntity(
        string entityId,
        string trainId,
        string stopId,
        VehiclePosition.Types.VehicleStopStatus status,
        float latitude,
        float longitude,
        ulong timestamp)
    {
        return new FeedEntity
        {
            Id = entityId,
            Vehicle = new VehiclePosition
            {
                Trip = new TripDescriptor { RouteId = "GOLD" },
                Vehicle = new VehicleDescriptor { Id = trainId },
                Position = new Position { Latitude = latitude, Longitude = longitude },
                StopId = stopId,
                CurrentStatus = status,
                Timestamp = timestamp
            }
        };
    }

    private static FeedEntity CreateVehicleEntity(
        string entityId,
        string trainId,
        string routeId,
        string stopId,
        VehiclePosition.Types.VehicleStopStatus status,
        float latitude,
        float longitude,
        ulong timestamp)
    {
        return new FeedEntity
        {
            Id = entityId,
            Vehicle = new VehiclePosition
            {
                Trip = new TripDescriptor { RouteId = routeId },
                Vehicle = new VehicleDescriptor { Id = trainId },
                Position = new Position { Latitude = latitude, Longitude = longitude },
                StopId = stopId,
                CurrentStatus = status,
                Timestamp = timestamp
            }
        };
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handle;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle)
        {
            this.handle = handle;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handle(request));
        }
    }
}

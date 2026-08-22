namespace Platform22.Tests;

using System.IO.Compression;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using PaulsTransitData.Providers.GtfsRealtime;
using PaulsTransitData.Providers.Translink;
using Platform22.OrleansHost;
using Xunit;

[Collection("OrleansSilo")]
public sealed class TranslinkSnapshotPollerTests(OrleansSiloFixture fixture)
{
    [Fact]
    public async Task PollSkipped_WhenLeaseDenied()
    {
        var services = CreateServices();
        var poller = new TranslinkSnapshotPoller(
            services,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TranslinkSnapshotPoller>.Instance,
            CreateProvider(),
            new FakeLeaseStore(pollAllowed: false));

        await poller.PollOnceAsync(CancellationToken.None);

        var lineId = PaulsTransitData.Providers.Translink.TranslinkRailLineDefinitions.Lines[0].LineId;
        Assert.Null(await fixture.Client.GetGrain<Platform22.Orleans.ILineSnapshotGrain>(lineId).GetSnapshotJsonAsync());
    }

    [Fact]
    public async Task PrewarmAndPoll_PopulateGrainCache()
    {
        var services = CreateServices();
        var leases = new FakeLeaseStore(pollAllowed: true);
        var poller = new TranslinkSnapshotPoller(
            services,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TranslinkSnapshotPoller>.Instance,
            CreateProvider(),
            leases);

        await poller.PrewarmStaticGtfsAsync(CancellationToken.None);
        await poller.PollOnceAsync(CancellationToken.None);

        var directoryJson = await fixture.Client.GetGrain<Platform22.Orleans.IStationDirectoryGrain>("translink").GetStationsJsonAsync();
        Assert.NotNull(directoryJson);

        foreach (var line in PaulsTransitData.Providers.Translink.TranslinkRailLineDefinitions.Lines)
        {
            var json = await fixture.Client.GetGrain<Platform22.Orleans.ILineSnapshotGrain>(line.LineId).GetSnapshotJsonAsync();
            Assert.NotNull(json);
        }

        Assert.Equal(1, leases.PrewarmDoneCount);
        Assert.True(leases.PollDoneCount >= 1);
    }

    private IServiceProvider CreateServices()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IGrainFactory>(fixture.Client);
        return services.BuildServiceProvider();
    }

    private static TranslinkPTDClient CreateProvider()
    {
        var options = new TranslinkProviderOptions
        {
            StaticGtfsUrl = new("http://platform22.test/gtfs.zip"),
            RailVehiclePositionsUrl = new("http://platform22.test/vehicles")
        };
        return new TranslinkPTDClient(new HttpClient(new StubHandler(options)), options);
    }

    private static byte[] CreateZipForAllCatalogLines()
    {
        var routes = new StringBuilder("route_id,route_short_name,route_long_name,route_color,route_type\n");
        var trips = new StringBuilder("route_id,service_id,trip_id\n");
        var stopTimes = new StringBuilder("trip_id,arrival_time,departure_time,stop_id,stop_sequence\n");

        foreach (var line in PaulsTransitData.Providers.Translink.TranslinkRailLineDefinitions.Lines)
        {
            foreach (var part in line.RouteShortNameParts)
            {
                var routeId = $"{part}-1";
                routes.Append($"{routeId},{part},{line.Name},FFC425,2\n");
                trips.Append($"{routeId},WEEKDAY,{part}-trip-1\n");
                stopTimes.Append($"{part}-trip-1,08:00:00,08:00:00,roma-street,1\n");
                stopTimes.Append($"{part}-trip-1,08:10:00,08:10:00,helensvale,2\n");
            }
        }

        const string stops =
            "stop_id,stop_name,stop_lat,stop_lon,parent_station,location_type\n" +
            "roma-street,Roma Street station,-27.4661,153.0180,,0\n" +
            "helensvale,Helensvale station,-27.9256,153.3381,,0\n";

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "routes.txt", routes.ToString());
            AddEntry(archive, "trips.txt", trips.ToString());
            AddEntry(archive, "stop_times.txt", stopTimes.ToString());
            AddEntry(archive, "stops.txt", stops);
        }

        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed class StubHandler(TranslinkProviderOptions options) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (string.Equals(request.RequestUri, options.StaticGtfsUrl))
            {
                return Respond(CreateZipForAllCatalogLines());
            }

            var feed = new FeedMessage { Header = new FeedHeader { GtfsRealtimeVersion = "2.0", Timestamp = 1787387400 } };
            return Respond(feed.ToByteArray());
        }

        private static Task<HttpResponseMessage> Respond(byte[] bytes)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }

    private sealed class FakeLeaseStore(bool pollAllowed) : ITranslinkPollLeaseStore
    {
        public int PrewarmDoneCount { get; private set; }
        public int PollDoneCount { get; private set; }

        public Task<bool> IsPrewarmDoneAsync() => Task.FromResult(false);

        public Task<bool> TryAcquirePrewarmLeaseAsync(TimeSpan expiry) => Task.FromResult(true);

        public Task MarkPrewarmDoneAsync()
        {
            PrewarmDoneCount++;
            return Task.CompletedTask;
        }

        public Task<bool> TryAcquirePollLeaseAsync() => Task.FromResult(pollAllowed);

        public Task MarkPollOwnerAsync() => Task.CompletedTask;

        public Task MarkPollDoneAsync()
        {
            PollDoneCount++;
            return Task.CompletedTask;
        }
    }
}

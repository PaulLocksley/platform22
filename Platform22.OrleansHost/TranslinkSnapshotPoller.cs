namespace Platform22.OrleansHost;

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaulsTransitData.Providers.Translink;
using Platform22.Orleans;
using StackExchange.Redis;

public sealed class TranslinkSnapshotPoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceProvider services;
    private readonly ILogger<TranslinkSnapshotPoller> logger;
    private readonly TranslinkPTDClient provider;
    private readonly ITranslinkPollLeaseStore leases;
    private readonly Lazy<ConnectionMultiplexer?> redis;

    public TranslinkSnapshotPoller(
        IServiceProvider services,
        ILogger<TranslinkSnapshotPoller> logger,
        TranslinkPTDClient provider,
        IConfiguration configuration)
    {
        this.services = services;
        this.logger = logger;
        this.provider = provider;
        redis = new Lazy<ConnectionMultiplexer?>(() => ConnectRedis(configuration));
        leases = CreateLeaseStore();
    }

    internal TranslinkSnapshotPoller(
        IServiceProvider services,
        ILogger<TranslinkSnapshotPoller> logger,
        TranslinkPTDClient provider,
        ITranslinkPollLeaseStore leaseStore)
    {
        this.services = services;
        this.logger = logger;
        this.provider = provider;
        leases = leaseStore;
        redis = new Lazy<ConnectionMultiplexer?>(() => null);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PrewarmStaticGtfsAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await PollOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task PrewarmStaticGtfsAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (await leases.IsPrewarmDoneAsync().ConfigureAwait(false)
                || !await leases.TryAcquirePrewarmLeaseAsync(TimeSpan.FromMinutes(2)).ConfigureAwait(false))
            {
                return;
            }

            var stations = await provider.GetStationsAsync(cancellationToken).ConfigureAwait(false);
            await services.GetRequiredService<IGrainFactory>()
                .GetGrain<IStationDirectoryGrain>("translink")
                .SetStationsJsonAsync(JsonSerializer.Serialize(new StationDirectoryCache(stations, DateTimeOffset.UtcNow)))
                .ConfigureAwait(false);

            await leases.MarkPrewarmDoneAsync().ConfigureAwait(false);
            await leases.MarkPollOwnerAsync().ConfigureAwait(false);

            Console.Error.WriteLine($"Translink static GTFS prewarm completed in {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Translink static GTFS prewarm failed after {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
        }
    }

    internal async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!await leases.TryAcquirePollLeaseAsync().ConfigureAwait(false))
            {
                return;
            }

            var grainFactory = services.GetRequiredService<IGrainFactory>();

            foreach (var line in TranslinkRailLineDefinitions.Lines)
            {
                var lineStopwatch = Stopwatch.StartNew();
                var snapshot = await provider.GetLineSnapshotByShortNameAnyAsync(line.RouteShortNameParts, cancellationToken).ConfigureAwait(false);
                await grainFactory.GetGrain<ILineSnapshotGrain>(line.LineId)
                    .SetSnapshotJsonAsync(JsonSerializer.Serialize(snapshot))
                    .ConfigureAwait(false);
                Console.Error.WriteLine($"Translink poll line {line.Name} completed in {lineStopwatch.ElapsedMilliseconds} ms");
                logger.LogInformation("Translink poll line {LineName} completed in {ElapsedMs} ms", line.Name, lineStopwatch.ElapsedMilliseconds);
            }

            Console.Error.WriteLine($"Translink poll completed in {stopwatch.ElapsedMilliseconds} ms");
            logger.LogInformation("Translink poll completed in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
            await leases.MarkPollOwnerAsync().ConfigureAwait(false);
            await leases.MarkPollDoneAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Translink poll failed after {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
        }
    }

    private ITranslinkPollLeaseStore CreateLeaseStore()
    {
        var database = redis.Value?.GetDatabase();
        return database is null ? NullPollLeaseStore.Instance : new RedisPollLeaseStore(new RedisLeaseKeyValueStore(database));
    }

    private static ConnectionMultiplexer? ConnectRedis(IConfiguration configuration)
    {
        var connectionString = OrleansEnvironment.GetValkeyConnectionString(configuration);
        return connectionString is null ? null : ConnectionMultiplexer.Connect(connectionString);
    }
}

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
    private readonly string instanceId = Guid.NewGuid().ToString("N");
    private readonly Lazy<ConnectionMultiplexer?> redis;

    public TranslinkSnapshotPoller(IServiceProvider services, ILogger<TranslinkSnapshotPoller> logger, TranslinkPTDClient provider, IConfiguration configuration)
    {
        this.services = services;
        this.logger = logger;
        this.provider = provider;
        redis = new Lazy<ConnectionMultiplexer?>(() => ConnectRedis(configuration));
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

    private async Task PrewarmStaticGtfsAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (await IsPrewarmDoneAsync().ConfigureAwait(false)
                || !await TryAcquireLeaseAsync("platform22:translink:prewarm-lock", TimeSpan.FromMinutes(2)).ConfigureAwait(false))
            {
                return;
            }

            var stations = await provider.GetStationsAsync(cancellationToken).ConfigureAwait(false);
            await services.GetRequiredService<IGrainFactory>()
                .GetGrain<IStationDirectoryGrain>("translink")
                .SetStationsJsonAsync(JsonSerializer.Serialize(new StationDirectoryCache(stations, DateTimeOffset.UtcNow)))
                .ConfigureAwait(false);

            await MarkPrewarmDoneAsync().ConfigureAwait(false);
            await MarkPollOwnerAsync().ConfigureAwait(false);

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

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!await TryAcquirePollLeaseAsync().ConfigureAwait(false))
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
            await MarkPollOwnerAsync().ConfigureAwait(false);
            await MarkPollDoneAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Translink poll failed after {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<bool> TryAcquirePollLeaseAsync()
    {
        var database = redis.Value?.GetDatabase();
        if (database is null)
        {
            return true;
        }

        if (!await IsPrewarmDoneAsync().ConfigureAwait(false))
        {
            return false;
        }

        if (!await TryRenewPollOwnerAsync().ConfigureAwait(false))
        {
            return false;
        }

        var lastPollValue = await database.StringGetAsync("platform22:translink:last-poll").ConfigureAwait(false);
        if (lastPollValue.HasValue
            && long.TryParse(lastPollValue.ToString(), out var lastPollTicks)
            && DateTimeOffset.UtcNow - new DateTimeOffset(lastPollTicks, TimeSpan.Zero) < TimeSpan.FromSeconds(20))
        {
            return false;
        }

        return await TryAcquireLeaseAsync("platform22:translink:poll-lock", TimeSpan.FromSeconds(25)).ConfigureAwait(false);
    }

    private async Task<bool> TryRenewPollOwnerAsync()
    {
        var database = redis.Value?.GetDatabase();
        if (database is null)
        {
            return true;
        }

        var owner = await database.StringGetAsync("platform22:translink:poll-owner").ConfigureAwait(false);
        if (!owner.HasValue)
        {
            return await database.StringSetAsync("platform22:translink:poll-owner", instanceId, TimeSpan.FromSeconds(90), When.NotExists).ConfigureAwait(false);
        }

        if (!string.Equals(owner.ToString(), instanceId, StringComparison.Ordinal))
        {
            return false;
        }

        await database.KeyExpireAsync("platform22:translink:poll-owner", TimeSpan.FromSeconds(90)).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryAcquireLeaseAsync(string key, TimeSpan expiry)
    {
        var database = redis.Value?.GetDatabase();
        return database is null || await database.StringSetAsync(key, instanceId, expiry, When.NotExists).ConfigureAwait(false);
    }

    private async Task<bool> IsPrewarmDoneAsync()
    {
        var database = redis.Value?.GetDatabase();
        return database is not null && await database.KeyExistsAsync("platform22:translink:prewarm-done").ConfigureAwait(false);
    }

    private async Task MarkPrewarmDoneAsync()
    {
        var database = redis.Value?.GetDatabase();
        if (database is not null)
        {
            await database.StringSetAsync("platform22:translink:prewarm-done", DateTimeOffset.UtcNow.UtcTicks, TimeSpan.FromHours(6)).ConfigureAwait(false);
        }
    }

    private async Task MarkPollOwnerAsync()
    {
        var database = redis.Value?.GetDatabase();
        if (database is not null)
        {
            await database.StringSetAsync("platform22:translink:poll-owner", instanceId, TimeSpan.FromSeconds(90)).ConfigureAwait(false);
        }
    }

    private async Task MarkPollDoneAsync()
    {
        var database = redis.Value?.GetDatabase();
        if (database is not null)
        {
            await database.StringSetAsync("platform22:translink:last-poll", DateTimeOffset.UtcNow.UtcTicks, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
        }
    }

    private static ConnectionMultiplexer? ConnectRedis(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("valkey")
            ?? configuration["ConnectionStrings:valkey"];
        return string.IsNullOrWhiteSpace(connectionString) ? null : ConnectionMultiplexer.Connect(connectionString);
    }

    private sealed record StationDirectoryCache(IReadOnlyList<PaulsTransitData.Models.PTDStationSummary> Stations, DateTimeOffset UpdatedAt);
}

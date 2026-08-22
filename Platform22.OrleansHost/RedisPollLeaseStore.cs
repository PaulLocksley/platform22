namespace Platform22.OrleansHost;

using StackExchange.Redis;

/// <summary>
/// Redis-backed lease store. Key layout (shared across silo replicas):
///   platform22:translink:prewarm-lock  - prewarm election, short expiry
///   platform22:translink:prewarm-done  - static GTFS cached flag
///   platform22:translink:poll-owner    - long-lived owner lease
///   platform22:translink:last-poll     - throttle timestamp (ticks)
///   platform22:translink:poll-lock     - per-poll election, short expiry
/// </summary>
public sealed class RedisPollLeaseStore : ITranslinkPollLeaseStore
{
    public static readonly TimeSpan PollOwnerTtl = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan PollLockTtl = TimeSpan.FromSeconds(25);
    public static readonly TimeSpan PrewarmDoneTtl = TimeSpan.FromHours(6);
    public static readonly TimeSpan LastPollDoneTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(20);

    private const string Prefix = "platform22:translink:";

    private readonly ILeaseKeyValueStore store;
    private readonly string instanceId;
    private readonly TimeProvider timeProvider;

    public RedisPollLeaseStore(ILeaseKeyValueStore store, string? instanceId = null, TimeProvider? timeProvider = null)
    {
        this.store = store;
        this.instanceId = instanceId ?? Guid.NewGuid().ToString("N");
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<bool> IsPrewarmDoneAsync()
    {
        return await store.KeyExistsAsync(Prefix + "prewarm-done").ConfigureAwait(false);
    }

    public Task<bool> TryAcquirePrewarmLeaseAsync(TimeSpan expiry)
    {
        return store.StringSetIfNotExistsAsync(Prefix + "prewarm-lock", instanceId, expiry);
    }

    public Task MarkPrewarmDoneAsync()
    {
        return store.StringSetAsync(Prefix + "prewarm-done", GetUtcNow().UtcTicks.ToString(), PrewarmDoneTtl);
    }

    public async Task<bool> TryAcquirePollLeaseAsync()
    {
        if (!await IsPrewarmDoneAsync().ConfigureAwait(false))
        {
            return false;
        }

        if (!await TryRenewPollOwnerAsync().ConfigureAwait(false))
        {
            return false;
        }

        var lastPollValue = await store.GetStringAsync(Prefix + "last-poll").ConfigureAwait(false);
        if (lastPollValue is not null
            && long.TryParse(lastPollValue, out var lastPollTicks)
            && GetUtcNow() - new DateTimeOffset(lastPollTicks, TimeSpan.Zero) < MinPollInterval)
        {
            return false;
        }

        return await store.StringSetIfNotExistsAsync(Prefix + "poll-lock", instanceId, PollLockTtl).ConfigureAwait(false);
    }

    public Task MarkPollOwnerAsync()
    {
        return store.StringSetAsync(Prefix + "poll-owner", instanceId, PollOwnerTtl);
    }

    public Task MarkPollDoneAsync()
    {
        return store.StringSetAsync(Prefix + "last-poll", GetUtcNow().UtcTicks.ToString(), LastPollDoneTtl);
    }

    private async Task<bool> TryRenewPollOwnerAsync()
    {
        var owner = await store.GetStringAsync(Prefix + "poll-owner").ConfigureAwait(false);
        if (owner is null)
        {
            return await store.StringSetIfNotExistsAsync(Prefix + "poll-owner", instanceId, PollOwnerTtl).ConfigureAwait(false);
        }

        if (!string.Equals(owner, instanceId, StringComparison.Ordinal))
        {
            return false;
        }

        await store.SetExpiryAsync(Prefix + "poll-owner", PollOwnerTtl).ConfigureAwait(false);
        return true;
    }

    private DateTimeOffset GetUtcNow()
    {
        return timeProvider.GetUtcNow();
    }
}

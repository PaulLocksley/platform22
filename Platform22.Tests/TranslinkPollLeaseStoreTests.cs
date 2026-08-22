namespace Platform22.Tests;

using System.Collections.Concurrent;
using Platform22.OrleansHost;
using Xunit;

public sealed class TranslinkPollLeaseStoreTests
{
    [Fact]
    public async Task PollDenied_BeforePrewarmDone()
    {
        var store = CreateStore(out _);

        Assert.False(await store.IsPrewarmDoneAsync());
        Assert.False(await store.TryAcquirePollLeaseAsync());
    }

    [Fact]
    public async Task PollAllowed_OncePerLockWindow()
    {
        var store = CreateStore(out var time);
        await store.MarkPrewarmDoneAsync();

        Assert.True(await store.TryAcquirePollLeaseAsync());

        // Lock held and no last-poll marker yet: second immediate attempt denied.
        Assert.False(await store.TryAcquirePollLeaseAsync());

        await store.MarkPollDoneAsync();
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(await store.TryAcquirePollLeaseAsync());

        // Past both the 20 s throttle and the 25 s poll lock.
        time.Advance(TimeSpan.FromSeconds(16));
        Assert.True(await store.TryAcquirePollLeaseAsync());
    }

    [Fact]
    public async Task ForeignInstance_CannotStealLiveOwnerLease()
    {
        var kv = new InMemoryLeaseKeyValueStore(FixedTime());
        var owner = new RedisPollLeaseStore(kv, "owner", FixedTime());
        await owner.MarkPrewarmDoneAsync();
        await owner.MarkPollOwnerAsync();

        var rival = new RedisPollLeaseStore(kv, "rival", FixedTime());
        Assert.False(await rival.TryAcquirePollLeaseAsync());
    }

    [Fact]
    public async Task ExpiredOwnerLease_CanBeTakenOver()
    {
        var time = FixedTime();
        var kv = new InMemoryLeaseKeyValueStore(time);
        var owner = new RedisPollLeaseStore(kv, "owner", time);
        await owner.MarkPrewarmDoneAsync();
        await owner.MarkPollOwnerAsync();

        time.Advance(TimeSpan.FromMinutes(5));

        var rival = new RedisPollLeaseStore(kv, "rival", time);
        Assert.True(await rival.TryAcquirePollLeaseAsync());
    }

    [Fact]
    public async Task PrewarmLease_ElectsSingleInstance()
    {
        var kv = new InMemoryLeaseKeyValueStore(FixedTime());

        Assert.True(await kv.StringSetIfNotExistsAsync("lock", "a", TimeSpan.FromMinutes(2)));
        Assert.False(await kv.StringSetIfNotExistsAsync("lock", "b", TimeSpan.FromMinutes(2)));
    }

    private static RedisPollLeaseStore CreateStore(out MutableTimeProvider time)
    {
        time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero));
        return new RedisPollLeaseStore(new InMemoryLeaseKeyValueStore(time), "test-instance", time);
    }

    private static MutableTimeProvider FixedTime()
    {
        return new MutableTimeProvider(new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero));
    }

    internal sealed class InMemoryLeaseKeyValueStore(TimeProvider clock) : ILeaseKeyValueStore
    {
        private sealed record Entry(string Value, DateTimeOffset? ExpiresAt);

        private readonly ConcurrentDictionary<string, Entry> entries = new();

        private DateTimeOffset Now => clock.GetUtcNow();

        public Task<bool> KeyExistsAsync(string key)
        {
            return Task.FromResult(Live(key) is not null);
        }

        public async Task<bool> StringSetIfNotExistsAsync(string key, string value, TimeSpan expiry)
        {
            var expiresAt = Expiry(expiry);
            return entries.TryAdd(key, new Entry(value, expiresAt))
                || (entries.TryGetValue(key, out var entry) && entry.ExpiresAt is not null && entry.ExpiresAt <= Now && entries.TryUpdate(key, new Entry(value, expiresAt), entry));
        }

        public Task<bool> StringSetAsync(string key, string value, TimeSpan expiry)
        {
            entries[key] = new Entry(value, Expiry(expiry));
            return Task.FromResult(true);
        }

        public Task<string?> GetStringAsync(string key)
        {
            return Task.FromResult<string?>(Live(key)?.Value);
        }

        public Task<bool> SetExpiryAsync(string key, TimeSpan expiry)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                return Task.FromResult(false);
            }

            entries[key] = entry with { ExpiresAt = Expiry(expiry) };
            return Task.FromResult(true);
        }

        private DateTimeOffset? Expiry(TimeSpan expiry)
        {
            return Now + expiry;
        }

        private Entry? Live(string key)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                return null;
            }

            if (entry.ExpiresAt is not null && entry.ExpiresAt <= Now)
            {
                entries.TryRemove(key, out _);
                return null;
            }

            return entry;
        }
    }
}

internal sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan delta)
    {
        UtcNow += delta;
    }
}

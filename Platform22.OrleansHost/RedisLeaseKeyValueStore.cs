namespace Platform22.OrleansHost;

using StackExchange.Redis;

public sealed class RedisLeaseKeyValueStore : ILeaseKeyValueStore
{
    private readonly IDatabase database;

    public RedisLeaseKeyValueStore(IDatabase database)
    {
        this.database = database;
    }

    public Task<bool> KeyExistsAsync(string key)
    {
        return database.KeyExistsAsync(key);
    }

    public Task<bool> StringSetIfNotExistsAsync(string key, string value, TimeSpan expiry)
    {
        return database.StringSetAsync(key, value, expiry, When.NotExists);
    }

    public Task<bool> StringSetAsync(string key, string value, TimeSpan expiry)
    {
        return database.StringSetAsync(key, value, expiry);
    }

    public async Task<string?> GetStringAsync(string key)
    {
        var value = await database.StringGetAsync(key).ConfigureAwait(false);
        return value.HasValue ? value.ToString() : null;
    }

    public Task<bool> SetExpiryAsync(string key, TimeSpan expiry)
    {
        return database.KeyExpireAsync(key, expiry);
    }
}

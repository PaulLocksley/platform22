namespace Platform22.OrleansHost;

/// <summary>
/// Narrow key-value surface the lease store needs, so tests can run without a
/// Redis server.
/// </summary>
public interface ILeaseKeyValueStore
{
    Task<bool> KeyExistsAsync(string key);

    Task<bool> StringSetIfNotExistsAsync(string key, string value, TimeSpan expiry);

    Task<bool> StringSetAsync(string key, string value, TimeSpan expiry);

    Task<string?> GetStringAsync(string key);

    Task<bool> SetExpiryAsync(string key, TimeSpan expiry);
}

namespace Platform22.OrleansHost;

/// <summary>
/// Distributed coordination for the Translink snapshot poller. Backed by Redis
/// in production; a no-op store is used when no valkey connection exists.
/// </summary>
public interface ITranslinkPollLeaseStore
{
    /// <summary>True once any instance has prewarmed the static GTFS data.</summary>
    Task<bool> IsPrewarmDoneAsync();

    /// <summary>Short-lived lease that elects one instance to run the prewarm.</summary>
    Task<bool> TryAcquirePrewarmLeaseAsync(TimeSpan expiry);

    Task MarkPrewarmDoneAsync();

    /// <summary>
    /// Full poll gate: waits for prewarm, renews the long-lived poll-owner
    /// lease, throttles to one poll per interval, then takes the poll lock.
    /// </summary>
    Task<bool> TryAcquirePollLeaseAsync();

    Task MarkPollOwnerAsync();

    Task MarkPollDoneAsync();
}

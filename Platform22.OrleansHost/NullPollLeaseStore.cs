namespace Platform22.OrleansHost;

/// <summary>
/// Lease store used when no Redis connection is configured: every poll is
/// allowed and markers are dropped, matching single-node behavior.
/// </summary>
public sealed class NullPollLeaseStore : ITranslinkPollLeaseStore
{
    public static readonly NullPollLeaseStore Instance = new();

    public Task<bool> IsPrewarmDoneAsync()
    {
        return Task.FromResult(false);
    }

    public Task<bool> TryAcquirePrewarmLeaseAsync(TimeSpan expiry)
    {
        return Task.FromResult(true);
    }

    public Task MarkPrewarmDoneAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> TryAcquirePollLeaseAsync()
    {
        return Task.FromResult(true);
    }

    public Task MarkPollOwnerAsync()
    {
        return Task.CompletedTask;
    }

    public Task MarkPollDoneAsync()
    {
        return Task.CompletedTask;
    }
}

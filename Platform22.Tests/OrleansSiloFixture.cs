namespace Platform22.Tests;

using Platform22.Orleans;
using Xunit;

/// <summary>
/// Shares one in-process Orleans silo (memory grain storage) across suites that
/// need a real grain client, keeping CI fast and Redis-free.
/// </summary>
[CollectionDefinition("OrleansSilo")]
public sealed class OrleansSiloCollection : ICollectionFixture<OrleansSiloFixture>;

public sealed class OrleansSiloFixture : IAsyncLifetime
{
    public IClusterClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("PLATFORM22_ORLEANS_MODE", null);
        var host = await Platform22OrleansHosting.StartInProcessSiloHostAsync();
        Client = await Platform22OrleansHosting.GetClientFromHostAsync(Task.FromResult(host));
    }

    public async Task DisposeAsync()
    {
        if (Client is IDisposable disposable)
        {
            disposable.Dispose();
        }

        await Task.CompletedTask;
    }
}

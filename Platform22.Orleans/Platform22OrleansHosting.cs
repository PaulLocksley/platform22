namespace Platform22.Orleans;

using System.Net;
using System.Net.Sockets;
using global::Orleans.Configuration;
using global::Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

/// <summary>
/// Shared Orleans bootstrap for the silo host and the terminal clients.
/// Keeps clustering, storage, and cluster-id wiring in one place.
/// </summary>
public static class Platform22OrleansHosting
{
    public const string GrainStorageName = "Default";

    /// <summary>
    /// Configures a silo: Kubernetes hosting when requested, Redis clustering when
    /// a valkey connection string is present, localhost otherwise.
    /// </summary>
    public static ISiloBuilder ConfigurePlatform22Clustering(
        this ISiloBuilder silo,
        string? clusteringMode,
        string? valkeyConnectionString,
        int siloPort = OrleansEnvironment.DefaultSiloPort,
        int gatewayPort = OrleansEnvironment.DefaultGatewayPort)
    {
        if (string.Equals(clusteringMode, "kubernetes", StringComparison.OrdinalIgnoreCase))
        {
            silo.UseKubernetesHosting();
        }
        else if (!string.IsNullOrWhiteSpace(valkeyConnectionString))
        {
            silo.ConfigureEndpoints(siloPort, gatewayPort);
            silo.UseRedisClustering(options =>
            {
                options.ConfigurationOptions = ConfigurationOptions.Parse(valkeyConnectionString);
            });
        }
        else
        {
            silo.UseLocalhostClustering(siloPort, gatewayPort);
        }

        // Grain snapshots are a self-repopulating cache: persist to Redis when
        // available, fall back to memory for in-process single-node runs.
        if (!string.IsNullOrWhiteSpace(valkeyConnectionString))
        {
            silo.AddRedisGrainStorage(GrainStorageName, options =>
            {
                options.ConfigurationOptions = ConfigurationOptions.Parse(valkeyConnectionString);
            });
        }
        else
        {
            silo.AddMemoryGrainStorage(GrainStorageName);
        }

        silo.Configure<ClusterOptions>(options => options.ClusterId = OrleansEnvironment.GetClusterId());
        return silo;
    }

    /// <summary>
    /// Configures a cluster client: Redis clustering against valkey when available,
    /// otherwise static or localhost clustering via ORLEANS_GATEWAY_HOST/PORT.
    /// </summary>
    public static IClientBuilder ConfigurePlatform22Clustering(
        this IClientBuilder client,
        string? clusteringMode,
        string? valkeyConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(valkeyConnectionString)
            && !string.Equals(clusteringMode, "kubernetes", StringComparison.OrdinalIgnoreCase))
        {
            client.UseRedisClustering(options =>
            {
                options.ConfigurationOptions = ConfigurationOptions.Parse(valkeyConnectionString);
            });
        }
        else
        {
            var gatewayHost = Environment.GetEnvironmentVariable(OrleansEnvironment.GatewayHostVariable);
            var gatewayPort = OrleansEnvironment.GetPort(OrleansEnvironment.GatewayPortVariable, OrleansEnvironment.DefaultGatewayPort);
            if (Uri.TryCreate(gatewayHost, UriKind.Absolute, out var gatewayUri))
            {
                gatewayHost = gatewayUri.Host;
                if (!gatewayUri.IsDefaultPort)
                {
                    gatewayPort = gatewayUri.Port;
                }
            }

            if (string.IsNullOrWhiteSpace(gatewayHost) || string.Equals(gatewayHost, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                client.UseLocalhostClustering(gatewayPort: gatewayPort);
            }
            else
            {
                var addresses = Dns.GetHostAddresses(gatewayHost);
                if (addresses.Length == 0)
                {
                    throw new InvalidOperationException($"Cannot resolve Orleans gateway host '{gatewayHost}'.");
                }

                client.UseStaticClustering(new IPEndPoint(addresses[0], gatewayPort));
            }
        }

        client.Configure<ClusterOptions>(options => options.ClusterId = OrleansEnvironment.GetClusterId());
        return client;
    }

    /// <summary>Starts an in-process single-node silo on free loopback ports.</summary>
    public static async Task<IHost> StartInProcessSiloHostAsync()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseOrleans(silo => silo.ConfigurePlatform22Clustering(null, null, GetFreeTcpPort(), GetFreeTcpPort()))
            .Build();

        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    /// <summary>Starts a host that owns only an Orleans cluster client.</summary>
    public static async Task<IHost> StartExternalClientHostAsync()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseOrleansClient(client => client.ConfigurePlatform22Clustering(
                OrleansEnvironment.GetClusteringMode(),
                OrleansEnvironment.GetValkeyConnectionString()))
            .Build();

        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    public static async Task<IClusterClient> GetClientFromHostAsync(Task<IHost> hostTask)
    {
        var host = await hostTask.ConfigureAwait(false);
        return host.Services.GetRequiredService<IClusterClient>();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

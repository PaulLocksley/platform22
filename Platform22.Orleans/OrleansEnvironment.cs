namespace Platform22.Orleans;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Single source of truth for Orleans environment settings shared by the silo
/// host, the terminal clients, and the tests.
/// </summary>
public static class OrleansEnvironment
{
    public const string DefaultClusterId = "platform22";

    public const string ClusteringModeVariable = "ORLEANS_CLUSTERING_MODE";
    public const string ClusterIdVariable = "ORLEANS_CLUSTER_ID";
    public const string GatewayHostVariable = "ORLEANS_GATEWAY_HOST";
    public const string GatewayPortVariable = "ORLEANS_GATEWAY_PORT";
    public const string SiloPortVariable = "ORLEANS_SILO_PORT";

    public const int DefaultGatewayPort = 30000;
    public const int DefaultSiloPort = 11111;

    /// <summary>True when clients must attach to a running cluster instead of hosting one.</summary>
    public static bool UseExternalOrleans()
    {
        return string.Equals(Environment.GetEnvironmentVariable("PLATFORM22_ORLEANS_MODE"), "external", StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetClusteringMode()
    {
        return Environment.GetEnvironmentVariable(ClusteringModeVariable);
    }

    public static bool IsKubernetesClustering()
    {
        return string.Equals(GetClusteringMode(), "kubernetes", StringComparison.OrdinalIgnoreCase);
    }

    public static int GetPort(string variableName, int defaultValue)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(variableName), out var port) ? port : defaultValue;
    }

    public static string GetClusterId()
    {
        return Environment.GetEnvironmentVariable(ClusterIdVariable) ?? DefaultClusterId;
    }

    /// <summary>
    /// Reads the valkey connection string from configuration or environment.
    /// Returns null when no usable value is configured.
    /// </summary>
    public static string? GetValkeyConnectionString(IConfiguration? configuration = null)
    {
        var connectionString = configuration?.GetConnectionString("valkey")
            ?? configuration?["ConnectionStrings:valkey"];
        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__valkey")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings:valkey");
        return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString;
    }
}

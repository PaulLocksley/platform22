using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using PaulsTransitData.Providers.Translink;
using Platform22.OrleansHost;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(new TranslinkPTDClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }));
builder.Services.AddHostedService<TranslinkSnapshotPoller>();

builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.UseOrleans(silo =>
{
    var siloPort = GetPort("ORLEANS_SILO_PORT", 11111);
    var gatewayPort = GetPort("ORLEANS_GATEWAY_PORT", 30000);
    if (string.Equals(Environment.GetEnvironmentVariable("ORLEANS_CLUSTERING_MODE"), "kubernetes", StringComparison.OrdinalIgnoreCase))
    {
        silo.UseKubernetesHosting();
    }
    else if (builder.Configuration.GetConnectionString("valkey") is { Length: > 0 } valkeyConnectionString)
    {
        silo.ConfigureEndpoints(siloPort, gatewayPort);
        silo.UseRedisClustering(options =>
        {
            options.ConfigurationOptions = StackExchange.Redis.ConfigurationOptions.Parse(valkeyConnectionString);
        });
    }
    else
    {
        silo.UseLocalhostClustering(siloPort, gatewayPort);
    }

    silo.AddMemoryGrainStorage("Default");
    silo.Configure<ClusterOptions>(options => options.ClusterId = GetClusterId());
});

await builder.Build().RunAsync();

static int GetPort(string name, int defaultValue)
{
    return int.TryParse(Environment.GetEnvironmentVariable(name), out var port) ? port : defaultValue;
}

static string GetClusterId()
{
    return Environment.GetEnvironmentVariable("ORLEANS_CLUSTER_ID") ?? "platform22";
}

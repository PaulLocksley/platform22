using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaulsTransitData.Providers.Translink;
using Platform22.Orleans;
using Platform22.OrleansHost;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(new TranslinkPTDClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }));
builder.Services.AddHostedService<TranslinkSnapshotPoller>();

builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.UseOrleans(silo => silo.ConfigurePlatform22Clustering(
    OrleansEnvironment.GetClusteringMode(),
    OrleansEnvironment.GetValkeyConnectionString(builder.Configuration),
    OrleansEnvironment.GetPort(OrleansEnvironment.SiloPortVariable, OrleansEnvironment.DefaultSiloPort),
    OrleansEnvironment.GetPort(OrleansEnvironment.GatewayPortVariable, OrleansEnvironment.DefaultGatewayPort)));

await builder.Build().RunAsync();

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.UseOrleans(silo =>
{
    silo.UseLocalhostClustering(
        siloPort: GetPort("ORLEANS_SILO_PORT", 11111),
        gatewayPort: GetPort("ORLEANS_GATEWAY_PORT", 30000));
    silo.AddMemoryGrainStorage("Default");
});

await builder.Build().RunAsync();

static int GetPort(string name, int defaultValue)
{
    return int.TryParse(Environment.GetEnvironmentVariable(name), out var port) ? port : defaultValue;
}

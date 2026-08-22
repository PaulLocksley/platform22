using Platform22;
using Platform22.Tui;
using PaulsTransitData.Providers.Mock;
using PaulsTransitData.Providers.Translink;

using var healthProbe = HealthProbeServer.StartFromEnvironment();
using var translinkHttpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(15)
};

var providers = new[]
{
    new TransitProviderOption("Mock", new MockMapClient(new MockPTDClient())),
    new TransitProviderOption("Translink", new TranslinkMapActor(new TranslinkPTDClient(translinkHttpClient)))
};
var initialProvider = args.Contains("--translink", StringComparer.OrdinalIgnoreCase) ? providers[1] : providers[0];

if (string.Equals(Environment.GetEnvironmentVariable("PLATFORM22_SSH_MODE"), "enabled", StringComparison.OrdinalIgnoreCase))
{
    var port = int.TryParse(Environment.GetEnvironmentVariable("PLATFORM22_SSH_PORT"), out var configuredPort) ? configuredPort : 2222;
    using var host = new SshTransitHost(providers, port);
    await host.RunAsync();
}
else if (Console.IsInputRedirected || Console.IsOutputRedirected)
{
    var app = new TransitTuiApp(initialProvider.Client, Console.In, Console.Out);
    await app.RunAsync();
}
else
{
    var app = new TerminalGuiTransitApp(providers, initialProvider);
    await app.RunAsync();
}

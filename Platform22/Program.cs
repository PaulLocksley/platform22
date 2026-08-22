using Platform22.Tui;
using PaulsTransitData.Providers.Mock;
using PaulsTransitData.Providers.Translink;

var providers = new[]
{
    new TransitProviderOption("Mock", new MockMapClient(new MockPTDClient())),
    new TransitProviderOption("Translink", new TranslinkMapClient(new TranslinkPTDClient(new HttpClient())))
};
var initialProvider = args.Contains("--translink", StringComparer.OrdinalIgnoreCase) ? providers[1] : providers[0];

if (Console.IsInputRedirected || Console.IsOutputRedirected)
{
    var app = new TransitTuiApp(initialProvider.Client, Console.In, Console.Out);
    await app.RunAsync();
}
else
{
    var app = new TerminalGuiTransitApp(providers, initialProvider);
    await app.RunAsync();
}

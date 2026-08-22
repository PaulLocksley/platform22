namespace Platform22.Tests;

using Platform22.Tui;
using Xunit;

public sealed class SshTransitShellTests
{
    [Fact]
    public async Task HelpCommand_ListsCommands()
    {
        var output = new RecordingOutput();
        var shell = CreateShell(output);

        await shell.ExecuteAsync("help");

        Assert.Contains(output.Lines, line => line.Contains("Commands:"));
        Assert.Contains(output.Lines, line => line.Contains("provider <name>"));
    }

    [Fact]
    public async Task UnknownCommand_ReportsError()
    {
        var output = new RecordingOutput();
        var shell = CreateShell(output);

        await shell.ExecuteAsync("bogus");

        Assert.Contains(output.Lines, line => line.Contains("Unknown command"));
    }

    [Fact]
    public async Task ProvidersCommand_ListsProviderNames()
    {
        var output = new RecordingOutput();
        var shell = CreateShell(output);

        await shell.ExecuteAsync("providers");

        Assert.Contains(output.Lines, line => line.Trim() == "Mock");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyCommand_ProducesNoOutput(string command)
    {
        var output = new RecordingOutput();
        var shell = CreateShell(output);

        await shell.ExecuteAsync(command);

        Assert.Empty(output.Lines);
    }

    [Theory]
    [InlineData("quit")]
    [InlineData("exit")]
    public async Task QuitCommand_ClosesOutput(string command)
    {
        var output = new RecordingOutput();
        var shell = CreateShell(output);

        await shell.ExecuteAsync(command);

        Assert.True(output.Closed);
    }

    private static SshTransitShell CreateShell(RecordingOutput output)
    {
        return new SshTransitShell(
        [
            new TransitProviderOption("Mock", new MockMapClient(new PaulsTransitData.Providers.Mock.MockPTDClient()))
        ], new AsciiTransitMapRenderer(), output);
    }

    private sealed class RecordingOutput : ISshShellOutput
    {
        public List<string> Lines { get; } = [];

        public bool Closed { get; private set; }

        public void Write(string value)
        {
            foreach (var part in value.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                Lines.Add(part);
            }
        }

        public void Close(bool sendEof = false)
        {
            Closed = true;
        }
    }
}

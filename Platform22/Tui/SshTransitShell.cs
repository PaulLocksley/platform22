namespace Platform22.Tui;

using System.Text;
using FxSsh;
using FxSsh.Services;
using PaulsTransitData.Models;

/// <summary>Output sink for the SSH shell, isolated from FxSsh for tests.</summary>
internal interface ISshShellOutput
{
    void Write(string value);

    void Close(bool sendEof = false);
}

/// <summary>FxSsh-backed output with closed-connection guarding.</summary>
internal sealed class FxSshShellOutput : ISshShellOutput
{
    private readonly Channel channel;
    private bool closed;

    public FxSshShellOutput(Channel channel)
    {
        this.channel = channel;
    }

    public bool IsClosed => closed;

    public void Write(string value)
    {
        if (closed)
        {
            return;
        }

        try
        {
            channel.SendData(Encoding.UTF8.GetBytes(value));
        }
        catch (Exception exception)
        {
            if (!SshChannelGuard.IsClosedConnectionException(exception))
            {
                Console.Error.WriteLine(exception);
            }

            closed = true;
        }
    }

    public void Close(bool sendEof = false)
    {
        if (closed)
        {
            return;
        }

        closed = true;
        try
        {
            if (sendEof)
            {
                channel.SendEof();
            }

            channel.SendClose(null);
        }
        catch (Exception exception)
        {
            if (!SshChannelGuard.IsClosedConnectionException(exception))
            {
                Console.Error.WriteLine(exception);
            }
        }
    }
}

/// <summary>
/// Line-oriented SSH shell command mode: providers, lines, stations, snapshots.
/// </summary>
internal sealed class SshTransitShell
{
    private readonly IReadOnlyList<TransitProviderOption> providers;
    private readonly AsciiTransitMapRenderer renderer;
    private readonly ISshShellOutput output;
    private readonly StringBuilder input = new();
    private TransitProviderOption provider;
    private IReadOnlyList<PTDLineSummary> lines = [];
    private IReadOnlyList<PTDStationSummary> stations = [];

    public SshTransitShell(IReadOnlyList<TransitProviderOption> providers, AsciiTransitMapRenderer renderer, Channel channel)
        : this(providers, renderer, new FxSshShellOutput(channel))
    {
        channel.DataReceived += (_, data) => OnData(data.Span);
        channel.CloseReceived += (_, _) => Close();
    }

    internal SshTransitShell(IReadOnlyList<TransitProviderOption> providers, AsciiTransitMapRenderer renderer, ISshShellOutput output)
    {
        this.providers = providers;
        this.renderer = renderer;
        this.output = output;
        provider = providers[0];
    }

    public void Start(string? commandText)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(commandText))
                {
                    await ExecuteAsync(commandText).ConfigureAwait(false);
                    Close(sendEof: true);
                    return;
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                Write($"error: {exception.Message}\r\n");
                if (!string.IsNullOrWhiteSpace(commandText))
                {
                    Close(sendEof: true);
                    return;
                }
            }

            Write("\u001b[2J\u001b[HPlatform22 SSH shell\r\n");
            WriteHelp();
            Prompt();
        });
    }

    private void OnData(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            var character = (char)value;
            if (character is '\r' or '\n')
            {
                var command = input.ToString();
                input.Clear();
                Write("\r\n");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteAsync(command).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(exception);
                        Write($"error: {exception.Message}\r\n");
                    }

                    Prompt();
                });
            }
            else if (character is '\b' or (char)127)
            {
                if (input.Length > 0)
                {
                    input.Length--;
                    Write("\b \b");
                }
            }
            else if (!char.IsControl(character))
            {
                input.Append(character);
                Write(character.ToString());
            }
        }
    }

    internal async Task ExecuteAsync(string commandText)
    {
        var parts = commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "help":
                WriteHelp();
                break;
            case "clear":
                Write("\u001b[2J\u001b[H");
                break;
            case "providers":
                WriteLines(providers.Select(option => option.Name));
                break;
            case "provider":
                await SetProviderAsync(parts.Skip(1).FirstOrDefault()).ConfigureAwait(false);
                break;
            case "lines":
                WriteLines(lines.Select(line => $"{line.Id}  {line.Name}"));
                break;
            case "stations":
                WriteLines(stations.Select(station => $"{station.Id}  {station.Name}  lines:{station.LineIds.Count}"));
                break;
            case "line":
                await ShowLineAsync(commandText[parts[0].Length..].Trim()).ConfigureAwait(false);
                break;
            case "station":
                await ShowStationAsync(commandText[parts[0].Length..].Trim()).ConfigureAwait(false);
                break;
            case "refresh":
                await RefreshAsync().ConfigureAwait(false);
                Write("refreshed\r\n");
                break;
            case "quit":
            case "exit":
                Close();
                break;
            default:
                Write("Unknown command. Type help.\r\n");
                break;
        }
    }

    private async Task SetProviderAsync(string? providerName)
    {
        var match = providers.FirstOrDefault(option => string.Equals(option.Name, providerName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            Write("Provider not found. Use: providers\r\n");
            return;
        }

        provider = match;
        await RefreshAsync().ConfigureAwait(false);
        Write($"provider: {provider.Name}\r\n");
    }

    private async Task RefreshAsync()
    {
        await provider.Client.RefreshAsync().ConfigureAwait(false);
        lines = await provider.Client.GetLinesAsync().ConfigureAwait(false);
        stations = await provider.Client.GetStationsAsync().ConfigureAwait(false);
    }

    private async Task ShowLineAsync(string value)
    {
        var line = FindByIdOrName(lines, value, line => line.Id, line => line.Name);
        if (line is null)
        {
            Write("Line not found. Use: lines\r\n");
            return;
        }

        var snapshot = await provider.Client.GetLineSnapshotAsync(line.Id).ConfigureAwait(false);
        Write(renderer.RenderLine(snapshot).Replace("\n", "\r\n"));
    }

    private async Task ShowStationAsync(string value)
    {
        var station = FindByIdOrName(stations, value, station => station.Id, station => station.Name);
        if (station is null)
        {
            Write("Station not found. Use: stations\r\n");
            return;
        }

        var snapshot = await provider.Client.GetStationSnapshotAsync(station.Id).ConfigureAwait(false);
        Write(renderer.RenderStation(snapshot).Replace("\n", "\r\n"));
    }

    private static T? FindByIdOrName<T>(IEnumerable<T> items, string value, Func<T, string?> id, Func<T, string?> name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        return items.FirstOrDefault(item =>
            string.Equals(id(item), value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name(item), value, StringComparison.OrdinalIgnoreCase)
            || name(item)?.Contains(value, StringComparison.OrdinalIgnoreCase) == true);
    }

    private void WriteHelp()
    {
        Write("Commands:\r\n");
        Write("  providers                 list providers\r\n");
        Write("  provider <name>           switch provider\r\n");
        Write("  lines                     list lines\r\n");
        Write("  stations                  list stations\r\n");
        Write("  line <id-or-name>         render line map\r\n");
        Write("  station <id-or-name>      render station map\r\n");
        Write("  refresh                   refresh provider cache\r\n");
        Write("  clear                     clear screen\r\n");
        Write("  quit                      close session\r\n");
    }

    private void WriteLines(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            Write(line + "\r\n");
        }
    }

    private void Prompt()
    {
        Write($"{provider.Name}> ");
    }

    private void Write(string value)
    {
        output.Write(value);
    }

    private void Close(bool sendEof = false)
    {
        output.Close(sendEof);
    }
}

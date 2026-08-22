namespace Platform22.Tui;

using System.Net;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Collections.Concurrent;
using FxSsh;
using FxSsh.Services;

public sealed class SshTransitHost : IDisposable
{
    private readonly IReadOnlyList<TransitProviderOption> providers;
    private readonly AsciiTransitMapRenderer renderer = new();
    private readonly ConcurrentDictionary<uint, SshPtySize> ptySizes = new();
    private readonly SshServer server;

    public SshTransitHost(IReadOnlyList<TransitProviderOption> providers, int port)
    {
        this.providers = providers;
        server = new SshServer(new StartingInfo(IPAddress.Any, port, "SSH-2.0-Platform22"));
        server.AddHostKey("ecdsa-sha2-nistp256", LoadOrCreateHostKey());
        server.ExceptionRaised += (_, exception) => Console.Error.WriteLine(exception);
        server.ConnectionAccepted += OnConnectionAccepted;
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        server.Start();
        Console.WriteLine($"Platform22 SSH server listening on port {server.StartingInfo.Port}.");
        Console.WriteLine("Connect with: ssh -p <port> platform22@localhost");

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetResult());
        return completion.Task;
    }

    public void Dispose()
    {
        server.Dispose();
    }

    private void OnConnectionAccepted(object? sender, Session session)
    {
        session.ServiceRegistered += (_, service) =>
        {
            if (service is UserAuthService userAuthService)
            {
                userAuthService.EnableNoneAuth = true;
                userAuthService.UserAuth += (_, args) => args.Result = true;
            }

            if (service is ConnectionService connectionService)
            {
                connectionService.PtyReceived += (_, args) =>
                {
                    ptySizes[args.Channel.ServerChannelId] = new SshPtySize(args.WidthChars, args.HeightRows, args.Terminal);
                };

                connectionService.CommandOpened += (_, args) => StartShell(session, args);
            }
        };
    }

    private static string LoadOrCreateHostKey()
    {
        var configuredPath = Environment.GetEnvironmentVariable("PLATFORM22_SSH_HOST_KEY_PATH");
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "platform22", "ssh_host_ecdsa_key.pem")
            : configuredPath;

        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var key = KeyGenerator.GenerateECDsaKeyPem("nistp256");
        File.WriteAllText(path, key);
        return key;
    }

    private void StartShell(Session session, CommandRequestedArgs args)
    {
        args.Agreed = true;
        if (string.IsNullOrWhiteSpace(args.CommandText) || string.Equals(args.ShellType, "shell", StringComparison.OrdinalIgnoreCase))
        {
            var size = ptySizes.GetValueOrDefault(args.Channel.ServerChannelId, new SshPtySize(120, 40, "xterm-256color"));
            var tuiSession = new SshTuiSession(session, args.Channel, size);
            tuiSession.Start();
            return;
        }

        var shell = new SshTransitShell(providers, renderer, args.Channel);
        shell.Start(args.CommandText);
    }

    private readonly record struct SshPtySize(uint Columns, uint Rows, string Terminal);

    private sealed class SshTuiSession
    {
        private readonly Channel channel;
        private readonly Session session;
        private readonly SshPtySize size;
        private Process? process;
        private bool stopping;

        public SshTuiSession(Session session, Channel channel, SshPtySize size)
        {
            this.session = session;
            this.channel = channel;
            this.size = size;
        }

        public void Start()
        {
            var assemblyPath = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("Cannot locate Platform22 assembly.");
            var command = $"env -u PLATFORM22_SSH_MODE -u PLATFORM22_ORLEANS_MODE -u ORLEANS_GATEWAY_PORT -u ORLEANS_SILO_PORT dotnet {ShellQuote(assemblyPath)}";
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "socat",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };
            process.StartInfo.ArgumentList.Add("-");
            process.StartInfo.ArgumentList.Add($"EXEC:{command},pty,setsid,ctty,raw,echo=0");
            process.StartInfo.Environment["TERM"] = string.IsNullOrWhiteSpace(size.Terminal) ? "xterm-256color" : size.Terminal;
            process.StartInfo.Environment["COLUMNS"] = Math.Max(40, size.Columns).ToString();
            process.StartInfo.Environment["LINES"] = Math.Max(10, size.Rows).ToString();
            process.StartInfo.Environment.Remove("PLATFORM22_SSH_MODE");
            process.StartInfo.Environment.Remove("PLATFORM22_ORLEANS_MODE");
            process.StartInfo.Environment.Remove("ORLEANS_GATEWAY_PORT");
            process.StartInfo.Environment.Remove("ORLEANS_SILO_PORT");

            channel.DataReceived += (_, data) =>
            {
                try
                {
                    if (process is { HasExited: false })
                    {
                        process.StandardInput.BaseStream.Write(data.Span);
                        process.StandardInput.BaseStream.Flush();
                    }
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    StopProcess();
                }
            };
            channel.CloseReceived += (_, _) => StopProcess();
            channel.EofReceived += (_, _) => StopProcess();
            session.Disconnected += (_, _) => StopProcess();

            process.Exited += (_, _) =>
            {
                if (!stopping || process.ExitCode != 137)
                {
                    Console.WriteLine($"SSH TUI process exited with code {process.ExitCode}.");
                }

                TrySendEof();
                TrySendClose();
            };

            Console.WriteLine($"Starting SSH TUI process: socat - EXEC:{command},pty,setsid,ctty,raw,echo=0");
            try
            {
                process.Start();
                _ = Task.Run(() => CopyToChannelAsync(process.StandardOutput.BaseStream));
                _ = Task.Run(() => CopyErrorsToLogAsync(process.StandardError));
            }
            catch (Exception exception)
            {
                TrySendData(Encoding.UTF8.GetBytes($"Failed to start TUI: {exception.Message}\r\n"));
                TrySendClose();
            }
        }

        private async Task CopyToChannelAsync(Stream stream)
        {
            var buffer = new byte[4096];
            while (process is { HasExited: false })
            {
                var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                if (!TrySendData(buffer.AsMemory(0, count)))
                {
                    StopProcess();
                    break;
                }
            }
        }

        private static async Task CopyErrorsToLogAsync(TextReader reader)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                Console.Error.WriteLine(line);
            }
        }

        private void StopProcess()
        {
            try
            {
                if (process is { HasExited: false })
                {
                    stopping = true;
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process can exit while FxSsh is closing the session.
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
        }

        private bool TrySendData(ReadOnlyMemory<byte> data)
        {
            try
            {
                channel.SendData(data);
                return true;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return false;
            }
        }

        private void TrySendEof()
        {
            try
            {
                channel.SendEof();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
        }

        private void TrySendClose()
        {
            try
            {
                channel.SendClose(null);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
        }

        private static string ShellQuote(string value)
        {
            return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        }
    }

    private sealed class SshTransitShell
    {
        private readonly IReadOnlyList<TransitProviderOption> providers;
        private readonly AsciiTransitMapRenderer renderer;
        private readonly Channel channel;
        private readonly StringBuilder input = new();
        private TransitProviderOption provider;
        private IReadOnlyList<PaulsTransitData.Models.PTDLineSummary> lines = [];
        private IReadOnlyList<PaulsTransitData.Models.PTDStationSummary> stations = [];

        public SshTransitShell(IReadOnlyList<TransitProviderOption> providers, AsciiTransitMapRenderer renderer, Channel channel)
        {
            this.providers = providers;
            this.renderer = renderer;
            this.channel = channel;
            provider = providers[0];
        }

        public void Start(string? commandText)
        {
            channel.DataReceived += (_, data) => OnData(data.Span);
            channel.CloseReceived += (_, _) => channel.SendClose(null);
            _ = Task.Run(async () =>
            {
                await RefreshAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(commandText))
                {
                    await ExecuteAsync(commandText).ConfigureAwait(false);
                    channel.SendEof();
                    channel.SendClose(null);
                    return;
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
                        await ExecuteAsync(command).ConfigureAwait(false);
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

        private async Task ExecuteAsync(string commandText)
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
                    channel.SendClose(null);
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
            var line = FindByIdOrName(lines, value);
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
            var station = FindByIdOrName(stations, value);
            if (station is null)
            {
                Write("Station not found. Use: stations\r\n");
                return;
            }

            var snapshot = await provider.Client.GetStationSnapshotAsync(station.Id).ConfigureAwait(false);
            Write(renderer.RenderStation(snapshot).Replace("\n", "\r\n"));
        }

        private static T? FindByIdOrName<T>(IEnumerable<T> items, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return default;
            }

            return items.FirstOrDefault(item =>
            {
                var id = item?.GetType().GetProperty("Id")?.GetValue(item)?.ToString();
                var name = item?.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
                return string.Equals(id, value, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, value, StringComparison.OrdinalIgnoreCase)
                    || name?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
            });
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
            channel.SendData(Encoding.UTF8.GetBytes(value));
        }
    }
}

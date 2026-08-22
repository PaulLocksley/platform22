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
        server.ExceptionRaised += (_, exception) =>
        {
            Console.Error.WriteLine(exception);
        };
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
        ExtendSessionTimeout(session);

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

    private static void ExtendSessionTimeout(Session session)
    {
        var timeoutField = typeof(Session).GetField("_timeout", BindingFlags.Instance | BindingFlags.NonPublic);
        timeoutField?.SetValue(session, TimeSpan.FromHours(12));
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

    private static bool IsClosedConnectionException(Exception exception)
    {
        return exception is NullReferenceException
            && exception.StackTrace?.Contains("FxSsh.Session.SocketWrite", StringComparison.Ordinal) == true;
    }

    private readonly record struct SshPtySize(uint Columns, uint Rows, string Terminal);

    private sealed class SshTuiSession
    {
        private readonly Channel channel;
        private readonly Session session;
        private readonly SshPtySize size;
        private Process? process;
        private bool sessionClosed;
        private bool awaitingRestart;

        public SshTuiSession(Session session, Channel channel, SshPtySize size)
        {
            this.session = session;
            this.channel = channel;
            this.size = size;
        }

        public void Start()
        {
            channel.DataReceived += (_, data) => OnDataReceived(data.Span);
            channel.CloseReceived += (_, _) => CloseSessionProcess("channel close received");
            channel.EofReceived += (_, _) => CloseSessionProcess("channel EOF received");
            session.Disconnected += (_, _) => CloseSessionProcess("session disconnected");

            StartProcess();
        }

        private void StartProcess()
        {
            var assemblyPath = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("Cannot locate Platform22 assembly.");
            var command = $"env -u PLATFORM22_SSH_MODE dotnet {ShellQuote(assemblyPath)}";
            var nextProcess = new Process
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
            nextProcess.StartInfo.ArgumentList.Add("-");
            nextProcess.StartInfo.ArgumentList.Add($"EXEC:{command},pty,setsid,ctty,raw,echo=0");
            nextProcess.StartInfo.Environment["TERM"] = string.IsNullOrWhiteSpace(size.Terminal) ? "xterm-256color" : size.Terminal;
            nextProcess.StartInfo.Environment["COLUMNS"] = Math.Max(40, size.Columns).ToString();
            nextProcess.StartInfo.Environment["LINES"] = Math.Max(10, size.Rows).ToString();
            nextProcess.StartInfo.Environment.Remove("PLATFORM22_SSH_MODE");

            nextProcess.Exited += (_, _) => OnProcessExited(nextProcess);

            try
            {
                awaitingRestart = false;
                process = nextProcess;
                nextProcess.Start();
                Console.WriteLine($"SSH TUI process started for channel {channel.ServerChannelId} with PID {nextProcess.Id}.");
                _ = Task.Run(() => CopyToChannelAsync(nextProcess, nextProcess.StandardOutput.BaseStream));
                _ = Task.Run(() => CopyErrorsToLogAsync(nextProcess.StandardError));
            }
            catch (Exception exception)
            {
                // TODO: Send SSH TUI startup failures to Sentry.
                Console.Error.WriteLine(exception);
                awaitingRestart = true;
                TrySendData(Encoding.UTF8.GetBytes($"\r\nFailed to start Platform22 TUI: {exception.Message}\r\nPress r to retry, or q to quit.\r\n"));
            }
        }

        private void OnDataReceived(ReadOnlySpan<byte> data)
        {
            if (awaitingRestart)
            {
                HandleRestartInput(data);
                return;
            }

            try
            {
                if (process is { HasExited: false })
                {
                    process.StandardInput.BaseStream.Write(data);
                    process.StandardInput.BaseStream.Flush();
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                StopProcess("stdin write failed");
            }
        }

        private void HandleRestartInput(ReadOnlySpan<byte> data)
        {
            foreach (var value in data)
            {
                var character = char.ToLowerInvariant((char)value);
                if (character == 'q')
                {
                    TrySendClose();
                    return;
                }

                if (character is 'r' or '\r' or '\n' or ' ')
                {
                    TrySendData(Encoding.UTF8.GetBytes("\r\nRestarting Platform22 TUI...\r\n"));
                    StartProcess();
                    return;
                }
            }
        }

        private void OnProcessExited(Process exitedProcess)
        {
            var exitCode = exitedProcess.ExitCode;
            var expectedStop = sessionClosed || exitCode == 0;
            Console.WriteLine($"SSH TUI process {exitedProcess.Id} exited with code {exitCode} for channel {channel.ServerChannelId}; sessionClosed={sessionClosed}.");

            if (expectedStop)
            {
                if (!sessionClosed)
                {
                    TrySendEof();
                    TrySendClose();
                }

                return;
            }

            // TODO: Send SSH TUI crashes to Sentry.
            awaitingRestart = true;
            TrySendData(Encoding.UTF8.GetBytes($"\u001b[?1049l\u001b[0m\u001b[?25h\r\n\r\nPlatform22 TUI crashed with exit code {exitCode}. The SSH session is still open.\r\nPress r or Enter to restart, or q to quit.\r\n"));
        }

        private async Task CopyToChannelAsync(Process sourceProcess, Stream stream)
        {
            var buffer = new byte[4096];
            while (!sourceProcess.HasExited)
            {
                var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                if (!TrySendData(buffer.AsMemory(0, count)))
                {
                    StopProcess("channel send failed");
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

        private void StopProcess(string reason)
        {
            try
            {
                if (process is { HasExited: false })
                {
                    Console.WriteLine($"Stopping SSH TUI process {process.Id} for channel {channel.ServerChannelId}: {reason}.");
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

        private void CloseSessionProcess(string reason)
        {
            Console.WriteLine($"Closing SSH TUI session for channel {channel.ServerChannelId}: {reason}.");
            sessionClosed = true;
            StopProcess(reason);
        }

        private bool TrySendData(ReadOnlyMemory<byte> data)
        {
            if (sessionClosed)
            {
                return false;
            }

            try
            {
                channel.SendData(data);
                return true;
            }
            catch (Exception exception)
            {
                if (!IsClosedConnectionException(exception))
                {
                    Console.Error.WriteLine(exception);
                }
                return false;
            }
        }

        private void TrySendEof()
        {
            if (sessionClosed)
            {
                return;
            }

            try
            {
                channel.SendEof();
            }
            catch (Exception exception)
            {
                if (!IsClosedConnectionException(exception))
                {
                    Console.Error.WriteLine(exception);
                }
            }
        }

        private void TrySendClose()
        {
            if (sessionClosed)
            {
                return;
            }

            try
            {
                channel.SendClose(null);
            }
            catch (Exception exception)
            {
                if (!IsClosedConnectionException(exception))
                {
                    Console.Error.WriteLine(exception);
                }
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
        private bool closed;

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
            channel.CloseReceived += (_, _) => Close();
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
                if (!IsClosedConnectionException(exception))
                {
                    Console.Error.WriteLine(exception);
                }

                closed = true;
            }
        }

        private void Close(bool sendEof = false)
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
                if (!IsClosedConnectionException(exception))
                {
                    Console.Error.WriteLine(exception);
                }
            }
        }
    }
}

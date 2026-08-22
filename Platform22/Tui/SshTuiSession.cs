namespace Platform22.Tui;

using System.Diagnostics;
using System.Reflection;
using System.Text;
using FxSsh;
using FxSsh.Services;

internal readonly record struct SshPtySize(uint Columns, uint Rows, string Terminal);

/// <summary>
/// Runs the Terminal.Gui TUI for one SSH channel through a child socat process,
/// with crash-restart handling (r restarts, q quits).
/// </summary>
internal sealed class SshTuiSession
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
            if (!SshChannelGuard.IsClosedConnectionException(exception))
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
            if (!SshChannelGuard.IsClosedConnectionException(exception))
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
            if (!SshChannelGuard.IsClosedConnectionException(exception))
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

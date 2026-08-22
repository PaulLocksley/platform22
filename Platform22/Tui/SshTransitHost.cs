namespace Platform22.Tui;

using System.Collections.Concurrent;
using System.Net;
using FxSsh;
using FxSsh.Services;

public sealed class SshTransitHost : IDisposable
{
    private readonly IReadOnlyList<TransitProviderOption> providers;
    private readonly AsciiTransitMapRenderer renderer = new();
    private readonly ConcurrentDictionary<uint, SshPtySize> ptySizes = new();
    private readonly SshServer server;
    private readonly SshAuthPolicy authPolicy;

    public SshTransitHost(IReadOnlyList<TransitProviderOption> providers, int port, SshAuthPolicy? authPolicy = null)
    {
        this.providers = providers;
        this.authPolicy = authPolicy ?? SshAuthPolicy.FromEnvironment();
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
        // Replaces the old private _timeout reflection hack (removed upstream).
        // Probes idle sessions so dead peers are reaped while live sessions stay open.
        session.ConfigureKeepalive(TimeSpan.FromMinutes(1));

        session.ServiceRegistered += (_, service) =>
        {
            if (service is UserAuthService userAuthService)
            {
                authPolicy.Configure(userAuthService);
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

    internal static string LoadOrCreateHostKey()
    {
        var configuredPath = Environment.GetEnvironmentVariable(SshAuthPolicy.HostKeyPathVariable);
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
}

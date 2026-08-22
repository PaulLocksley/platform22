namespace Platform22;

using System.Net;
using System.Text;

public sealed class HealthProbeServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;

    private HealthProbeServer(int port)
    {
        listener.Prefixes.Add($"http://*:{port}/");
        listener.Start();
        loop = Task.Run(() => RunAsync(stop.Token));
    }

    public static HealthProbeServer? StartFromEnvironment()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("PLATFORM22_HEALTH_PORT"), out var port) && port > 0
            ? new HealthProbeServer(port)
            : null;
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Close();
        try
        {
            loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || !listener.IsListening)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context), cancellationToken);
        }
    }

    private static async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var ok = path is "/health" or "/alive";
        var body = Encoding.UTF8.GetBytes(ok ? "OK" : "Not found");
        context.Response.StatusCode = ok ? StatusCodes.Status200OK : StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        context.Response.Close();
    }

    private static class StatusCodes
    {
        public const int Status200OK = 200;
        public const int Status404NotFound = 404;
    }
}

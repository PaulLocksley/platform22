namespace PaulsTransitData.Providers.Translink;

using System.Diagnostics;
using System.IO.Compression;
using PaulsTransitData.Models;
using PaulsTransitData.Streams;

public sealed class TranslinkGtfsHttpClient
{
    private static readonly TimeSpan StaticGtfsTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan RealtimeTtl = TimeSpan.FromSeconds(10);
    private readonly HttpClient httpClient;
    private readonly TranslinkProviderOptions options;
    private readonly TranslinkGtfsMapper mapper = new();
    private readonly SemaphoreSlim fetchLock = new(1, 1);
    private readonly object staticGtfsLock = new();
    private CachedFeed? staticGtfs;
    private CachedFeed? railVehiclePositions;
    private ParsedStaticGtfs? parsedStaticGtfs;

    public TranslinkGtfsHttpClient(HttpClient httpClient, TranslinkProviderOptions? options = null)
    {
        this.httpClient = httpClient;
        this.options = options ?? new TranslinkProviderOptions();
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var (staticBytes, realtimeBytes) = await FetchStaticAndRealtimeAsync(cancellationToken).ConfigureAwait(false);
        var staticGtfs = GetParsedStaticGtfs(staticBytes);
        var lineId = TranslinkLineIds.ToPtdLineId(routeId);

        var staticResponse = TranslinkGtfsResponseComposer.ParseStaticGtfsByRouteIds(staticGtfs, [routeId], lineId, routeId);
        var realtimeResponse = TranslinkGtfsRealtimeParser.ParseRealtime(realtimeBytes, new HashSet<string>([routeId], StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateByShortNameAsync(string routeShortName, CancellationToken cancellationToken = default)
    {
        var (staticBytes, realtimeBytes) = await FetchStaticAndRealtimeAsync(cancellationToken).ConfigureAwait(false);
        var staticGtfs = GetParsedStaticGtfs(staticBytes);
        var lineId = TranslinkLineIds.ToShortNameLineId(routeShortName);
        var routes = staticGtfs.Routes
            .Where(route => TranslinkGtfsRows.IsRailRoute(route) && string.Equals(route.GetValueOrDefault("route_short_name"), routeShortName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (routes.Length == 0)
        {
            throw new InvalidOperationException($"No Translink routes matched short name '{routeShortName}'.");
        }

        var routeIds = routes.Select(route => route["route_id"]).ToArray();
        var staticResponse = TranslinkGtfsResponseComposer.ParseStaticGtfsByRouteIds(staticGtfs, routeIds, lineId, routeShortName);
        var realtimeResponse = TranslinkGtfsRealtimeParser.ParseRealtime(realtimeBytes, routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateByShortNameContainsAsync(string routeShortNamePart, CancellationToken cancellationToken = default)
    {
        var (staticBytes, realtimeBytes) = await FetchStaticAndRealtimeAsync(cancellationToken).ConfigureAwait(false);
        var staticGtfs = GetParsedStaticGtfs(staticBytes);
        var lineId = TranslinkLineIds.ToShortNameContainsLineId(routeShortNamePart);
        var routes = staticGtfs.Routes
            .Where(route => TranslinkGtfsRows.IsRailRoute(route) && route.GetValueOrDefault("route_short_name")?.Contains(routeShortNamePart, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        if (routes.Length == 0)
        {
            throw new InvalidOperationException($"No Translink routes matched short name part '{routeShortNamePart}'.");
        }

        var routeIds = routes.Select(route => route["route_id"]).ToArray();
        var staticResponse = TranslinkGtfsResponseComposer.ParseStaticGtfsByRouteIds(staticGtfs, routeIds, lineId, routeShortNamePart);
        var realtimeResponse = TranslinkGtfsRealtimeParser.ParseRealtime(realtimeBytes, routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDProviderLineUpdate> GetLineUpdateByShortNameAnyAsync(IReadOnlyCollection<string> routeShortNameParts, CancellationToken cancellationToken = default)
    {
        var parts = routeShortNameParts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        if (parts.Length == 0)
        {
            throw new ArgumentException("At least one route short name part is required.", nameof(routeShortNameParts));
        }

        var (staticBytes, realtimeBytes) = await FetchStaticAndRealtimeAsync(cancellationToken).ConfigureAwait(false);
        var staticGtfs = GetParsedStaticGtfs(staticBytes);
        var lineId = TranslinkLineIds.ToShortNameAnyLineId(parts);
        var routes = staticGtfs.Routes
            .Where(route => TranslinkGtfsRows.IsRailRoute(route) && parts.Any(part => route.GetValueOrDefault("route_short_name")?.Contains(part, StringComparison.OrdinalIgnoreCase) == true))
            .ToArray();

        if (routes.Length == 0)
        {
            throw new InvalidOperationException($"No Translink routes matched short name parts '{string.Join(", ", parts)}'.");
        }

        var routeIds = routes.Select(route => route["route_id"]).ToArray();
        var staticResponse = TranslinkGtfsResponseComposer.ParseStaticGtfsByRouteIds(staticGtfs, routeIds, lineId, string.Join(" / ", parts));
        var realtimeResponse = TranslinkGtfsRealtimeParser.ParseRealtime(realtimeBytes, routeIds.ToHashSet(StringComparer.OrdinalIgnoreCase), lineId);

        return mapper.MapLineUpdate(staticResponse, realtimeResponse, lineId);
    }

    public async Task<PTDStationSnapshot> GetStationSnapshotAsync(string stopId, CancellationToken cancellationToken = default)
    {
        var (staticBytes, realtimeBytes) = await FetchStaticAndRealtimeAsync(cancellationToken).ConfigureAwait(false);
        var staticGtfs = GetParsedStaticGtfs(staticBytes);

        return TranslinkGtfsResponseComposer.ParseStationSnapshot(staticGtfs, realtimeBytes, stopId);
    }

    public async Task<IReadOnlyList<PTDStationSummary>> GetStationsAsync(CancellationToken cancellationToken = default)
    {
        var staticBytes = await FetchBytesAsync("static GTFS", options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        return TranslinkGtfsResponseComposer.ParseStations(GetParsedStaticGtfs(staticBytes));
    }

    private async Task<(byte[] StaticBytes, byte[] RealtimeBytes)> FetchStaticAndRealtimeAsync(CancellationToken cancellationToken)
    {
        var staticBytes = await FetchBytesAsync("static GTFS", options.StaticGtfsUrl, cancellationToken).ConfigureAwait(false);
        var realtimeBytes = await FetchBytesAsync("rail vehicle positions", options.RailVehiclePositionsUrl, cancellationToken).ConfigureAwait(false);
        return (staticBytes, realtimeBytes);
    }

    private async Task<byte[]> FetchBytesAsync(string name, Uri uri, CancellationToken cancellationToken)
    {
        var ttl = string.Equals(uri.AbsoluteUri, options.StaticGtfsUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
            ? StaticGtfsTtl
            : RealtimeTtl;
        var cached = GetCachedFeed(uri);
        if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < ttl)
        {
            return cached.Bytes;
        }

        await fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = GetCachedFeed(uri);
            if (cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < ttl)
            {
                return cached.Bytes;
            }

            var bytes = await FetchBytesUncachedAsync(name, uri, cancellationToken).ConfigureAwait(false);
            SetCachedFeed(uri, new CachedFeed(bytes, DateTimeOffset.UtcNow));
            return bytes;
        }
        finally
        {
            fetchLock.Release();
        }
    }

    private async Task<byte[]> FetchBytesUncachedAsync(string name, Uri uri, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        Console.Error.WriteLine($"Translink fetch start: {name} {uri} at {startedAt:O}");

        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        Console.Error.WriteLine(
            $"Translink fetch end: {name} status={(int)response.StatusCode} {response.ReasonPhrase} bytes={bytes.Length} durationMs={stopwatch.ElapsedMilliseconds} {GetRateLimitText(response)}");
        response.EnsureSuccessStatusCode();

        return bytes;
    }

    private CachedFeed? GetCachedFeed(Uri uri)
    {
        return string.Equals(uri.AbsoluteUri, options.StaticGtfsUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
            ? staticGtfs
            : railVehiclePositions;
    }

    private void SetCachedFeed(Uri uri, CachedFeed feed)
    {
        if (string.Equals(uri.AbsoluteUri, options.StaticGtfsUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
        {
            staticGtfs = feed;
        }
        else
        {
            railVehiclePositions = feed;
        }
    }

    private static string GetRateLimitText(HttpResponseMessage response)
    {
        var headers = new[]
            {
                "RateLimit-Limit",
                "RateLimit-Remaining",
                "RateLimit-Reset",
                "X-RateLimit-Limit",
                "X-RateLimit-Remaining",
                "X-RateLimit-Reset",
                "Retry-After"
            }
            .Select(header => TryGetHeader(response, header))
            .Where(value => value is not null)
            .ToArray();

        return headers.Length == 0 ? "rateLimitHeaders=none" : string.Join(' ', headers);
    }

    private static string? TryGetHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
        {
            return $"{name}={string.Join(',', values)}";
        }

        return null;
    }

    private ParsedStaticGtfs GetParsedStaticGtfs(byte[] zipBytes)
    {
        lock (staticGtfsLock)
        {
            if (parsedStaticGtfs is not null && ReferenceEquals(parsedStaticGtfs.SourceBytes, zipBytes))
            {
                return parsedStaticGtfs;
            }

            var stopwatch = Stopwatch.StartNew();
            using var stream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var routes = TranslinkGtfsCsv.ReadCsv(archive, "routes.txt");
            var trips = TranslinkGtfsCsv.ReadCsv(archive, "trips.txt");
            var stopTimes = TranslinkGtfsCsv.ReadCsv(archive, "stop_times.txt");
            var stops = TranslinkGtfsCsv.ReadCsv(archive, "stops.txt");
            parsedStaticGtfs = ParsedStaticGtfs.Create(zipBytes, routes, trips, stopTimes, stops);
            Console.Error.WriteLine($"Translink static GTFS parsed in {stopwatch.ElapsedMilliseconds} ms");
            return parsedStaticGtfs;
        }
    }

    private sealed record CachedFeed(byte[] Bytes, DateTimeOffset FetchedAt);
}

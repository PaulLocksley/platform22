namespace Platform22.Tui;

using PaulsTransitData.Models;

public sealed class TransitTuiApp
{
    private readonly ITransitMapClient client;
    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly AsciiTransitMapRenderer renderer = new();
    private string filter = string.Empty;
    private TransitTuiMode mode = TransitTuiMode.Lines;

    public TransitTuiApp(ITransitMapClient client, TextReader input, TextWriter output)
    {
        this.client = client;
        this.input = input;
        this.output = output;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await client.RefreshAsync(cancellationToken).ConfigureAwait(false);
        var lines = await client.GetLinesAsync(cancellationToken).ConfigureAwait(false);
        var stations = await client.GetStationsAsync(cancellationToken).ConfigureAwait(false);
        PTDLineSummary? selectedLine = lines.FirstOrDefault();
        PTDStationSummary? selectedStation = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            RenderHeader(lines, stations);
            if (mode == TransitTuiMode.Lines)
            {
                var filtered = TransitFilter.FilterLines(lines, filter);
                selectedLine = filtered.FirstOrDefault() ?? selectedLine;
                RenderList(filtered.Select(line => $"{line.Name} ({line.Id})"));
                if (selectedLine is not null)
                {
                    var snapshot = await client.GetLineSnapshotAsync(selectedLine.Id, cancellationToken).ConfigureAwait(false);
                    output.WriteLine(renderer.RenderLine(snapshot));
                }
            }
            else
            {
                var filtered = TransitFilter.FilterStations(stations, filter);
                selectedStation = filtered.FirstOrDefault() ?? selectedStation;
                RenderList(filtered.Select(station => $"{station.Name} ({station.Id}) lines:{station.LineIds.Count}"));
                if (selectedStation is not null)
                {
                    var snapshot = await client.GetStationSnapshotAsync(selectedStation.Id, cancellationToken).ConfigureAwait(false);
                    output.WriteLine(renderer.RenderStation(snapshot));
                }
            }

            output.WriteLine("Keys: text filters, Tab switches lines/stations, Backspace edits, q quits, Enter refreshes.");
            var key = ReadKey();
            if (key == ConsoleKey.Q)
            {
                return;
            }

            if (key == ConsoleKey.Tab)
            {
                mode = mode == TransitTuiMode.Lines ? TransitTuiMode.Stations : TransitTuiMode.Lines;
                filter = string.Empty;
            }
            else if (key == ConsoleKey.Backspace && filter.Length > 0)
            {
                filter = filter[..^1];
            }
            else if (key is not ConsoleKey.Enter && key is not ConsoleKey.Backspace)
            {
                var character = (char)key;
                if (!char.IsControl(character))
                {
                    filter += character;
                }
            }
        }
    }

    private void RenderHeader(IReadOnlyList<PTDLineSummary> lines, IReadOnlyList<PTDStationSummary> stations)
    {
        Console.Clear();
        output.WriteLine($"Platform22 Transit Map - {client.Name}");
        output.WriteLine($"Mode: {mode}  Filter: '{filter}'  Lines: {lines.Count}  Stations: {stations.Count}");
        output.WriteLine(new string('-', 80));
    }

    private void RenderList(IEnumerable<string> items)
    {
        foreach (var item in items.Take(8))
        {
            output.WriteLine($"> {item}");
        }

        output.WriteLine(new string('-', 80));
    }

    private ConsoleKey ReadKey()
    {
        if (input == Console.In && !Console.IsInputRedirected)
        {
            return Console.ReadKey(intercept: true).Key;
        }

        var value = input.Read();
        return value switch
        {
            -1 => ConsoleKey.Q,
            '\t' => ConsoleKey.Tab,
            '\b' => ConsoleKey.Backspace,
            '\n' => ConsoleKey.Enter,
            '\r' => ConsoleKey.Enter,
            _ => (ConsoleKey)char.ToUpperInvariant((char)value)
        };
    }
}

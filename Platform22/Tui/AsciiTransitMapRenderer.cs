namespace Platform22.Tui;

using System.Text;
using PaulsTransitData.Models;

public sealed class AsciiTransitMapRenderer
{
    public string RenderLine(PTDLineSnapshot snapshot, int width = 76, int height = 22)
    {
        var usableWidth = Math.Max(width, 32);
        var stopColumns = GetStopColumns(snapshot.Stops, usableWidth);
        var trainRow = new char[usableWidth];
        var trackRow = new char[usableWidth];
        Array.Fill(trainRow, ' ');
        Array.Fill(trackRow, '-');

        foreach (var stop in snapshot.Stops)
        {
            trackRow[stopColumns[stop.Id]] = 'o';
        }

        foreach (var train in snapshot.TrainPositions)
        {
            var column = GetTrainColumn(train, stopColumns);
            if (column is null)
            {
                continue;
            }

            var marker = GetTrainMarker(train, stopColumns);
            trainRow[column.Value] = trainRow[column.Value] == ' ' ? marker : '+';
            trackRow[column.Value] = trackRow[column.Value] is '>' or '<' or '+' ? '+' : marker;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Line: {snapshot.Line.Name}  Stops: {snapshot.Stops.Count}  Trains: {snapshot.TrainPositions.Count}");
        builder.AppendLine($" {new string(trainRow)} ");
        builder.AppendLine($" {new string(trackRow)} ");
        builder.AppendLine(RenderStopIndex(snapshot.Stops, height));
        builder.AppendLine(RenderTrainList(snapshot.TrainPositions));
        builder.AppendLine("Legend: o stop, > outbound/train forward, < inbound/train reverse, + multiple trains");
        return builder.ToString();
    }

    public string RenderStation(PTDStationSnapshot snapshot, int width = 76, int height = 16)
    {
        var usableWidth = Math.Max(width, 40);
        var center = usableWidth / 2;
        var groupedTrains = snapshot.TrainPositions
            .GroupBy(train => train.Line.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.First().Line.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"Station: {snapshot.Station.Name} ({snapshot.Station.Id})");
        builder.AppendLine($"Trains: {snapshot.TrainPositions.Count}");
        builder.AppendLine("Legend: S station, < inbound, > outbound, + multiple trains");
        builder.AppendLine();

        foreach (var group in groupedTrains.Take(Math.Max(height / 2, 6)))
        {
            var line = group.First().Line;
            var row = CreateStationTrackRow(usableWidth, center, snapshot.Station.Id, group.Select(train => train.TrainPosition));
            builder.AppendLine($"{line.Name}");
            builder.AppendLine(row);
        }

        if (groupedTrains.Length > Math.Max(height / 2, 6))
        {
            builder.AppendLine($"... {groupedTrains.Length - Math.Max(height / 2, 6)} more lines");
        }

        builder.AppendLine();
        foreach (var train in snapshot.TrainPositions.Take(8))
        {
            builder.AppendLine($"{GetDirectionMarker(train.TrainPosition, snapshot.Station.Id)} {train.TrainPosition.TrainId}  {train.Line.Name}  {train.TrainPosition.LastStopId} -> {train.TrainPosition.NextStopId}");
        }

        return builder.ToString();
    }

    private static string CreateStationTrackRow(int width, int center, string stationId, IEnumerable<PTDTrainPosition> trains)
    {
        var row = new char[width];
        Array.Fill(row, '-');
        row[center] = 'S';

        var inboundColumn = Math.Max(0, center - 8);
        var outboundColumn = Math.Min(width - 1, center + 8);
        var passingColumn = Math.Min(width - 1, center + 14);

        foreach (var train in trains)
        {
            var marker = GetDirectionMarker(train, stationId);
            var column = marker switch
            {
                '<' => inboundColumn,
                '>' => outboundColumn,
                _ => passingColumn
            };

            row[column] = row[column] is '<' or '>' or 'T' ? '+' : marker;
        }

        return new string(row);
    }

    private static char GetDirectionMarker(PTDTrainPosition train, string stationId)
    {
        if (string.Equals(train.NextStopId, stationId, StringComparison.OrdinalIgnoreCase))
        {
            return '<';
        }

        if (string.Equals(train.LastStopId, stationId, StringComparison.OrdinalIgnoreCase))
        {
            return '>';
        }

        return 'T';
    }

    private static Dictionary<string, int> GetStopColumns(IReadOnlyList<PTDStop> stops, int width)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (stops.Count == 0)
        {
            return columns;
        }

        if (stops.Count == 1)
        {
            columns[stops[0].Id] = width / 2;
            return columns;
        }

        for (var index = 0; index < stops.Count; index++)
        {
            var column = (int)Math.Round(index * (width - 1d) / (stops.Count - 1));
            columns[stops[index].Id] = Math.Clamp(column, 0, width - 1);
        }

        return columns;
    }

    private static int? GetTrainColumn(PTDTrainPosition train, IReadOnlyDictionary<string, int> stopColumns)
    {
        if (train.LastStopId is not null && train.NextStopId is not null
            && stopColumns.TryGetValue(train.LastStopId, out var lastColumn)
            && stopColumns.TryGetValue(train.NextStopId, out var nextColumn))
        {
            return (lastColumn + nextColumn) / 2;
        }

        if (train.LastStopId is not null && stopColumns.TryGetValue(train.LastStopId, out var stoppedColumn))
        {
            return stoppedColumn;
        }

        if (train.NextStopId is not null && stopColumns.TryGetValue(train.NextStopId, out var incomingColumn))
        {
            return incomingColumn;
        }

        return null;
    }

    private static char GetTrainMarker(PTDTrainPosition train, IReadOnlyDictionary<string, int> stopColumns)
    {
        if (train.LastStopId is not null && train.NextStopId is not null
            && stopColumns.TryGetValue(train.LastStopId, out var lastColumn)
            && stopColumns.TryGetValue(train.NextStopId, out var nextColumn))
        {
            return nextColumn >= lastColumn ? '>' : '<';
        }

        return '>';
    }

    private static string RenderStopIndex(IReadOnlyList<PTDStop> stops, int height)
    {
        var builder = new StringBuilder();
        var maxStops = Math.Max(height - 8, 6);
        foreach (var stop in stops.Take(maxStops))
        {
            builder.AppendLine($"o {stop.Sequence,2}: {stop.Name} ({stop.Id})");
        }

        if (stops.Count > maxStops)
        {
            builder.AppendLine($"... {stops.Count - maxStops} more stops");
        }

        return builder.ToString();
    }

    private static string RenderTrainList(IReadOnlyList<PTDTrainPosition> trains)
    {
        var builder = new StringBuilder();
        foreach (var train in trains.Take(8))
        {
            builder.AppendLine($"T {train.TrainId}  {train.LastStopId} -> {train.NextStopId}");
        }

        if (trains.Count > 8)
        {
            builder.AppendLine($"... {trains.Count - 8} more trains");
        }

        return builder.ToString();
    }
}

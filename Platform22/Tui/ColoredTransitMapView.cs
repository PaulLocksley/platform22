namespace Platform22.Tui;

using Terminal.Gui;

public sealed class ColoredTransitMapView : View
{
    private string text = string.Empty;

    public ColoredTransitMapView()
    {
        CanFocus = false;
    }

    public Color LineColor { get; set; } = Color.BrightCyan;

    public new string Text
    {
        get => text;
        set
        {
            text = value;
            SetNeedsDisplay();
        }
    }

    public override void Redraw(Rect bounds)
    {
        var lines = text.Split(Environment.NewLine);
        var height = Bounds.Height;
        var width = Bounds.Width;

        for (var row = 0; row < height; row++)
        {
            Driver.Move(0, row);
            if (row >= lines.Length)
            {
                Driver.SetAttribute(Application.Driver.MakeAttribute(Color.Gray, Color.Black));
                Driver.AddStr(new string(' ', width));
                continue;
            }

            var line = lines[row];
            Driver.SetAttribute(GetAttribute(line));
            Driver.AddStr(line.Length > width ? line[..width] : line.PadRight(width));
        }
    }

    private Attribute GetAttribute(string line)
    {
        var foreground = line switch
        {
            _ when IsTrackRow(line) => LineColor,
            _ when IsTrainRow(line) => Color.BrightYellow,
            _ when line.StartsWith("Line:", StringComparison.OrdinalIgnoreCase) => Color.White,
            _ when line.StartsWith("Station:", StringComparison.OrdinalIgnoreCase) => Color.White,
            _ when line.StartsWith("Legend:", StringComparison.OrdinalIgnoreCase) => Color.BrightMagenta,
            _ => Color.Gray
        };

        return Application.Driver.MakeAttribute(foreground, Color.Black);
    }

    private static bool IsTrackRow(string line)
    {
        return line.Contains('-') && line.Contains('o');
    }

    private static bool IsTrainRow(string line)
    {
        return line.Contains('>') || line.Contains('<') || line.Contains('+') || line.StartsWith("T ", StringComparison.OrdinalIgnoreCase);
    }
}

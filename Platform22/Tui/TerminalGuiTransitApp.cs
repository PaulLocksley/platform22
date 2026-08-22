namespace Platform22.Tui;

using System.Reflection;
using System.Text;
using PaulsTransitData.Models;
using Terminal.Gui;

public sealed class TerminalGuiTransitApp
{
    private readonly IReadOnlyList<TransitProviderOption> providers;
    private readonly AsciiTransitMapRenderer renderer = new();
    private ITransitMapClient client;
    private IReadOnlyList<PTDLineSummary> lines = [];
    private IReadOnlyList<PTDStationSummary> stations = [];
    private string filter = string.Empty;
    private TransitTuiMode mode = TransitTuiMode.Lines;
    private int panX;
    private int panY;
    private int zoom = 76;
    private int selectedIndex;
    private string? selectedItemId;
    private bool filterEditing;
    private Label? header;
    private Label? status;
    private ColoredTransitMapView? mapView;
    private ListView? selectorView;
    private Window? window;
    private MenuBar? menuBar;
    private Timer? repaintTimer;
    private Timer? dataRefreshTimer;
    private bool updatingSelector;

    public TerminalGuiTransitApp(IReadOnlyList<TransitProviderOption> providers, TransitProviderOption initialProvider)
    {
        this.providers = providers;
        client = initialProvider.Client;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await LoadCurrentProviderAsync(cancellationToken).ConfigureAwait(false);

        Application.Init();
        try
        {
            var top = Application.Top;
            menuBar = CreateMenuBar();
            window = new Window("Platform22 Transit Map")
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ColorScheme = DarkColorScheme(Color.White)
            };

            header = new Label(string.Empty)
            {
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                Height = 3,
                ColorScheme = DarkColorScheme(Color.BrightCyan)
            };
            mapView = new ColoredTransitMapView
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 40,
                Height = Dim.Fill() - 3,
                ColorScheme = DarkColorScheme(Color.BrightYellow)
            };
            selectorView = new ListView(Array.Empty<string>())
            {
                X = Pos.Right(mapView) + 1,
                Y = 3,
                Width = 36,
                Height = Dim.Fill() - 3,
                ColorScheme = DarkColorScheme(Color.White)
            };
            status = new Label(string.Empty)
            {
                X = 1,
                Y = Pos.Bottom(mapView),
                Width = Dim.Fill() - 2,
                Height = 1,
                ColorScheme = DarkColorScheme(Color.Gray)
            };

            selectorView.SelectedItemChanged += args =>
            {
                if (updatingSelector)
                {
                    return;
                }

                selectedIndex = args.Item;
                selectedItemId = null;
                panY = 0;
                Refresh();
            };

            window.Add(header, mapView, selectorView, status);
            window.KeyPress += args =>
            {
                HandleKey(args.KeyEvent);
                args.Handled = true;
            };
            top.KeyPress += args =>
            {
                HandleKey(args.KeyEvent);
                args.Handled = true;
            };
            top.Add(menuBar, window);
            window.SetFocus();
            Refresh();
            repaintTimer = new Timer(_ => InvokeRefresh(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            dataRefreshTimer = new Timer(_ => RefreshData(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            Application.Run();
        }
        finally
        {
            repaintTimer?.Dispose();
            dataRefreshTimer?.Dispose();
            Application.Shutdown();
        }
    }

    private void HandleKey(KeyEvent keyEvent)
    {
        if (!filterEditing && keyEvent.KeyValue == 'L')
        {
            OpenMenu(1);
            Refresh();
            return;
        }

        if (!filterEditing && keyEvent.KeyValue == 'S')
        {
            OpenMenu(2);
            Refresh();
            return;
        }

        var key = NormalizeKey(keyEvent);

        if (filterEditing)
        {
            HandleFilterKey(keyEvent);
            Refresh();
            return;
        }

        if (key == '+')
        {
            zoom = Math.Min(180, zoom + 12);
            Refresh();
            return;
        }

        if (key == '-')
        {
            zoom = Math.Max(40, zoom - 12);
            Refresh();
            return;
        }

        switch (key)
        {
            case '/':
                filterEditing = true;
                Refresh();
                return;
            case 'h':
                panX = Math.Max(0, panX - 4);
                Refresh();
                return;
            case 'l':
                panX += 4;
                Refresh();
                return;
            case 'k':
                panY = Math.Max(0, panY - 1);
                Refresh();
                return;
            case 'j':
                panY += 1;
                Refresh();
                return;
            case 'r':
                RefreshData();
                return;
            case 'q':
                Application.RequestStop();
                return;
        }

        switch (keyEvent.Key)
        {
            case Key.Esc:
                Application.RequestStop();
                return;
            case Key.Tab:
                SwitchMode(mode == TransitTuiMode.Lines ? TransitTuiMode.Stations : TransitTuiMode.Lines);
                break;
            case Key.Backspace:
                break;
            case Key.CursorDown:
                selectedIndex++;
                selectedItemId = null;
                panY = 0;
                break;
            case Key.CursorUp:
                selectedIndex = Math.Max(0, selectedIndex - 1);
                selectedItemId = null;
                panY = 0;
                break;
            default:
                break;
        }

        Refresh();
    }

    private void HandleFilterKey(KeyEvent keyEvent)
    {
        if (keyEvent.Key is Key.Enter or Key.Esc)
        {
            filterEditing = false;
            return;
        }

        if (keyEvent.Key == Key.Backspace)
        {
            if (filter.Length > 0)
            {
                filter = filter[..^1];
                selectedIndex = 0;
            }

            return;
        }

        var character = NormalizeKey(keyEvent);
        if (!char.IsControl(character))
        {
            filter += character;
            selectedIndex = 0;
            panX = 0;
            panY = 0;
        }
    }

    private void Refresh()
    {
        if (header is null || mapView is null || status is null || selectorView is null)
        {
            return;
        }

        var items = GetItems();
        if (items.Count == 0)
        {
            header.Text = $"{client.Name} | {mode} | filter: {filter} | no matches";
            mapView.Text = string.Empty;
            UpdateSelector([]);
            status.Text = "Type to filter, Tab switch, q quit";
            return;
        }

        if (selectedItemId is not null)
        {
            var selectedItemIndex = FindItemIndex(items, selectedItemId);
            if (selectedItemIndex >= 0)
            {
                selectedIndex = selectedItemIndex;
            }
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, items.Count - 1);
        selectedItemId = items[selectedIndex].Id;
        var rendered = mode == TransitTuiMode.Lines
            ? RenderSelectedLine(items[selectedIndex].Id)
            : RenderSelectedStation(items[selectedIndex].Id);

        var filterMode = filterEditing ? "FILTER" : "MAP";
        header.Text = $"{client.Name} | {mode} | {filterMode} | filter: {filter} | selected: {items[selectedIndex].Name} | {selectedIndex + 1}/{items.Count}";
        mapView.Text = ApplyViewport(rendered);
        mapView.LineColor = mode == TransitTuiMode.Lines ? GetLineColor(items[selectedIndex].Id) : Color.BrightGreen;
        UpdateSelector(items);
        status.Text = GetTooltip(items[selectedIndex]);
    }

    private void UpdateSelector(IReadOnlyList<MapItem> items)
    {
        if (selectorView is null)
        {
            return;
        }

        updatingSelector = true;
        try
        {
            selectorView.SetSource(items.Select((item, index) => $"{index + 1,3} {item.Name}").ToArray());
            selectorView.SelectedItem = items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, items.Count - 1);
            selectorView.EnsureSelectedItemVisible();
        }
        finally
        {
            updatingSelector = false;
        }
    }

    private IReadOnlyList<MapItem> GetItems()
    {
        if (mode == TransitTuiMode.Lines)
        {
            return TransitFilter.FilterLines(lines, filter)
                .Select(line => new MapItem(line.Id, line.Name, line.Id))
                .ToArray();
        }

        return TransitFilter.FilterStations(stations, filter)
            .Select(station => new MapItem(station.Id, station.Name, $"{station.Id} lines:{string.Join(',', station.LineIds)}"))
            .ToArray();
    }

    private string RenderSelectedLine(string lineId)
    {
        var snapshot = client.GetLineSnapshotAsync(lineId).GetAwaiter().GetResult();
        return renderer.RenderLine(snapshot, zoom, 28);
    }

    private Color GetLineColor(string lineId)
    {
        var line = lines.FirstOrDefault(line => string.Equals(line.Id, lineId, StringComparison.OrdinalIgnoreCase));
        return line?.Color?.ToUpperInvariant() switch
        {
            "#D13F3F" or "D13F3F" => Color.BrightRed,
            "#3F6FD1" or "3F6FD1" => Color.BrightBlue,
            "#3FA35C" or "3FA35C" => Color.BrightGreen,
            "#FFC425" or "FFC425" => Color.BrightYellow,
            _ => Color.BrightCyan
        };
    }

    private static char NormalizeKey(KeyEvent keyEvent)
    {
        if (keyEvent.KeyValue != 0)
        {
            return char.ToLowerInvariant((char)keyEvent.KeyValue);
        }

        return keyEvent.Key switch
        {
            Key.H => 'h',
            Key.J => 'j',
            Key.K => 'k',
            Key.L => 'l',
            Key.R => 'r',
            Key.Q => 'q',
            Key.CharMask => '\0',
            _ => '\0'
        };
    }

    private static ColorScheme DarkColorScheme(Color foreground)
    {
        var driver = Application.Driver;
        return new ColorScheme
        {
            Normal = driver.MakeAttribute(foreground, Color.Black),
            Focus = driver.MakeAttribute(Color.White, Color.DarkGray),
            HotNormal = driver.MakeAttribute(Color.BrightMagenta, Color.Black),
            HotFocus = driver.MakeAttribute(Color.BrightYellow, Color.DarkGray)
        };
    }

    private string RenderSelectedStation(string stationId)
    {
        var snapshot = client.GetStationSnapshotAsync(stationId).GetAwaiter().GetResult();
        return renderer.RenderStation(snapshot, zoom, 24);
    }

    private void RefreshData()
    {
        _ = Task.Run(async () =>
        {
            await client.RefreshAsync().ConfigureAwait(false);
            lines = await client.GetLinesAsync().ConfigureAwait(false);
            stations = await client.GetStationsAsync().ConfigureAwait(false);
            Application.MainLoop?.Invoke(() =>
            {
                if (menuBar is not null)
                {
                    menuBar.Menus = CreateMenuBar().Menus;
                }
            });
            InvokeRefresh();
        });
    }

    private MenuBar CreateMenuBar()
    {
        return new MenuBar(
        [
            new MenuBarItem("_Providers", providers.Select(provider => new MenuItem(provider.Name, $"Switch to {provider.Name}", () => SwitchProvider(provider))).ToArray()),
            new MenuBarItem("_Line view", lines.Select(line => new MenuItem(line.Name, line.Id, () => SelectLine(line.Id))).ToArray()),
            new MenuBarItem("_Station view", stations.Select(station => new MenuItem(station.Name, station.Id, () => SelectStation(station.Id))).ToArray()),
                new MenuBarItem("_Actions",
            [
                new MenuItem("_Refresh", "Reload cached provider data", () => RefreshData()),
                new MenuItem("_Quit", "Exit Platform22", () => Application.RequestStop())
            ])
        ]);
    }

    private void SwitchProvider(TransitProviderOption provider)
    {
        client = provider.Client;
        mode = TransitTuiMode.Lines;
        selectedIndex = 0;
        selectedItemId = null;
        filter = string.Empty;
        panX = 0;
        panY = 0;
        _ = Task.Run(async () =>
        {
            await LoadCurrentProviderAsync().ConfigureAwait(false);
            InvokeRefresh();
        });
    }

    private async Task LoadCurrentProviderAsync(CancellationToken cancellationToken = default)
    {
        await client.RefreshAsync(cancellationToken).ConfigureAwait(false);
        lines = await client.GetLinesAsync(cancellationToken).ConfigureAwait(false);
        stations = await client.GetStationsAsync(cancellationToken).ConfigureAwait(false);
        Application.MainLoop?.Invoke(() =>
        {
            if (menuBar is not null)
            {
                menuBar.Menus = CreateMenuBar().Menus;
            }
        });
    }

    private static int FindItemIndex(IReadOnlyList<MapItem> items, string itemId)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (string.Equals(items[index].Id, itemId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void SelectLine(string lineId)
    {
        mode = TransitTuiMode.Lines;
        selectedItemId = lineId;
        filter = string.Empty;
        panX = 0;
        panY = 0;
        Refresh();
    }

    private void SwitchMode(TransitTuiMode nextMode)
    {
        mode = nextMode;
        selectedIndex = 0;
        selectedItemId = null;
        filter = string.Empty;
        panX = 0;
        panY = 0;
    }

    private void OpenMenu(int index)
    {
        menuBar?.SetFocus();
        if (menuBar is null)
        {
            return;
        }

        var openMenu = typeof(MenuBar).GetMethod("OpenMenu", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(int), typeof(int), typeof(MenuBarItem)]);
        openMenu?.Invoke(menuBar, [index, -1, null]);
    }

    private void SelectStation(string stationId)
    {
        mode = TransitTuiMode.Stations;
        selectedItemId = stationId;
        filter = string.Empty;
        panX = 0;
        panY = 0;
        Refresh();
    }

    private void InvokeRefresh()
    {
        Application.MainLoop?.Invoke(Refresh);
    }

    private string ApplyViewport(string text)
    {
        var builder = new StringBuilder();
        foreach (var line in text.Split(Environment.NewLine).Skip(panY))
        {
            builder.AppendLine(line.Length > panX ? line[panX..] : string.Empty);
        }

        return builder.ToString();
    }

    private static string GetTooltip(MapItem item)
    {
        return $"Right panel scrolls | L/S menus | / filter | hjkl pan | +/- zoom | Up/Down select | r refresh | Tab mode | q quit | tooltip: {item.Tooltip}";
    }

    private sealed record MapItem(string Id, string Name, string Tooltip);
}

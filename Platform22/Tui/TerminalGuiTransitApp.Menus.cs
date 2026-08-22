namespace Platform22.Tui;

using Terminal.Gui;

public sealed partial class TerminalGuiTransitApp
{
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

    private void OpenMenu(int index)
    {
        menuBar?.SetFocus();
        MenuBarOpener.OpenMenu(menuBar, index);
    }

    private void ShowProviderPicker()
    {
        ShowPicker("Providers", MenuBarOpener.GetMenuX(0), providers.Select(provider => provider.Name).ToArray(), index => SwitchProvider(providers[index]));
    }

    private void ShowLinePicker()
    {
        var items = TransitFilter.FilterLines(lines, filter).ToArray();
        ShowPicker("Line view", MenuBarOpener.GetMenuX(1), items.Select(line => line.Name).ToArray(), index => SelectLine(items[index].Id));
    }

    private void ShowStationPicker()
    {
        var items = TransitFilter.FilterStations(stations, filter).ToArray();
        ShowPicker("Station view", MenuBarOpener.GetMenuX(2), items.Select(station => station.Name).ToArray(), index => SelectStation(items[index].Id));
    }

    private void ShowPicker(string title, int x, IReadOnlyList<string> items, Action<int> select)
    {
        ClosePicker();
        if (items.Count == 0 || Application.Top is null)
        {
            return;
        }

        var width = Math.Clamp(items.Max(item => item.Length) + 4, 20, 54);
        var height = Math.Clamp(items.Count + 2, 3, Math.Max(3, Application.Top.Frame.Height - 2));
        var listView = new ListView(items.ToArray())
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        pickerWindow = new Window(title)
        {
            X = Math.Min(x, Math.Max(0, Application.Top.Frame.Width - width)),
            Y = 1,
            Width = width,
            Height = height,
            ColorScheme = DarkColorScheme(Color.White)
        };
        listView.OpenSelectedItem += args =>
        {
            ClosePicker();
            select(args.Item);
        };
        pickerWindow.KeyPress += args =>
        {
            if (args.KeyEvent.Key is Key.Esc)
            {
                ClosePicker();
                args.Handled = true;
            }
        };

        pickerWindow.Add(listView);
        Application.Top.Add(pickerWindow);
        listView.SetFocus();
    }

    private void ClosePicker()
    {
        if (pickerWindow is null || Application.Top is null)
        {
            return;
        }

        Application.Top.Remove(pickerWindow);
        pickerWindow = null;
        window?.SetFocus();
    }

    private void SwitchProvider(TransitProviderOption provider)
    {
        client = provider.Client;
        mode = TransitTuiMode.Lines;
        lines = [];
        stations = [];
        selectedIndex = 0;
        selectedItemId = null;
        filter = string.Empty;
        panX = 0;
        panY = 0;
        loadingProvider = true;
        loadError = null;
        ClearRenderedSnapshots();

        Refresh();
        _ = Task.Run(async () =>
        {
            try
            {
                await LoadCurrentProviderAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                loadError = exception.Message;
            }
            finally
            {
                loadingProvider = false;
                InvokeRefresh();
            }
        });
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

    private void SelectStation(string stationId)
    {
        mode = TransitTuiMode.Stations;
        selectedItemId = stationId;
        filter = string.Empty;
        panX = 0;
        panY = 0;
        Refresh();
    }
}

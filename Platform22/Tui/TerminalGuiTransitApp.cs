namespace Platform22.Tui;

using System.Text;
using PaulsTransitData.Models;
using Terminal.Gui;

public sealed partial class TerminalGuiTransitApp
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
    private bool loadingProvider;
    private string? loadError;
    private Label? header;
    private Label? status;
    private ColoredTransitMapView? mapView;
    private ListView? selectorView;
    private Window? window;
    private MenuBar? menuBar;
    private Window? pickerWindow;
    private Timer? repaintTimer;
    private Timer? dataRefreshTimer;
    private DateTimeOffset? lastUpdatedAt;
    private DateTimeOffset nextUpdateAt;
    private bool updatingSelector;
    private int refreshInProgress;
    private readonly object renderedSnapshotLock = new();
    private readonly Dictionary<string, string> renderedSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> loadingSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> snapshotRetryAfter = new(StringComparer.OrdinalIgnoreCase);

    public TerminalGuiTransitApp(IReadOnlyList<TransitProviderOption> providers, TransitProviderOption initialProvider)
    {
        this.providers = providers;
        client = initialProvider.Client;
    }

    // Application.Run blocks; run the loop on a worker so callers keep an awaitable task.
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            loadingProvider = true;
            Application.Init();
            try
            {
                BuildUi();
                ScheduleNextUpdate();
                RefreshData();
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
        }, cancellationToken);
    }

    private void BuildUi()
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
            panX = 0;
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
    }

    private void RefreshData()
    {
        if (Interlocked.Exchange(ref refreshInProgress, 1) == 1)
        {
            return;
        }

        loadingProvider = true;
        loadError = null;
        ClearRenderedSnapshots();
        InvokeRefresh();

        _ = Task.Run(async () =>
        {
            try
            {
                await LoadCurrentProviderAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                loadError = $"Refresh failed: {exception.Message}";
            }
            finally
            {
                loadingProvider = false;
                Interlocked.Exchange(ref refreshInProgress, 0);
                InvokeRefresh();
            }
        });
    }

    private async Task LoadCurrentProviderAsync(CancellationToken cancellationToken = default)
    {
        await client.RefreshAsync(cancellationToken).ConfigureAwait(false);
        lines = await client.GetLinesAsync(cancellationToken).ConfigureAwait(false);
        stations = await client.GetStationsAsync(cancellationToken).ConfigureAwait(false);
        lastUpdatedAt = DateTimeOffset.Now;
        ScheduleNextUpdate();
        Application.MainLoop?.Invoke(() =>
        {
            if (menuBar is not null)
            {
                menuBar.Menus = CreateMenuBar().Menus;
            }
        });
    }

    private void ScheduleNextUpdate()
    {
        nextUpdateAt = DateTimeOffset.Now.AddSeconds(30);
    }

    private void InvokeRefresh()
    {
        Application.MainLoop?.Invoke(Refresh);
    }

    private sealed record MapItem(string Id, string Name, string Tooltip);
}

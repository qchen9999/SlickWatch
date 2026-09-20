using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SlickWatch.Platform;
using System.Text.Json;

namespace SlickWatch.App;

public partial class App : Application
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Queue<(string Title, string Description, Action Open, bool Test)> _notifications = new();
    private SingleInstance? _instance;
    private TrayIcon? _tray;
    private FeedClient? _client;
    private Task? _watchTask;
    private Task? _refreshTask;
    private NotificationWindow? _popup;
    private bool _smoke;
    private IClassicDesktopStyleApplicationLifetime Desktop => (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
    public StateStore Store { get; private set; } = null!;
    public WatchService Watcher { get; private set; } = null!;
    public MainWindow Dashboard { get; private set; } = null!;
    public bool Quitting { get; private set; }
    // Some Linux shells create a tray backend without actually displaying its icon.
    // Keep the dashboard in the task switcher on Linux so it always remains reachable.
    public bool CanHideToTray => !OperatingSystem.IsLinux() && _tray?.NativeMenuExporter is not null;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        Desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Desktop.ShutdownRequested += (_, e) => { if (!Quitting) { e.Cancel = true; _ = QuitAsync(); } };
        Desktop.Exit += (_, _) => { _shutdown.Cancel(); _tray?.Dispose(); _client?.Dispose(); _instance?.Dispose(); };
        Dispatcher.UIThread.Post(() => StartAsync(Desktop.Args ?? []));
        base.OnFrameworkInitializationCompleted();
    }
    private async void StartAsync(string[] args)
    {
        string? Argument(string name) => Array.IndexOf(args, name) is var index && index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        _smoke = args.Contains("--smoke-test");
        string directory = Argument("--data-dir") ?? (_smoke ? Path.Combine(Path.GetTempPath(), "SlickWatch-smoke-" + Guid.NewGuid().ToString("N")) : DesktopIntegration.DataDirectory);
        try
        {
            directory = Path.GetFullPath(directory);
            _instance = new SingleInstance(directory);
            if (!_instance.IsPrimary)
            {
                bool activated = await _instance.ActivateExistingAsync();
                if (!activated && !_smoke) await ShowMessageAsync("SlickWatch is already running. Open it from the tray or task switcher.");
                Desktop.Shutdown(activated ? 0 : 1); return;
            }
            Store = new StateStore(directory);
            var state = Store.Load();
            bool offline = _smoke && !args.Contains("--live");
            RemoteImage.Offline = offline;
            if (offline) SeedPreview(state);
            _client = new FeedClient();
            Watcher = new WatchService(state, Store, _client);
            Dashboard = new MainWindow();
            Desktop.MainWindow = Dashboard;
            _instance.Listen(() => Dispatcher.UIThread.Post(Dashboard.Reveal));
            CreateTray();
            Watcher.AlertsReady += ShowAlerts;
            if (!args.Contains("--tray") || !CanHideToTray || _smoke) Dashboard.Show();
            if (_smoke) { await SmokeAsync(Argument("--capture-dir") ?? directory, !offline); return; }
            _watchTask = RunWatcherAsync();
        }
        catch (Exception ex)
        {
            try { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, "startup-error.txt"), ex.ToString()); } catch (IOException) { }
            if (!_smoke) await ShowMessageAsync("SlickWatch could not start. " + ex.Message);
            Quitting = true; Desktop.Shutdown(1);
        }
    }
    private async Task RunWatcherAsync()
    {
        try { await Watcher.RunAsync(_shutdown.Token); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await ShowMessageAsync("Watching stopped after an unexpected error. Please restart SlickWatch. Your saved deals remain available.\n\n" + ex.Message);
        }
    }
    public Task RefreshAsync() => _refreshTask is { IsCompleted: false } ? _refreshTask : _refreshTask = RefreshCoreAsync();
    private async Task RefreshCoreAsync()
    {
        try { await Watcher.PollAsync(_shutdown.Token); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) { await ShowMessageAsync("Could not refresh: " + ex.Message); }
    }
    public async Task<bool> SaveAsync()
    {
        try { await Store.SaveAsync(Watcher.State); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { await ShowMessageAsync("Could not save: " + ex.Message); return false; }
    }
    private void CreateTray()
    {
        using var stream = AssetLoader.Open(new Uri("avares://SlickWatch/Assets/radar.ico"));
        var icon = new WindowIcon(stream);
        var menu = new NativeMenu();
        NativeMenuItem Item(string text, Action action)
        {
            var item = new NativeMenuItem(text); item.Click += (_, _) => action(); menu.Items.Add(item); return item;
        }
        Item("Open SlickWatch", Dashboard.Reveal);
        var refresh = Item("Check now", async () => await RefreshAsync());
        var pause = Item("Pause watching", Watcher.TogglePaused);
        Item("Preferences", async () => { Dashboard.Reveal(); await new SettingsWindow(this).ShowDialog(Dashboard); });
        Item("Test notification", ShowTestAlert);
        menu.Items.Add(new NativeMenuItemSeparator());
        Item("Exit", async () => await QuitAsync());
        _tray = new TrayIcon { Icon = icon, ToolTipText = "SlickWatch · Watching deals", Menu = menu, IsVisible = true };
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
        _tray.Clicked += (_, _) => Dashboard.Reveal();
        Dashboard.Icon = icon;
        Watcher.Changed += () =>
        {
            _tray.ToolTipText = Watcher.Paused ? "SlickWatch · Paused" : Watcher.Busy ? "SlickWatch · Checking deals" : "SlickWatch · Watching deals";
            pause.Header = Watcher.Paused ? "Resume watching" : "Pause watching";
            refresh.IsEnabled = !Watcher.Busy && !Watcher.Paused && !(Watcher.BackoffUntil > DateTimeOffset.UtcNow);
        };
    }
    public async void OpenUrl(string url)
    {
        if (!FeedParser.IsDealUrl(url)) return;
        try { if (!await DesktopIntegration.OpenUrlAsync(url)) await ShowMessageAsync("Your browser could not open the deal. You can copy its address from Read description."); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        { await ShowMessageAsync("Could not open your browser: " + ex.Message); }
    }
    public async Task ShowMessageAsync(string text)
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 18 };
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var window = new Window { Title = "SlickWatch", Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right }; ok.Click += (_, _) => window.Close(); panel.Children.Add(ok);
        if (Dashboard is not null) { Dashboard.Reveal(); await window.ShowDialog(Dashboard); }
        else { var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.SetResult(); window.Show(); await closed.Task; }
    }
    private void ShowAlerts(IReadOnlyList<DealAlert> alerts)
    {
        if (alerts.Count > 3) _notifications.Enqueue(($"{alerts.Count} new deals made the cut", $"{alerts[0].Title}\nOpen SlickWatch to see all your matches.", Dashboard.ShowMatches, false));
        else foreach (var alert in alerts) _notifications.Enqueue((alert.Title, alert.Reason, () => OpenUrl(alert.Url), false));
        ShowNextPopup();
    }
    public void ShowTestAlert()
    {
        _notifications.Enqueue(("A good find just crossed your radar", "This is a preview. Real alerts show the deal title and which threshold it passed.", () => { }, true));
        ShowNextPopup();
    }
    private void ShowNextPopup()
    {
        if (_popup is not null || _notifications.Count == 0 || Quitting) return;
        var item = _notifications.Dequeue();
        _popup = new NotificationWindow(item.Title, item.Description, item.Open, item.Test);
        _popup.Closed += (_, _) => { _popup = null; if (!Quitting) Dispatcher.UIThread.Post(ShowNextPopup); };
        _popup.Show();
        if (Watcher.State.Settings.Sound) DesktopIntegration.PlayAlertSound();
    }
    public async Task QuitAsync()
    {
        if (Quitting) return;
        Quitting = true; _shutdown.Cancel(); _popup?.Close();
        if (_watchTask is not null) await _watchTask;
        if (_refreshTask is not null) await _refreshTask;
        bool saved = Watcher is null || await SaveAsync();
        Desktop.Shutdown(saved ? 0 : 1);
    }
    private static void SeedPreview(WatchState state)
    {
        if (state.Deals.Count > 0) return;
        string[] titles = ["Wireless headphones with charging case — $49.99", "27-inch 4K monitor — $199.99", "Stainless steel cookware set — $89", "Portable SSD, 2TB — $119.99", "Adjustable standing desk — $149", "Coffee beans, 2 lb — $14.99"];
        for (int i = 0; i < 72; i++) state.Deals.Add(new Deal { Id = (20000000 + i).ToString(), Title = titles[i % titles.Length], Description = "Preview deal for checking the interface. Includes a community discussion and retailer details.", Url = $"https://slickdeals.net/f/{20000000 + i}", Score = 80 - i, Comments = i * 3, Sources = [i % 2 == 0 ? "Frontpage" : "Popular"], PostedAt = DateTimeOffset.UtcNow.AddMinutes(-i * 8), FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow, OriginalPostDate = true, Saved = i == 3 });
    }
    private async Task SmokeAsync(string captures, bool live)
    {
        Directory.CreateDirectory(captures);
        if (live) await Watcher.PollAsync(_shutdown.Token);
        else await SaveAsync();
        await Task.Delay(live ? 2500 : 600);
        Dashboard.UpdateView(); Capture(Dashboard, Path.Combine(captures, "dashboard.png"));
        bool filters = await Dashboard.CheckFiltersAsync();
        var settings = new SettingsWindow(this); settings.Show(Dashboard); await Task.Delay(300);
        Capture(settings, Path.Combine(captures, "preferences.png")); settings.Close();
        ShowTestAlert(); await Task.Delay(400);
        bool popup = _popup?.IsVisible == true;
        if (_popup is not null) Capture(_popup, Path.Combine(captures, "notification.png"));
        Dashboard.Close();
        await Task.Delay(150);
        // A virtual X display without a window manager may ignore minimization. The
        // fallback must remain present in the task switcher even in that environment.
        bool keepsRunning = !Quitting && (CanHideToTray ? !Dashboard.IsVisible : Dashboard.IsVisible && Dashboard.ShowInTaskbar);
        Dashboard.Reveal();
        var report = new { Platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription, Framework = "Avalonia", LiveFeed = live, Deals = Watcher.State.Deals.Count, Matches = Watcher.State.Deals.Count(d => AlertRules.Matches(d, Watcher.State.Settings)), FirstRunAlerts = Watcher.State.Alerts.Count, TrayCreated = _tray?.IsVisible, Filters = filters, Popup = popup, CloseKeepsRunning = keepsRunning, Watcher.Error, SavedState = File.Exists(Path.Combine(Store.DirectoryPath, "state.json")) };
        await File.WriteAllTextAsync(Path.Combine(captures, "smoke-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (!filters || !popup || !keepsRunning) throw new InvalidOperationException("Desktop smoke checks failed.");
        await QuitAsync();
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        using var target = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        target.Render(window); target.Save(path, PngBitmapEncoderOptions.Default);
    }
}

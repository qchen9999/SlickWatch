using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SlickWatch.App;

public partial class App : Application
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Queue<(string Title, string Description, Action Open, bool Test)> _notifications = new();
    private Mutex? _instance;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;
    private Forms.NotifyIcon? _tray;
    private Icon? _icon;
    private FeedClient? _client;
    private Task? _watchTask;
    private NotificationWindow? _popup;
    private bool _smoke;
    public StateStore Store { get; private set; } = null!;
    public WatchService Watcher { get; private set; } = null!;
    public MainWindow Dashboard { get; private set; } = null!;
    public bool Quitting { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? Argument(string name) => Array.IndexOf(e.Args, name) is var index && index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null;
        _smoke = e.Args.Contains("--smoke-test");
        string directory = Argument("--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SlickWatch");
        directory = Path.GetFullPath(directory);
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..16];
        _instance = new Mutex(true, @"Local\SlickWatch-" + identity, out _ownsMutex);
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SlickWatch-Show-" + identity);
        if (!_ownsMutex) { _activate.Set(); Shutdown(); return; }
        try
        {
            Store = new StateStore(directory);
            var state = Store.Load();
            _client = new FeedClient();
            Watcher = new WatchService(state, Store, _client);
            Dashboard = new MainWindow(this);
            MainWindow = Dashboard;
            _activationWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => Dispatcher.BeginInvoke(() => Dashboard.Reveal()), null, Timeout.Infinite, false);
            CreateTray();
            Watcher.AlertsReady += ShowAlerts;
            if (!e.Args.Contains("--tray")) Dashboard.Show();
            if (_smoke)
            {
                await SmokeAsync(Argument("--capture-dir") ?? directory);
                return;
            }
            _watchTask = RunWatcherAsync();
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "startup-error.txt"), ex.ToString());
            if (!_smoke) MessageBox.Show("SlickWatch could not start. " + ex.Message, "SlickWatch", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private async Task RunWatcherAsync()
    {
        try { await Watcher.RunAsync(_shutdown.Token); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(Store.DirectoryPath, "error.log"), ex.ToString());
            MessageBox.Show("Watching stopped after an unexpected error. Please restart SlickWatch. Your saved deals are still available.\n\n" + ex.Message, "SlickWatch");
        }
    }
    public async Task RefreshAsync()
    {
        try { await Watcher.PollAsync(_shutdown.Token); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) { MessageBox.Show("Could not refresh: " + ex.Message, "SlickWatch"); }
    }
    public async Task<bool> SaveAsync()
    {
        try { await Store.SaveAsync(Watcher.State); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show("Could not save: " + ex.Message, "SlickWatch"); return false; }
    }
    private void CreateTray()
    {
        _icon = MakeIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open SlickWatch", null, (_, _) => Dashboard.Reveal());
        var refresh = menu.Items.Add("Check now", null, async (_, _) => await RefreshAsync());
        var pause = menu.Items.Add("Pause watching", null, (_, _) => Watcher.TogglePaused());
        menu.Items.Add("Preferences", null, (_, _) => { Dashboard.Reveal(); new SettingsWindow(this) { Owner = Dashboard }.ShowDialog(); });
        menu.Items.Add("Test notification", null, (_, _) => ShowTestAlert());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await QuitAsync());
        menu.Opening += (_, _) =>
        {
            pause.Text = Watcher.Paused ? "Resume watching" : "Pause watching";
            refresh.Enabled = !Watcher.Busy && !Watcher.Paused && !(Watcher.BackoffUntil > DateTimeOffset.UtcNow);
        };
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "SlickWatch · Watching deals", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) Dashboard.Reveal(); };
        Watcher.Changed += () => { if (_tray is not null) _tray.Text = Watcher.Paused ? "SlickWatch · Paused" : Watcher.Busy ? "SlickWatch · Checking deals" : "SlickWatch · Watching deals"; };
        using var iconStream = new MemoryStream();
        _icon.Save(iconStream); iconStream.Position = 0;
        Dashboard.Icon = BitmapFrame.Create(iconStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }
    private static Icon MakeIcon()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);
        using var fill = new SolidBrush(System.Drawing.Color.FromArgb(33, 104, 81));
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 243, 186), 4);
        graphics.FillEllipse(fill, 1, 1, 62, 62);
        graphics.DrawEllipse(pen, 13, 13, 38, 38);
        graphics.DrawLine(pen, 32, 32, 47, 16);
        using var dot = new SolidBrush(System.Drawing.Color.White);
        graphics.FillEllipse(dot, 25, 25, 14, 14);
        IntPtr handle = bitmap.GetHicon();
        try { using var original = Icon.FromHandle(handle); return (Icon)original.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    public void OpenUrl(string url)
    {
        if (!FeedParser.IsDealUrl(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show("Could not open your browser: " + ex.Message, "SlickWatch"); }
    }
    private void ShowAlerts(IReadOnlyList<DealAlert> alerts)
    {
        if (alerts.Count > 3)
            _notifications.Enqueue(($"{alerts.Count} new deals made the cut", $"{alerts[0].Title}\nOpen SlickWatch to see all your matches.", () => { Dashboard.ShowMatches(); }, false));
        else foreach (var alert in alerts)
            _notifications.Enqueue((alert.Title, alert.Reason, () => OpenUrl(alert.Url), false));
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
        _popup.Closed += (_, _) => { _popup = null; if (!Quitting) Dispatcher.BeginInvoke(ShowNextPopup); };
        _popup.Show();
        if (Watcher.State.Settings.Sound) System.Media.SystemSounds.Asterisk.Play();
    }
    public async Task QuitAsync()
    {
        if (Quitting) return;
        Quitting = true;
        _shutdown.Cancel();
        _popup?.Close();
        if (_watchTask is not null) await _watchTask;
        await SaveAsync();
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        _tray?.Dispose(); _icon?.Dispose(); _client?.Dispose();
        _activationWait?.Unregister(null); _activate?.Dispose();
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private async Task SmokeAsync(string captures)
    {
        Directory.CreateDirectory(captures);
        await Watcher.PollAsync(_shutdown.Token);
        await Task.Delay(2500);
        Dashboard.UpdateLayout();
        Capture(Dashboard, Path.Combine(captures, "dashboard.png"));
        var settings = new SettingsWindow(this) { Owner = Dashboard };
        settings.Show(); await Task.Delay(400);
        Capture(settings, Path.Combine(captures, "preferences.png")); settings.Close();
        ShowTestAlert(); await Task.Delay(500);
        if (_popup is not null) Capture(_popup, Path.Combine(captures, "notification.png"));
        bool trayVisible = _tray?.Visible == true;
        Dashboard.Close(); // Closing must hide the window without exiting the process.
        bool closeToTray = !Dashboard.IsVisible && !Quitting;
        Dashboard.Reveal();
        var report = new
        {
            Deals = Watcher.State.Deals.Count,
            KnownComments = Watcher.State.Deals.Count(d => d.Comments.HasValue),
            OriginalDates = Watcher.State.Deals.Count(d => d.OriginalPostDate),
            Matches = Watcher.State.Deals.Count(d => AlertRules.Matches(d, Watcher.State.Settings)),
            FirstRunAlerts = Watcher.State.Alerts.Count,
            TrayVisible = trayVisible,
            CloseToTray = closeToTray,
            Status = Watcher.Status,
            Error = Watcher.Error,
            SavedState = File.Exists(Path.Combine(Store.DirectoryPath, "state.json"))
        };
        await File.WriteAllTextAsync(Path.Combine(captures, "smoke-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        await QuitAsync();
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var target = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(path); png.Save(file);
    }
}

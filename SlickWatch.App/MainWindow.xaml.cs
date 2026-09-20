using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace SlickWatch.App;

public partial class MainWindow : Window
{
    private readonly App _app;
    private string _page = "all";
    private bool _ready;
    private int _visibleLimit = 60;
    private readonly ObservableCollection<Deal> _displayedDeals = [];
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };
    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(nameof(CardWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(290.0));
    public double CardWidth { get => (double)GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }
    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        DealItems.ItemsSource = _displayedDeals;
        _ready = true;
        _app.Watcher.Changed += UpdateView;
        _clock.Tick += (_, _) => UpdateStatus();
        _clock.Start();
        UpdateView();
    }
    public void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }
    public void ShowMatches() { _page = "matches"; SearchBox.Clear(); SourceFilter.SelectedIndex = 0; UpdateView(); Reveal(); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_app.Quitting) return;
        e.Cancel = true;
        Hide();
    }
    private void Window_StateChanged(object? sender, EventArgs e) { if (WindowState == WindowState.Minimized) Hide(); }
    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        _page = (string)((Button)sender).Tag;
        _visibleLimit = 60;
        UpdateView();
        DealsScroll.ScrollToTop();
    }
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (_ready) { _visibleLimit = 60; UpdateView(); } }
    private void LoadMore_Click(object sender, RoutedEventArgs e) { _visibleLimit += 60; UpdateView(); }
    private void DealsScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = Math.Max(300, e.NewSize.Width - 20);
        int columns = Math.Max(1, (int)(width / 282));
        CardWidth = Math.Floor(width / columns) - 14;
    }
    public void UpdateView()
    {
        if (!_ready) return;
        var state = _app.Watcher.State;
        var settings = state.Settings;
        DealsCount.Text = state.Deals.Count.ToString("N0");
        MatchCount.Text = state.Deals.Count(d => AlertRules.Matches(d, settings)).ToString("N0");
        RuleSummary.Text = $">{settings.ThumbThreshold} thumbs OR >{settings.CommentThreshold} comments";
        SourceSummary.Text = string.Join(" + ", new[] { settings.Frontpage ? "Frontpage" : null, settings.Popular ? "Popular" : null }.Where(x => x is not null));
        foreach (var button in new[] { AllNav, MatchesNav, SavedNav, AlertsNav })
            button.Background = (string)button.Tag == _page ? new SolidColorBrush(Color.FromRgb(51, 81, 65)) : Brushes.Transparent;
        PageTitle.Text = _page switch { "matches" => "Worth a closer look", "saved" => "Your saved finds", "alerts" => "Your alert history", _ => "Your deal radar" };
        PageSubtitle.Text = _page switch
        {
            "matches" => $"Deals above {settings.ThumbThreshold} thumbs or {settings.CommentThreshold} comments, using their latest known counts.",
            "saved" => "A shortlist of the deals you want to come back to.",
            "alerts" => "New qualifying deals, remembered across app restarts.",
            _ => "Fresh finds from Slickdeals, with the signal turned up."
        };
        string search = SearchBox.Text.Trim();
        bool history = _page == "alerts";
        DealsScroll.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        AlertsScroll.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        SourceFilter.IsEnabled = SortBox.IsEnabled = !history;
        int count;
        if (history)
        {
            var alerts = state.Alerts.Where(a => a.Title.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            AlertItems.ItemsSource = alerts;
            count = alerts.Count;
        }
        else
        {
            IEnumerable<Deal> deals = state.Deals;
            if (_page == "matches") deals = deals.Where(d => AlertRules.Matches(d, settings));
            if (_page == "saved") deals = deals.Where(d => d.Saved);
            string? source = SourceFilter.SelectedIndex switch { 1 => "Frontpage", 2 => "Popular", _ => null };
            if (source is not null) deals = deals.Where(d => d.Sources.Contains(source));
            if (search.Length > 0) deals = deals.Where(d => $"{d.Title} {d.Description} {d.Category}".Contains(search, StringComparison.OrdinalIgnoreCase));
            deals = SortBox.SelectedIndex switch
            {
                1 => deals.OrderBy(d => d.Expired).ThenByDescending(d => d.Comments ?? -1).ThenByDescending(d => d.Score),
                2 => deals.OrderByDescending(d => d.PostedAt),
                3 => deals.OrderByDescending(d => d.FirstSeenAt),
                _ => deals.OrderBy(d => d.Expired).ThenByDescending(d => d.Score ?? int.MinValue).ThenByDescending(d => d.Comments)
            };
            var list = deals.ToList();
            for (int index = 0; index < list.Count; index++) list[index].DisplayRank = index + 1;
            count = list.Count;
            var visible = list.Take(_visibleLimit).ToList();
            // Keep existing card containers and image bindings alive during background updates.
            for (int index = _displayedDeals.Count - 1; index >= 0; index--)
                if (!visible.Contains(_displayedDeals[index])) _displayedDeals.RemoveAt(index);
            for (int index = 0; index < visible.Count; index++)
            {
                int current = _displayedDeals.IndexOf(visible[index]);
                if (current < 0) _displayedDeals.Insert(index, visible[index]);
                else if (current != index) _displayedDeals.Move(current, index);
                visible[index].NotifyUpdated();
            }
            LoadMoreButton.Visibility = count > _visibleLimit ? Visibility.Visible : Visibility.Collapsed;
        }
        ResultsLabel.Text = $"{count:N0} {(history ? "alerts" : "deals")}  ·  {(_page == "matches" ? "Your thresholds" : history ? "Newest first" : ((ComboBoxItem)SortBox.SelectedItem).Content)}";
        if (!history && count > _visibleLimit) ResultsLabel.Text += $"  ·  showing {_visibleLimit}";
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        (EmptyTitle.Text, EmptyDescription.Text) = _page switch
        {
            "saved" => ("Keep the good ones close", "Click ☆ Save on a deal to add it to your shortlist."),
            "matches" => ("Nothing above your thresholds yet", "Your radar will let you know when a deal qualifies."),
            "alerts" => ("A quiet start is a good start", "The first scan is silent. New qualifying deals will appear here; use Test alert to preview a popup."),
            _ when search.Length > 0 => ("No deals found", "Try a different search or feed filter."),
            _ => ("Tuning in to your next find", "Your feeds will appear here after the first check. Cached deals remain available offline.")
        };
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        var watcher = _app.Watcher;
        RefreshButton.IsEnabled = !watcher.Busy && !watcher.Paused && !(watcher.BackoffUntil > DateTimeOffset.UtcNow);
        RefreshButton.Content = watcher.Busy ? "Checking…" : "↻  Refresh";
        PauseButton.Content = watcher.Paused ? "Resume watching" : "Pause watching";
        WatchStatus.Text = watcher.Paused ? "Ⅱ  Paused" : watcher.Busy ? "◌  Checking" : watcher.Error is not null ? "◷  Retrying" : "●  Watching";
        NextCheck.Text = watcher.Paused ? "Resume whenever you're ready" : watcher.NextPollAt is { } next ? $"Next check {next.ToLocalTime():h:mm tt}" : "First scan loads quietly";
        StatusText.Text = watcher.Status + (watcher.State.LastPollAt is { } last ? $"  ·  Last feed check {last.ToLocalTime():h:mm tt}" : "");
        ErrorBanner.Visibility = watcher.Error is null ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Text = watcher.Error;
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _app.RefreshAsync();
    private void Pause_Click(object sender, RoutedEventArgs e) => _app.Watcher.TogglePaused();
    private void TestAlert_Click(object sender, RoutedEventArgs e) => _app.ShowTestAlert();
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(_app) { Owner = this }.ShowDialog();
    private void OpenDeal_Click(object sender, RoutedEventArgs e) => _app.OpenUrl(((Deal)((Button)sender).Tag).Url);
    private void OpenAlert_Click(object sender, RoutedEventArgs e) => _app.OpenUrl(((DealAlert)((Button)sender).Tag).Url);
    private async void SaveDeal_Click(object sender, RoutedEventArgs e)
    {
        var deal = (Deal)((Button)sender).Tag;
        deal.Saved = !deal.Saved;
        UpdateView();
        await _app.SaveAsync();
    }
    private void Details_Click(object sender, RoutedEventArgs e)
    {
        var deal = (Deal)((Button)sender).Tag;
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = deal.Title, FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"{deal.ScoreLabel} thumbs  ·  {deal.CommentsLabel} comments  ·  {deal.SourceLabel}", Margin = new Thickness(0, 15, 0, 8), Foreground = (Brush)FindResource("Accent") });
        panel.Children.Add(new TextBlock { Text = $"{deal.DateKind}: {deal.DateLabel}\nCategory: {deal.Category}\nPosted by: {deal.Author}\n{deal.MetricsTooltip}", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(new TextBox { Text = deal.Description, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent });
        var open = new Button { Content = "Open on Slickdeals ↗", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 20, 0, 10) };
        open.Click += (_, _) => _app.OpenUrl(deal.Url);
        panel.Children.Add(open);
        panel.Children.Add(new TextBox { Text = deal.Url, IsReadOnly = true, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        new Window { Title = "Deal details — SlickWatch", Owner = this, Width = 720, Height = 730, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.ShowDialog();
    }
    private void Image_Failed(object sender, ExceptionRoutedEventArgs e) => ((Image)sender).Visibility = Visibility.Collapsed;
}

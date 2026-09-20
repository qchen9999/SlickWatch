using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Controls.Platform;
using Avalonia.Threading;

namespace SlickWatch.Android;

public sealed class MainView : UserControl
{
    private static readonly IBrush Ink = Brush.Parse("#243C2F");
    private static readonly IBrush Green = Brush.Parse("#216851");
    private static readonly IBrush Muted = Brush.Parse("#6C7C71");
    private readonly Grid shell = new() { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), Margin = new(16, 8) };
    private readonly TextBlock status = Text("Loading your deals…", 12);
    private readonly TextBlock count = Text("YOUR DEAL RADAR", 11);
    private readonly TextBox search = new() { PlaceholderText = "Search deals", Margin = new(0, 8), MinHeight = 46 };
    private readonly ComboBox section = new() { ItemsSource = new[] { "All deals", "Matches", "Saved", "Alerts" }, SelectedIndex = 0, MinWidth = 126, MinHeight = 44 };
    private readonly ComboBox sort = new() { ItemsSource = new[] { "Newest", "Top score", "Most comments" }, SelectedIndex = 0, MinWidth = 142, MinHeight = 44 };
    private readonly ContentControl body = new();
    private readonly StackPanel filters = new();
    private readonly Button settings;
    private readonly ListBox deals = new() { Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(0) };
    private readonly Button refresh;
    private readonly Button pause;
    private WatchState state = new();
    private bool settingsOpen;
    private bool active;
    private long revision;
    private IInsetsManager? insets;
    private TextBlock? notificationStatus;

    public MainView()
    {
        TopLevel.SetAutoSafeAreaPadding(this, false);
        Background = Brush.Parse("#F5F7F2"); Foreground = Ink;
        FontFamily = new("avares://Avalonia.Fonts.Inter/Assets#Inter"); FontSize = 14;
        var heading = new Grid { ColumnDefinitions = new("*,Auto") };
        var brand = new StackPanel { Spacing = 3 };
        brand.Children.Add(Text("SlickWatch", 28, true)); brand.Children.Add(count);
        heading.Children.Add(brand);
        settings = Button("Settings", () => { settingsOpen = !settingsOpen; Render(); });
        Grid.SetColumn(settings, 1); heading.Children.Add(settings); shell.Children.Add(heading);

        filters.Children.Add(search);
        var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 0, 0, 6) };
        row.Children.Add(section); Grid.SetColumn(sort, 1); row.Children.Add(sort); filters.Children.Add(row);
        Grid.SetRow(filters, 1); shell.Children.Add(filters);
        status.Foreground = Muted; status.Margin = new(0, 5, 0, 10); status.MaxLines = 4;
        Grid.SetRow(status, 2); shell.Children.Add(status);
        Grid.SetRow(body, 3); shell.Children.Add(body);
        var footer = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 8, 0, 0) };
        refresh = AsyncButton("Refresh deals", async () => { await Task.Run(() => PhoneServices.RefreshAsync(force: true)); });
        refresh.Background = Green; refresh.Foreground = Brushes.White; footer.Children.Add(refresh);
        pause = AsyncButton("Pause", async () => await PhoneServices.UpdateAsync(s => { s.Mobile.Paused = !s.Mobile.Paused; s.Mobile.NextCheckAt = null; }));
        Grid.SetColumn(pause, 1); pause.Margin = new(8, 0, 0, 0); footer.Children.Add(pause);
        Grid.SetRow(footer, 4); shell.Children.Add(footer);
        Content = shell;
        deals.ItemTemplate = new FuncDataTemplate<Deal>((deal, _) => deal is null ? new Border() : Card(deal));
        search.TextChanged += (_, _) => RenderDeals();
        section.SelectionChanged += (_, _) => { settingsOpen = false; Render(); };
        sort.SelectionChanged += (_, _) => RenderDeals();
        AttachedToVisualTree += (_, _) =>
        {
            active = true; PhoneServices.Changed += Reload;
            insets = TopLevel.GetTopLevel(this)?.InsetsManager;
            if (insets is not null) { insets.SafeAreaChanged += InsetsChanged; ApplyInsets(); }
            Reload();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            active = false; revision++; PhoneServices.Changed -= Reload;
            if (insets is not null) insets.SafeAreaChanged -= InsetsChanged;
        };
    }

    private void InsetsChanged(object? sender, EventArgs e) => ApplyInsets();
    private void ApplyInsets()
    {
        var p = insets?.SafeAreaPadding ?? default;
        shell.Margin = new Thickness(16 + p.Left, 8 + p.Top, 16 + p.Right, 8 + p.Bottom);
    }
    private async void Reload()
    {
        if (!active) return;
        long current = ++revision;
        refresh.IsEnabled = !PhoneServices.Busy && !state.Mobile.Paused;
        if (PhoneServices.Busy) status.Text = "Checking feeds and deal details…";
        try
        {
            var loaded = await Task.Run(() => PhoneServices.Monitor.ReadAsync());
            if (!active || current != revision) return;
            state = loaded; Render(rebuildSettings: false);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("SlickWatch", ex.ToString());
            status.Text = "Could not load saved deals: " + ex.Message;
        }
    }
    private void Render(bool rebuildSettings = true)
    {
        filters.IsVisible = !settingsOpen;
        settings.Content = settingsOpen ? "Back" : "Settings";
        if (notificationStatus is not null) notificationStatus.Text = PhoneServices.NotificationsAllowed ? "Android notifications are allowed." : "Android notifications are currently blocked.";
        pause.Content = state.Mobile.Paused ? "Resume" : "Pause";
        refresh.IsEnabled = !PhoneServices.Busy && !state.Mobile.Paused;
        string checkedAt = state.LastPollAt is { } date ? $"Updated {date.ToLocalTime():MMM d, h:mm tt}" : "First refresh loads existing deals quietly.";
        status.Text = PhoneServices.Busy ? "Checking feeds and deal details…" : state.Mobile.Paused ? "Watching paused" : checkedAt;
        if (!PhoneServices.Busy)
        {
            if ((PhoneServices.Error ?? state.Mobile.LastError) is { } error) status.Text += "\n" + error;
            if (state.Mobile.RetryNotBefore > DateTimeOffset.UtcNow) status.Text += $"\nSlickdeals asked us to wait until {state.Mobile.RetryNotBefore.Value.ToLocalTime():t}.";
            else if (!PhoneServices.NotificationsAllowed && state.Settings.Notifications) status.Text += "\nAllow notifications in Settings to receive alerts.";
        }
        count.Text = $"{state.Deals.Count} DEALS · >{state.Settings.ThumbThreshold} THUMBS OR >{state.Settings.CommentThreshold} COMMENTS";
        if (settingsOpen) { if (rebuildSettings) body.Content = Settings(); return; }
        RenderDeals();
    }
    private void RenderDeals()
    {
        if (settingsOpen) return;
        if (section.SelectedIndex == 3)
        {
            var history = new ListBox { Background = Brushes.Transparent, BorderThickness = new(0) };
            history.ItemTemplate = new FuncDataTemplate<DealAlert>((alert, _) =>
            {
                if (alert is null) return new Border();
                var panel = new StackPanel { Spacing = 8, Margin = new(4, 6) };
                panel.Children.Add(Text(alert.Title, 16, true));
                panel.Children.Add(Text($"{alert.Reason}\n{alert.CreatedAt.ToLocalTime():g}", 12));
                panel.Children.Add(Button("Open deal", () => PhoneServices.OpenDeal(alert.Url)));
                return panel;
            });
            history.ItemsSource = state.Alerts;
            body.Content = state.Alerts.Count == 0 ? Empty("Your next find starts here", "New threshold crossings appear here. Existing deals load quietly on the first check.") : history;
            return;
        }
        IEnumerable<Deal> query = state.Deals;
        if (section.SelectedIndex == 1) query = query.Where(d => AlertRules.Matches(d, state.Settings));
        if (section.SelectedIndex == 2) query = query.Where(d => d.Saved);
        if (!string.IsNullOrWhiteSpace(search.Text)) query = query.Where(d => (d.Title + " " + d.Description).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        query = sort.SelectedIndex switch
        {
            1 => query.OrderByDescending(d => d.Score).ThenByDescending(d => d.PostedAt),
            2 => query.OrderByDescending(d => d.Comments).ThenByDescending(d => d.PostedAt),
            _ => query.OrderByDescending(d => d.PostedAt ?? d.FirstSeenAt)
        };
        var visible = query.ToList();
        for (int i = 0; i < visible.Count; i++) visible[i].DisplayRank = i + 1;
        deals.ItemsSource = visible;
        body.Content = visible.Count == 0 ? Empty("No deals here yet", state.Deals.Count == 0 ? "Refresh to collect Frontpage and Popular deals from Slickdeals." : "Try another view or search.") : deals;
    }
    private Control Card(Deal deal)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Text($"{deal.RankLabel}  ·  {deal.StatusLabel}", 11));
        if (deal.ImageUrl is not null)
            panel.Children.Add(new SlickWatch.App.RemoteImage { Url = deal.ImageUrl, Height = 140, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch });
        panel.Children.Add(Text(deal.Title, 17, true));
        var metrics = Text($"{deal.ScoreLabel} thumbs   ·   {deal.CommentsLabel} comments", 14, true); metrics.Foreground = Green;
        panel.Children.Add(metrics);
        panel.Children.Add(Text($"{deal.DateKind} {deal.DateLabel}", 12));
        if (!string.IsNullOrWhiteSpace(deal.Description))
        {
            var description = new Expander { Header = "Description", HorizontalAlignment = HorizontalAlignment.Stretch };
            description.Content = Text(deal.Description, 13); panel.Children.Add(description);
        }
        if (deal.Comments is null || deal.DetailError is not null)
            panel.Children.Add(Text("Some counts are unavailable; later checks will retry.", 11));
        var actions = new Grid { ColumnDefinitions = new("*,Auto") };
        var open = Button("Open deal", () => PhoneServices.OpenDeal(deal.Url)); open.Background = Green; open.Foreground = Brushes.White;
        actions.Children.Add(open);
        var save = AsyncButton(deal.Saved ? "Saved ★" : "Save ☆", async () =>
            await PhoneServices.UpdateAsync(s => { var found = s.Deals.FirstOrDefault(d => d.Id == deal.Id); if (found is not null) found.Saved = !found.Saved; }));
        save.Margin = new(8, 0, 0, 0); Grid.SetColumn(save, 1); actions.Children.Add(save); panel.Children.Add(actions);
        return new Border { Background = Brushes.White, CornerRadius = new(12), BorderBrush = Brush.Parse("#DEE6DC"), BorderThickness = new(1), Padding = new(14), Margin = new(0, 0, 0, 10), Child = panel };
    }
    private Control Settings()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new(2, 4, 2, 12) };
        panel.Children.Add(Text("Your watch rules", 22, true));
        panel.Children.Add(Text("A deal matches when either count is greater than your threshold.", 13));
        var thumbs = Number(state.Settings.ThumbThreshold, 0, 100000);
        var comments = Number(state.Settings.CommentThreshold, 0, 100000);
        var minutes = Number(state.Settings.PollMinutes, 5, 120);
        var pages = Number(state.Settings.DetailChecksPerPoll, 1, 25);
        AddField(panel, "Thumbs threshold", thumbs); AddField(panel, "Comments threshold", comments);
        AddField(panel, "Check every (minutes, while open)", minutes);
        AddField(panel, "Deal pages checked per refresh", pages);
        var front = Check("Frontpage deals", state.Settings.Frontpage);
        var popular = Check("Popular deals", state.Settings.Popular);
        var notifications = Check("Notify me about new matches", state.Settings.Notifications);
        var background = Check("Check in the background", state.Mobile.BackgroundChecks);
        panel.Children.Add(front); panel.Children.Add(popular); panel.Children.Add(notifications); panel.Children.Add(background);
        panel.Children.Add(Text("Background checks run at least 15 minutes apart. Android may delay them to save battery. Force-stopping the app stops checks until you open it again.", 12));
        notificationStatus = Text(PhoneServices.NotificationsAllowed ? "Android notifications are allowed." : "Android notifications are currently blocked.", 13, true);
        panel.Children.Add(notificationStatus);
        panel.Children.Add(Button("Notification permission", PhoneServices.EnableNotifications));
        panel.Children.Add(Button("Send test notification", () =>
        {
            if (!PhoneServices.NotificationsAllowed) { status.Text = "Allow notifications first, then send a test."; return; }
            PhoneServices.TestNotification(); status.Text = "Test sent. Check your notification shade.";
        }));
        panel.Children.Add(AsyncButton("Save settings", async () =>
        {
            var updated = new WatchSettings
            {
                ThumbThreshold = (int)(thumbs.Value ?? 30), CommentThreshold = (int)(comments.Value ?? 50),
                PollMinutes = (int)(minutes.Value ?? 5), DetailChecksPerPoll = (int)(pages.Value ?? 12),
                Frontpage = front.IsChecked == true, Popular = popular.IsChecked == true,
                Notifications = notifications.IsChecked == true
            };
            updated.Validate();
            await PhoneServices.UpdateAsync(s => { s.Settings = updated; s.Mobile.BackgroundChecks = background.IsChecked == true; s.Mobile.NextCheckAt = null; });
            settingsOpen = false; Reload();
        }));
        panel.Children.Add(Text("SlickWatch Android preview · 0.1.0\nSaved only on this device. No account or server required. Not affiliated with Slickdeals.", 11));
        return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
    private static NumericUpDown Number(int value, int min, int max) => new() { Value = value, Minimum = min, Maximum = max, Increment = 1, FormatString = "0", MinHeight = 46, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static void AddField(StackPanel panel, string label, Control input) { panel.Children.Add(Text(label, 13, true)); panel.Children.Add(input); }
    private static CheckBox Check(string label, bool value) => new() { Content = label, IsChecked = value, MinHeight = 44 };
    private static Control Empty(string title, string detail)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new(20, 40) };
        panel.Children.Add(Text(title, 22, true)); panel.Children.Add(Text(detail, 14)); return panel;
    }
    private Button AsyncButton(string label, Func<Task> action) => Button(label, async () =>
    {
        try { await action(); }
        catch (Exception ex) { status.Text = ex.Message; }
    });
    private static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, MinHeight = 46, Padding = new(12, 8), CornerRadius = new(8), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => action(); return button;
    }
    private static TextBlock Text(string value, double size, bool bold = false) => new()
    {
        Text = value, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap, Foreground = Ink
    };
}

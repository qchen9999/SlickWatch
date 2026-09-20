using Microsoft.Win32;
using System.Text.Json;

namespace SlickWatch.App;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(App app)
    {
        Title = "Preferences — SlickWatch";
        Width = 560; Height = 790; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(30) };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var footer = new StackPanel { Margin = new Thickness(30, 0, 30, 20) };
        Grid.SetRow(footer, 1); layout.Children.Add(footer);
        Content = layout;
        var settings = app.Watcher.State.Settings;
        panel.Children.Add(new TextBlock { Text = "Make it your radar", FontSize = 28, FontWeight = FontWeights.SemiBold });
        AddText("Alert when either count is strictly greater than its threshold. The feed reports net thumb score.");
        var thumbs = Field("Thumb score above", settings.ThumbThreshold);
        var comments = Field("Comments above", settings.CommentThreshold);
        var interval = Field("Check feeds every (minutes, 5–120)", settings.PollMinutes);
        var checks = Field("Deal pages per check (1–25)", settings.DetailChecksPerPoll);
        AddText("Page checks rotate through deals posted in the last 3 days, including those that leave RSS. Comment alerts may take several polling cycles. Older deals can still match using RSS scores.");
        var frontpage = Check("Watch Frontpage deals", settings.Frontpage);
        var popular = Check("Watch Popular deals", settings.Popular);
        var notifications = Check("Show sliding desktop alerts", settings.Notifications);
        var sound = Check("Play a sound with alerts", settings.Sound);
        var startup = Check("Start in the tray when I sign in to Windows", settings.StartWithWindows);
        AddText("First-time feeds load silently. Each qualifying deal alerts once, even after a restart. Muted alerts still appear in history. Closing the window keeps watching; Exit in the tray stops the app.");
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 10, 0, 10) };
        footer.Children.Add(message);
        var save = new Button { Content = "Save preferences", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(save);
        save.Click += async (_, _) =>
        {
            try
            {
                var next = new WatchSettings
                {
                    ThumbThreshold = Number(thumbs), CommentThreshold = Number(comments), PollMinutes = Number(interval), DetailChecksPerPoll = Number(checks),
                    Frontpage = frontpage.IsChecked == true, Popular = popular.IsChecked == true, Notifications = notifications.IsChecked == true,
                    Sound = sound.IsChecked == true, StartWithWindows = startup.IsChecked == true
                };
                next.Validate();
                if (next.StartWithWindows != settings.StartWithWindows) SetStartup(next.StartWithWindows);
                app.Watcher.State.Settings = next;
                if (await app.SaveAsync()) { app.Dashboard.UpdateView(); Close(); }
                else message.Text = "Preferences could not be saved. Check the data folder is writable.";
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or System.Security.SecurityException or UnauthorizedAccessException or IOException)
            { message.Text = ex.Message; }
        };
        var export = new Button { Content = "Export deals as JSON", Margin = new Thickness(0, 12, 0, 0) };
        export.Click += async (_, _) =>
        {
            var dialog = new SaveFileDialog { FileName = "SlickWatch-deals.json", Filter = "JSON file|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var json = JsonSerializer.Serialize(app.Watcher.State.Deals, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(dialog.FileName, json);
                message.Foreground = (Brush)FindResource("Accent"); message.Text = "Deals exported.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { message.Text = ex.Message; }
        };
        footer.Children.Add(export);
        AddText("Stored only on this computer: " + app.Store.DirectoryPath);

        void AddText(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 12, 0, 8) });
        TextBox Field(string label, int number)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 6), FontWeight = FontWeights.SemiBold, FontSize = 12 });
            var input = new TextBox { Text = number.ToString() }; panel.Children.Add(input); return input;
        }
        CheckBox Check(string label, bool value) { var box = new CheckBox { Content = label, IsChecked = value }; panel.Children.Add(box); return box; }
    }
    private static int Number(TextBox input) => int.TryParse(input.Text, out int n) ? n : throw new ArgumentException("Enter whole numbers for thresholds and intervals.");
    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new IOException("The app's location could not be found.");
            key.SetValue("SlickWatch", $"\"{executable}\" --tray");
        }
        else key.DeleteValue("SlickWatch", false);
    }
}

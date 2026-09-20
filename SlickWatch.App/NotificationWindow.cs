namespace SlickWatch.App;

public sealed class NotificationWindow : Window
{
    private readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromSeconds(12) };
    public NotificationWindow(string title, string description, Action open, bool test = false)
    {
        Width = 390; SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None; CanResize = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        // Fluent draws interaction colors on the button's template presenter.
        // Use an opaque dark palette so its light-theme colors cannot blend into the alert.
        Resources["ButtonBackgroundPointerOver"] = Brush.Parse("#426653");
        Resources["ButtonForegroundPointerOver"] = Brushes.White;
        Resources["ButtonBackgroundPressed"] = Brush.Parse("#294B3B");
        Resources["ButtonForegroundPressed"] = Brushes.White;
        Opacity = 0;
        Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) } };
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(20, 47, 39)), CornerRadius = new CornerRadius(14), Padding = new Thickness(22), BorderBrush = new SolidColorBrush(Color.FromRgb(96, 143, 114)), BorderThickness = new Thickness(1), Margin = new Thickness(5) };
        Content = border;
        var panel = new StackPanel(); border.Child = panel;
        var heading = new DockPanel();
        var close = new Button { Content = "×", Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(6, 0, 6, 0), FontSize = 20 };
        DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        heading.Children.Add(new TextBlock { Text = test ? "SLICKWATCH  ·  TEST ALERT" : "SLICKWATCH  ·  NEW MATCH", Foreground = new SolidColorBrush(Color.FromRgb(163, 207, 170)), FontSize = 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(heading);
        panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 13, 0, 9), MaxHeight = 95, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(184, 204, 186)), FontSize = 12 });
        var button = new Button { Content = test ? "Looks good" : "Take a look  ↗", Background = new SolidColorBrush(Color.FromRgb(211, 234, 177)), Foreground = new SolidColorBrush(Color.FromRgb(25, 64, 42)), BorderThickness = new Thickness(0), Margin = new Thickness(0, 17, 0, 0) };
        // Keep the action button light, with dark text, in every interactive state.
        button.Resources["ButtonBackgroundPointerOver"] = Brush.Parse("#E4F3CD");
        button.Resources["ButtonForegroundPointerOver"] = button.Foreground;
        button.Resources["ButtonBackgroundPressed"] = Brush.Parse("#B8D78F");
        button.Resources["ButtonForegroundPressed"] = button.Foreground;
        button.Click += (_, _) => { open(); Close(); }; close.Click += (_, _) => Close(); panel.Children.Add(button);
        Opened += (_, _) =>
        {
            var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea;
            if (area is { } bounds)
                Position = new PixelPoint(bounds.Right - (int)(Width * RenderScaling) - 18, bounds.Bottom - (int)(Bounds.Height * RenderScaling) - 18);
            _dismiss.Start();
            Opacity = 1;
        };
        PointerEntered += (_, _) => _dismiss.Stop();
        PointerExited += (_, _) => _dismiss.Start();
        _dismiss.Tick += (_, _) => Close();
        Closed += (_, _) => _dismiss.Stop();
    }
}

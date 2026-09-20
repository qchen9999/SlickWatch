using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SlickWatch.App;

public sealed class NotificationWindow : Window
{
    private readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromSeconds(12) };
    public NotificationWindow(string title, string description, Action open, bool test = false)
    {
        Width = 390; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(20, 47, 39)), CornerRadius = new CornerRadius(14), Padding = new Thickness(22), BorderBrush = new SolidColorBrush(Color.FromRgb(96, 143, 114)), BorderThickness = new Thickness(1), Margin = new Thickness(5) };
        Content = border;
        var panel = new StackPanel(); border.Child = panel;
        var heading = new DockPanel();
        var close = new Button { Content = "×", Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(6, 0, 6, 0), FontSize = 20 };
        DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        heading.Children.Add(new TextBlock { Text = test ? "SLICKWATCH  ·  TEST ALERT" : "SLICKWATCH  ·  NEW MATCH", Foreground = new SolidColorBrush(Color.FromRgb(163, 207, 170)), FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(heading);
        panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 13, 0, 9), MaxHeight = 95, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(184, 204, 186)), FontSize = 12 });
        var button = new Button { Content = test ? "Looks good" : "Take a look  ↗", Background = new SolidColorBrush(Color.FromRgb(211, 234, 177)), Foreground = new SolidColorBrush(Color.FromRgb(25, 64, 42)), BorderThickness = new Thickness(0), Margin = new Thickness(0, 17, 0, 0) };
        button.Click += (_, _) => { open(); Close(); }; close.Click += (_, _) => Close(); panel.Children.Add(button);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000000 | 0x00000080));
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 18;
            Top = area.Bottom - ActualHeight - 18;
            border.RenderTransform = new TranslateTransform();
            ((TranslateTransform)border.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            _dismiss.Start();
        };
        MouseEnter += (_, _) => _dismiss.Stop();
        MouseLeave += (_, _) => _dismiss.Start();
        _dismiss.Tick += (_, _) => Close();
        Closed += (_, _) => _dismiss.Stop();
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}

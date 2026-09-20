using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace SlickWatch.Android;

[Activity(Label = "SlickWatch", Theme = "@style/SlickWatchTheme", MainLauncher = true,
    Exported = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    private CancellationTokenSource? foreground;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OpenNotification(Intent);
    }
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        OpenNotification(intent);
    }
    private void OpenNotification(Intent? intent)
    {
        if (intent?.GetStringExtra("dealUrl") is { } url && FeedParser.IsDealUrl(url))
        {
            intent.RemoveExtra("dealUrl");
            PhoneServices.OpenDeal(url);
        }
    }
    protected override void OnResume()
    {
        base.OnResume();
        PhoneServices.Activity = this;
        foreground?.Cancel();
        foreground = new();
        _ = PhoneServices.RunForegroundAsync(foreground.Token);
    }
    protected override void OnPause()
    {
        foreground?.Cancel();
        foreground?.Dispose();
        foreground = null;
        if (PhoneServices.Activity == this) PhoneServices.Activity = null;
        base.OnPause();
    }
}

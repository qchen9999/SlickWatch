using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Work;
using Avalonia.Threading;
using Java.Util.Concurrent;

namespace SlickWatch.Android;

internal static class PhoneServices
{
    private const string WorkName = "slickwatch-deals";
    private const string Channel = "deals-v1";
    private static Context context = null!;
    public static MobileMonitor Monitor { get; private set; } = null!;
    public static MainActivity? Activity { get; set; }
    public static event Action? Changed;
    public static bool Busy => Volatile.Read(ref busy) > 0;
    private static int busy;
    public static string? Error { get; private set; }

    public static void Initialize(Context appContext)
    {
        context = appContext;
        var directory = Path.Combine(context.FilesDir!.AbsolutePath, "SlickWatch");
        var store = new StateStore(directory);
        if (!File.Exists(Path.Combine(directory, "state.json")))
            Task.Run(() => store.SaveAsync(new WatchState { Settings = new() { DetailChecksPerPoll = 4 } })).GetAwaiter().GetResult();
        Monitor = new(store, new FeedClient());
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel(Channel, "Deal alerts", NotificationImportance.Default)
        { Description = "New Slickdeals that cross your thumbs or comment threshold" });
    }

    public static async Task RunForegroundAsync(CancellationToken token)
    {
        try
        {
            await ConfigureScheduleAsync(await Task.Run(() => Monitor.ReadAsync(token), token));
            NotifyChanged();
            while (!token.IsCancellationRequested)
            {
                var state = await Task.Run(() => Monitor.ReadAsync(token), token);
                if (!state.Mobile.Paused && !(state.Mobile.NextCheckAt > DateTimeOffset.UtcNow) &&
                    !(state.Mobile.RetryNotBefore > DateTimeOffset.UtcNow))
                    await Task.Run(() => RefreshAsync(token: token), token);
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Error = "Could not refresh: " + ex.Message; NotifyChanged(); }
    }

    public static async Task RefreshAsync(bool force = false, bool background = false, CancellationToken token = default)
    {
        Interlocked.Increment(ref busy); NotifyChanged();
        try
        {
            Error = null;
            var result = await Monitor.PollAsync(force, background, token).ConfigureAwait(false);
            if (!token.IsCancellationRequested) ShowAlerts(result.Alerts);
        }
        finally { Interlocked.Decrement(ref busy); NotifyChanged(); }
    }

    public static async Task UpdateAsync(Action<WatchState> update)
    {
        var state = await Task.Run(() => Monitor.UpdateAsync(update));
        await ConfigureScheduleAsync(state);
        NotifyChanged();
    }

    private static async Task ConfigureScheduleAsync(WatchState state)
    {
        var manager = WorkManager.GetInstance(context);
        IOperation operation;
        if (state.Mobile.Paused || !state.Mobile.BackgroundChecks)
            operation = manager.CancelUniqueWork(WorkName);
        else
        {
            var constraints = new Constraints.Builder().SetRequiredNetworkType(NetworkType.Connected!).Build();
            var request = new PeriodicWorkRequest.Builder(typeof(DealWorker),
                MobileMonitor.BackgroundMinutes(state.Settings), TimeUnit.Minutes!)
                .SetConstraints(constraints).AddTag(WorkName).Build();
            operation = manager.EnqueueUniquePeriodicWork(WorkName, ExistingPeriodicWorkPolicy.Update!, request);
        }
        // Observe the scheduler's durable operation before reporting that settings are saved.
        await Task.Run(() => operation.Result!.Get());
    }

    public static bool NotificationsAllowed => NotificationManagerCompat.From(context)!.AreNotificationsEnabled();
    public static void EnableNotifications()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && Activity is { } activity &&
            activity.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            activity.RequestPermissions([Manifest.Permission.PostNotifications], 100);
        else
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionAppNotificationSettings);
            intent.PutExtra(global::Android.Provider.Settings.ExtraAppPackage, context.PackageName);
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
    }

    public static void TestNotification() => ShowNotification(101, "SlickWatch is ready", "New matching deals will appear here.", null);
    private static void ShowAlerts(IReadOnlyList<DealAlert> alerts)
    {
        if (alerts.Count == 0) return;
        var newest = alerts[0];
        ShowNotification(100, alerts.Count == 1 ? newest.Title : $"{alerts.Count} new matching deals",
            alerts.Count == 1 ? newest.Reason : string.Join("\n", alerts.Take(5).Select(a => a.Title)),
            alerts.Count == 1 ? newest.Url : null);
    }
    private static void ShowNotification(int id, string title, string body, string? url)
    {
        if (!NotificationsAllowed) return;
        var intent = new Intent(context, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        if (url is not null) intent.PutExtra("dealUrl", url);
        var pending = PendingIntent.GetActivity(context, id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var builder = new NotificationCompat.Builder(context, Channel);
        builder.SetSmallIcon(Resource.Drawable.ic_notification);
        builder.SetContentTitle(title);
        builder.SetContentText(body);
        builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(body));
        builder.SetContentIntent(pending);
        builder.SetAutoCancel(true);
        NotificationManagerCompat.From(context)!.Notify(id, builder.Build());
    }

    public static void OpenDeal(string url)
    {
        if (!FeedParser.IsDealUrl(url)) return;
        var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
        intent.AddFlags(ActivityFlags.NewTask);
        try { context.StartActivity(intent); }
        catch (ActivityNotFoundException) { Error = "Install a browser to open deal pages."; NotifyChanged(); }
    }
    private static void NotifyChanged() => Dispatcher.UIThread.Post(() => Changed?.Invoke());
}

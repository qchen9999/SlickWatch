using Android.Content;
using Android.Runtime;
using AndroidX.Work;

namespace SlickWatch.Android;

// WorkManager constructs this Java peer by name after the app process is recreated.
[Register("com/qchen9999/slickwatch/DealWorker")]
public sealed class DealWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    private readonly CancellationTokenSource stop = new(TimeSpan.FromMinutes(3));
    public override Result DoWork()
    {
        try
        {
            PhoneServices.RefreshAsync(background: true, token: stop.Token).GetAwaiter().GetResult();
            return Result.InvokeSuccess()!;
        }
        catch (OperationCanceledException) { return Result.InvokeRetry()!; }
        catch (IOException) { return Result.InvokeRetry()!; }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("SlickWatch", "Background check failed: " + ex.Message);
            return Result.InvokeRetry()!;
        }
        finally { stop.Dispose(); }
    }
    public override void OnStopped()
    {
        try { stop.Cancel(); } catch (ObjectDisposedException) { }
        base.OnStopped();
    }
}

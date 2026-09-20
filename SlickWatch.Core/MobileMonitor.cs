namespace SlickWatch.Core;

public sealed record MobilePoll(WatchState State, IReadOnlyList<DealAlert> Alerts);

// One instance per Android process, shared by every activity and WorkManager worker.
// All transactions load under the same gate; UI callers only receive detached snapshots.
public sealed class MobileMonitor(StateStore store, IFeedClient client)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public static int BackgroundMinutes(WatchSettings settings) => Math.Max(15, settings.PollMinutes);

    public async Task<WatchState> ReadAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return store.Load(); }
        finally { gate.Release(); }
    }

    public async Task<WatchState> UpdateAsync(Action<WatchState> update, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = store.Load();
            update(state);
            state.Settings.Validate();
            await store.SaveAsync(state, token).ConfigureAwait(false);
            return state;
        }
        finally { gate.Release(); }
    }

    public async Task<MobilePoll> PollAsync(bool force = false, bool background = false, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var state = store.Load();
            var now = DateTimeOffset.UtcNow;
            if (state.Mobile.Paused || (background && !state.Mobile.BackgroundChecks) ||
                state.Mobile.RetryNotBefore > now || (!force && state.Mobile.NextCheckAt > now))
                return new(state, []);
            var watcher = new WatchService(state, store, client);
            IReadOnlyList<DealAlert> alerts = [];
            watcher.AlertsReady += batch => alerts = batch;
            await watcher.PollAsync(token).ConfigureAwait(false);
            state.Mobile.NextCheckAt = token.IsCancellationRequested ? null : watcher.NextPollAt;
            state.Mobile.RetryNotBefore = watcher.BackoffUntil;
            state.Mobile.LastError = watcher.Error;
            // Also persist scheduling/backoff, including when the OS stops a worker midway.
            // A failed save must never release notifications to the caller.
            await store.SaveAsync(state).ConfigureAwait(false);
            return new(state, alerts);
        }
        finally { gate.Release(); }
    }
}

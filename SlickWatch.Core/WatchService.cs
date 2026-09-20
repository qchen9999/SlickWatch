namespace SlickWatch.Core;

public sealed class WatchService(WatchState state, StateStore store, IFeedClient client)
{
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    public WatchState State => state;
    public bool Busy { get; private set; }
    public bool Paused { get; private set; }
    public string Status { get; private set; } = "Ready to watch";
    public string? Error { get; private set; } = store.Warning;
    public DateTimeOffset? NextPollAt { get; private set; }
    public DateTimeOffset? BackoffUntil { get; private set; }
    public event Action? Changed;
    public event Action<IReadOnlyList<DealAlert>>? AlertsReady;

    public void TogglePaused()
    {
        Paused = !Paused;
        Status = Paused ? "Watching paused" : "Watching resumed";
        if (!Paused) NextPollAt = DateTimeOffset.UtcNow;
        Changed?.Invoke();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!Paused && (NextPollAt is null || DateTimeOffset.UtcNow >= NextPollAt))
                await PollAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        if (Paused || !await _pollGate.WaitAsync(0, cancellationToken)) return;
        var pendingAlerts = new List<DealAlert>();
        var baselineIds = new HashSet<string>();
        try
        {
            if (BackoffUntil > DateTimeOffset.UtcNow)
            {
                NextPollAt = BackoffUntil;
                Status = $"Waiting until {BackoffUntil.Value.ToLocalTime():t}";
                return;
            }
            Busy = true;
            Error = null;
            Status = "Checking RSS feeds…";
            Changed?.Invoke();
            var feeds = new List<(string Name, string Url)>();
            if (state.Settings.Frontpage) feeds.Add(("Frontpage", FeedClient.FrontpageUrl));
            if (state.Settings.Popular) feeds.Add(("Popular", FeedClient.PopularUrl));
            var errors = new List<string>();
            int successfulFeeds = 0;
            foreach (var feed in feeds)
            {
                if (Paused) break;
                try
                {
                    var response = await client.GetAsync(feed.Url, true, cancellationToken);
                    if (!response.NotModified)
                    {
                        var incoming = FeedParser.Parse(response.Content!, feed.Name, DateTimeOffset.UtcNow);
                        bool baseline = !state.InitializedFeeds.Contains(feed.Name);
                        foreach (var fresh in incoming)
                        {
                            var existing = state.Deals.FirstOrDefault(d => d.Id == fresh.Id);
                            if (existing is null)
                            {
                                existing = fresh;
                                existing.ScoreBaselinePending = baseline;
                                existing.CommentBaselinePending = baseline;
                                state.Deals.Add(existing);
                                if (baseline) baselineIds.Add(existing.Id);
                            }
                            else
                            {
                                existing.Title = fresh.Title;
                                existing.Description = fresh.Description;
                                existing.ImageUrl = fresh.ImageUrl ?? existing.ImageUrl;
                                existing.Sources.UnionWith(fresh.Sources);
                                existing.LastSeenAt = fresh.LastSeenAt;
                                if (!existing.OriginalPostDate) existing.PostedAt = fresh.PostedAt ?? existing.PostedAt;
                                if (fresh.Score.HasValue) { existing.Score = fresh.Score; existing.ScoreCheckedAt = fresh.ScoreCheckedAt; }
                            }
                            if (baselineIds.Contains(existing.Id)) existing.ScoreBaselinePending = true;
                            if (AlertRules.Observe(state, existing, fresh.Score.HasValue, false, DateTimeOffset.UtcNow) is { } alert)
                                pendingAlerts.Add(alert);
                        }
                        // An empty feed may be transient; do not prime a source until real entries arrive.
                        if (incoming.Count > 0) state.InitializedFeeds.Add(feed.Name);
                    }
                    successfulFeeds++;
                }
                catch (Exception ex) when (Recoverable(ex, cancellationToken))
                {
                    errors.Add($"{feed.Name}: {Friendly(ex)}");
                    if (ex is FeedRequestException { RetryAt: { } retry }) { BackoffUntil = retry; break; }
                }
            }
            Changed?.Invoke();
            int checkedPages = 0;
            // Rotate fairly, including recent deals that have dropped out of the finite RSS window.
            // Match-only UI filtering never changes which deals get checked.
            var now = DateTimeOffset.UtcNow;
            var candidates = state.Deals.Where(d => !d.Expired &&
                ((state.Settings.Frontpage && d.Sources.Contains("Frontpage")) || (state.Settings.Popular && d.Sources.Contains("Popular"))) &&
                (d.PostedAt ?? d.FirstSeenAt) > now.AddDays(-3) &&
                (d.DetailAttemptedAt is null || d.DetailAttemptedAt < now.AddMinutes(-state.Settings.PollMinutes)))
                .OrderBy(d => d.DetailAttemptedAt ?? DateTimeOffset.MinValue).ThenByDescending(d => d.FirstSeenAt)
                .Take(state.Settings.DetailChecksPerPoll).ToList();
            if (successfulFeeds > 0 && (BackoffUntil is null || BackoffUntil <= now))
            foreach (var deal in candidates)
            {
                if (Paused || cancellationToken.IsCancellationRequested) break;
                Status = $"Checking deal details · {++checkedPages}/{candidates.Count}";
                deal.DetailAttemptedAt = DateTimeOffset.UtcNow;
                Changed?.Invoke();
                try
                {
                    var response = await client.GetAsync(deal.Url, false, cancellationToken);
                    var metrics = FeedParser.ParsePage(response.Content!);
                    if (metrics.Score is null && metrics.Comments is null)
                        throw new FormatException("Page counts unavailable; they will be checked again later.");
                    if (metrics.Score.HasValue) { deal.Score = metrics.Score; deal.ScoreCheckedAt = DateTimeOffset.UtcNow; }
                    if (metrics.Comments.HasValue) { deal.Comments = metrics.Comments; deal.CommentsCheckedAt = DateTimeOffset.UtcNow; }
                    if (metrics.PostedAt.HasValue) { deal.PostedAt = metrics.PostedAt; deal.OriginalPostDate = true; }
                    if (metrics.Expired.HasValue) deal.Expired = metrics.Expired.Value;
                    if (metrics.Category is not null) deal.Category = metrics.Category;
                    deal.DetailError = metrics.Comments is null ? "Comments unavailable; queued for a later check." : null;
                    if (baselineIds.Contains(deal.Id)) deal.ScoreBaselinePending = true;
                    if (AlertRules.Observe(state, deal, metrics.Score.HasValue, metrics.Comments.HasValue, DateTimeOffset.UtcNow) is { } alert)
                        pendingAlerts.Add(alert);
                }
                catch (Exception ex) when (Recoverable(ex, cancellationToken))
                {
                    deal.DetailError = Friendly(ex);
                    if (ex is FeedRequestException { RetryAt: { } retry }) { BackoffUntil = retry; errors.Add(Friendly(ex)); break; }
                }
                Changed?.Invoke();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            if (successfulFeeds > 0) state.LastPollAt = DateTimeOffset.UtcNow;
            state.Deals.RemoveAll(d => !d.Saved && d.LastSeenAt < DateTimeOffset.UtcNow.AddDays(-7));
            Status = Paused ? "Watching paused" : successfulFeeds > 0 ? "Watching for your next find" : "Offline · retry scheduled";
            int unavailable = candidates.Count(d => d.DetailError is not null);
            if (unavailable > 0) errors.Add($"Counts unavailable on {unavailable} checked page(s); saved counts are retained.");
            Error = errors.Count > 0 ? string.Join("  ", errors) : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            // Persist de-duplication before displaying popups, including partial and cancelled polls.
            bool saved = false;
            try { await store.SaveAsync(state); saved = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Error = "Could not save your data. Alerts are in this session's history: " + ex.Message; }
            Busy = false;
            NextPollAt = DateTimeOffset.UtcNow.AddMinutes(state.Settings.PollMinutes);
            if (BackoffUntil > NextPollAt) NextPollAt = BackoffUntil;
            _pollGate.Release();
            Changed?.Invoke();
            if (saved && pendingAlerts.Count > 0 && state.Settings.Notifications && !cancellationToken.IsCancellationRequested)
                AlertsReady?.Invoke(pendingAlerts);
        }
    }
    private static bool Recoverable(Exception ex, CancellationToken token) => ex is HttpRequestException or FeedRequestException or
        System.Xml.XmlException or FormatException or System.Text.RegularExpressions.RegexMatchTimeoutException ||
        ex is OperationCanceledException && !token.IsCancellationRequested;
    private static string Friendly(Exception ex) => ex is OperationCanceledException ? "The request timed out; it will be retried." : ex.Message;
}

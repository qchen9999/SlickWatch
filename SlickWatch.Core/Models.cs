using System.Text.Json.Serialization;
using System.ComponentModel;

namespace SlickWatch.Core;

public sealed class WatchSettings
{
    public int ThumbThreshold { get; set; } = 30;
    public int CommentThreshold { get; set; } = 50;
    public int PollMinutes { get; set; } = 5;
    public bool Frontpage { get; set; } = true;
    public bool Popular { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool Sound { get; set; }
    public bool StartWithWindows { get; set; }
    public int DetailChecksPerPoll { get; set; } = 12;
    public void Validate()
    {
        if (ThumbThreshold < 0 || ThumbThreshold > 100000 || CommentThreshold < 0 || CommentThreshold > 100000)
            throw new ArgumentException("Thresholds must be between 0 and 100,000.");
        if (PollMinutes < 5 || PollMinutes > 120) throw new ArgumentException("Choose a polling interval from 5 to 120 minutes.");
        if (!Frontpage && !Popular) throw new ArgumentException("Select at least one feed.");
        if (DetailChecksPerPoll < 1 || DetailChecksPerPoll > 25) throw new ArgumentException("Page checks must be between 1 and 25.");
    }
}

public sealed class Deal : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyUpdated() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string Author { get; set; } = "";
    public string Category { get; set; } = "";
    public HashSet<string> Sources { get; set; } = [];
    public DateTimeOffset? PostedAt { get; set; }
    public bool OriginalPostDate { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? ScoreCheckedAt { get; set; }
    public DateTimeOffset? CommentsCheckedAt { get; set; }
    public DateTimeOffset? DetailAttemptedAt { get; set; }
    public int? Score { get; set; }
    public int? Comments { get; set; }
    public bool Expired { get; set; }
    public bool Saved { get; set; }
    public bool ScoreBaselinePending { get; set; }
    public bool CommentBaselinePending { get; set; }
    public string? DetailError { get; set; }
    [JsonIgnore] public int DisplayRank { get; set; }
    [JsonIgnore] public string RankLabel => "#" + DisplayRank;
    [JsonIgnore] public string SourceLabel => string.Join(" + ", Sources.Order());
    [JsonIgnore] public string ScoreLabel => Score?.ToString("+0;-0;0") ?? "—";
    [JsonIgnore] public string CommentsLabel => Comments?.ToString("N0") ?? "—";
    [JsonIgnore] public string DateLabel => PostedAt?.ToLocalTime().ToString("MMM d · h:mm tt") ?? "Date unavailable";
    [JsonIgnore] public string DateKind => OriginalPostDate ? "Posted" : "RSS published";
    [JsonIgnore] public string StatusLabel => Expired ? "EXPIRED" : SourceLabel.ToUpperInvariant();
    [JsonIgnore] public string SaveLabel => Saved ? "★ Saved" : "☆ Save";
    [JsonIgnore] public string MetricsTooltip => $"Thumb score: {(ScoreCheckedAt?.ToLocalTime().ToString("g") ?? "not checked")}\nComments: {(CommentsCheckedAt?.ToLocalTime().ToString("g") ?? "queued for page check")}\n{DetailError}";
}

public sealed record DealAlert(string DealId, string Title, string Url, string Reason, DateTimeOffset CreatedAt);
public sealed record PageMetrics(int? Score, int? Comments, DateTimeOffset? PostedAt, bool? Expired, string? Category);
public sealed class WatchState
{
    public int Version { get; set; } = 1;
    public WatchSettings Settings { get; set; } = new();
    public List<Deal> Deals { get; set; } = [];
    public HashSet<string> InitializedFeeds { get; set; } = [];
    public HashSet<string> AcknowledgedDeals { get; set; } = [];
    public List<DealAlert> Alerts { get; set; } = [];
    public DateTimeOffset? LastPollAt { get; set; }
}

public static class AlertRules
{
    public static bool Matches(Deal deal, WatchSettings settings) => !deal.Expired &&
        (deal.Score > settings.ThumbThreshold || deal.Comments > settings.CommentThreshold);

    // Only freshly observed metrics can trigger an alert; an old cached count cannot.
    public static DealAlert? Observe(WatchState state, Deal deal, bool scoreObserved, bool commentsObserved, DateTimeOffset now)
    {
        bool baselineScore = scoreObserved && deal.ScoreBaselinePending;
        bool baselineComments = commentsObserved && deal.CommentBaselinePending;
        if (scoreObserved) deal.ScoreBaselinePending = false;
        if (commentsObserved) deal.CommentBaselinePending = false;
        if (deal.Expired || state.AcknowledgedDeals.Contains(deal.Id)) return null;
        bool scoreMatches = scoreObserved && deal.Score > state.Settings.ThumbThreshold;
        bool commentsMatch = commentsObserved && deal.Comments > state.Settings.CommentThreshold;
        if (!scoreMatches && !commentsMatch) return null;
        state.AcknowledgedDeals.Add(deal.Id);
        if ((scoreMatches && baselineScore || commentsMatch && baselineComments) &&
            !(scoreMatches && !baselineScore || commentsMatch && !baselineComments)) return null;
        var reasons = new List<string>();
        if (scoreMatches) reasons.Add($"{deal.Score} thumbs (>{state.Settings.ThumbThreshold})");
        if (commentsMatch) reasons.Add($"{deal.Comments} comments (>{state.Settings.CommentThreshold})");
        var alert = new DealAlert(deal.Id, deal.Title, deal.Url, string.Join(" · ", reasons), now);
        state.Alerts.Insert(0, alert);
        if (state.Alerts.Count > 200) state.Alerts.RemoveRange(200, state.Alerts.Count - 200);
        return alert;
    }
}

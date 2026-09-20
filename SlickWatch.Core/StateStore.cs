using System.Text.Json;

namespace SlickWatch.Core;

public sealed class StateStore(string directory)
{
    private readonly string _file = Path.Combine(directory, "state.json");
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string DirectoryPath => directory;
    public string? Warning { get; private set; }

    public WatchState Load()
    {
        Directory.CreateDirectory(directory);
        if (!File.Exists(_file)) return new();
        foreach (string candidate in new[] { _file, _file + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var state = JsonSerializer.Deserialize<WatchState>(File.ReadAllText(candidate), Options)
                    ?? throw new JsonException("Empty saved state.");
                state.Settings.Validate();
                if (state.Version != 1 || state.Deals is null || state.Alerts is null || state.AcknowledgedDeals is null || state.InitializedFeeds is null)
                    throw new JsonException("Unrecognized saved state.");
                state.Deals = state.Deals.Where(d => FeedParser.IsDealUrl(d.Url) && !string.IsNullOrWhiteSpace(d.Id))
                    .GroupBy(d => d.Id).Select(g => g.First()).ToList();
                foreach (var deal in state.Deals)
                {
                    deal.Sources ??= [];
                    if (!FeedParser.IsImageUrl(deal.ImageUrl)) deal.ImageUrl = null;
                }
                if (candidate.EndsWith(".bak", StringComparison.Ordinal)) Warning = "Recovered your saved deals from the backup.";
                return state;
            }
            catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or NullReferenceException)
            {
                // Keep the damaged file for recovery before the next successful save.
                File.Copy(candidate, candidate + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true);
                Warning = "Saved data could not be read. A recovery copy was kept; existing deals will load quietly.";
            }
        }
        return new();
    }

    public async Task SaveAsync(WatchState state, CancellationToken cancellationToken = default)
    {
        // Snapshot on the caller's context before asynchronous I/O, avoiding concurrent collection reads.
        string json = JsonSerializer.Serialize(state, Options);
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            string temp = _file + ".tmp";
            await File.WriteAllTextAsync(temp, json, cancellationToken);
            if (File.Exists(_file)) File.Copy(_file, _file + ".bak", true);
            File.Move(temp, _file, true);
        }
        finally { _saveGate.Release(); }
    }
}

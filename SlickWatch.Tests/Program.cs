using System.Security;
using System.Xml;
using SlickWatch.Core;
using SlickWatch.Platform;
using System.Xml.Linq;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> run) => tests.Add((name, run));
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
var now = DateTimeOffset.UtcNow;
Deal Deal(int? score = null, int? comments = null) => new() { Id = "20033388", Title = "A useful deal", Url = "https://slickdeals.net/f/20033388", Score = score, Comments = comments, FirstSeenAt = now, LastSeenAt = now, Sources = ["Frontpage"] };
string Rss(int score = 10, string id = "20033388", string extra = "") => $"""
    <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/"><channel><title>Test feed</title><item>
    <title><![CDATA[Controller & Dock $48]]></title><link>https://slickdeals.net/f/{id}-controller?utm_source=rss</link>
    <description>Fallback</description><content:encoded><![CDATA[<img src="https://static.slickdealscdn.com/test.thumb"><div>Thumb Score: +{score}</div><div>Good controller. Previous deal had 57 comments and 103 Deal Score.{extra}</div>]]></content:encoded>
    <pubDate>{now:R}</pubDate><guid>thread-{id}</guid></item></channel></rss>
    """;
string Page(int score, int comments, bool expired = false) => $"""
    <html><head><meta name="dealScore" content="{score}"><meta name="expired" content="{(expired ? "yes" : "no")}">
    <meta name="schema-date-published" content="{now:O}"></head><body>
    <span class="sidebarDeals__socialCommentCount">999</span>
    <span class="dealDetailsSocialActions__commentCount" data-xyz>{comments}</span>
    <script>window.count = 98765;</script></body></html>
    """;
string Temp() { var path = Path.Combine(Path.GetTempPath(), "SlickWatch-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }

Test("30 thumbs and 50 comments do not qualify; 31 OR 51 does", () =>
{
    var s = new WatchSettings();
    Assert(!AlertRules.Matches(Deal(30, 50), s));
    Assert(AlertRules.Matches(Deal(31, 0), s));
    Assert(AlertRules.Matches(Deal(0, 51), s));
    Assert(!AlertRules.Matches(Deal(null, null), s));
});
Test("Threshold crossing alerts once even after counts fall and rise", () =>
{
    var state = new WatchState(); var d = Deal(30, 50);
    Assert(AlertRules.Observe(state, d, true, true, now) is null);
    d.Score = 31; Assert(AlertRules.Observe(state, d, true, false, now) is not null);
    d.Score = 20; Assert(AlertRules.Observe(state, d, true, false, now) is null);
    d.Score = 99; d.Comments = 80;
    Assert(AlertRules.Observe(state, d, true, true, now) is null);
    Assert(state.Alerts.Count == 1);
});
Test("Stale counts do not trigger fresh alerts", () =>
{
    var state = new WatchState(); var d = Deal(80, 2);
    Assert(AlertRules.Observe(state, d, false, true, now) is null);
    Assert(AlertRules.Observe(state, d, true, false, now) is not null);
});
Test("First observations of baseline deals remain silent", () =>
{
    var state = new WatchState(); var d = Deal(15, null); d.ScoreBaselinePending = d.CommentBaselinePending = true;
    Assert(AlertRules.Observe(state, d, true, false, now) is null);
    d.Comments = 75; Assert(AlertRules.Observe(state, d, false, true, now) is null);
    Assert(state.AcknowledgedDeals.Contains(d.Id));
});
Test("Below-threshold baseline can cross a threshold later", () =>
{
    var state = new WatchState(); var d = Deal(15, 12); d.ScoreBaselinePending = d.CommentBaselinePending = true;
    Assert(AlertRules.Observe(state, d, true, true, now) is null);
    d.Comments = 51; Assert(AlertRules.Observe(state, d, false, true, now) is not null);
});
Test("Expired deals never qualify", () =>
{
    var state = new WatchState(); var d = Deal(200, 100); d.Expired = true;
    Assert(!AlertRules.Matches(d, state.Settings)); Assert(AlertRules.Observe(state, d, true, true, now) is null);
});
Test("RSS extracts content, score, image, ID and date without prose counts", () =>
{
    var d = FeedParser.Parse(Rss(15), "Frontpage", now).Single();
    Assert(d.Score == 15); Assert(d.Comments is null); Assert(d.Title == "Controller & Dock $48");
    Assert(d.Id == "20033388"); Assert(d.ImageUrl == "https://static.slickdealscdn.com/test.thumb");
    Assert(!d.Url.Contains('?')); Assert(d.PostedAt.HasValue); Assert(d.Description.Contains("57 comments"));
    Assert(!d.Description.Contains("Thumb Score"));
});
Test("Parser supports signed comma-separated scores and two-digit RSS years", () =>
{
    var rss = Rss().Replace("Thumb Score: +10", "Thumb Score: -1,234");
    Assert(FeedParser.Parse(rss, "Popular", now).Single().Score == -1234);
    Assert(FeedParser.ParseDate("Sat, 19 Sep 26 23:02:22 +0000")?.Year == 2026);
    Assert(FeedParser.ParseDate("Sat, 19 Sep 2026 23:02:22 GMT")?.Offset == TimeSpan.Zero);
    Assert(FeedParser.ParseDate("not a date") is null);
});
Test("Description-only RSS is supported", () =>
{
    var rss = "<rss><channel><item><title>A deal</title><link>https://slickdeals.net/f/123-deal</link><description><![CDATA[<div>Thumb Score: +33</div>Buy this]]></description></item></channel></rss>";
    Assert(FeedParser.Parse(rss, "Popular", now).Single().Score == 33);
});
Test("XML external entities and HTML error pages are rejected", () =>
{
    try { FeedParser.Parse("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><rss><channel>&x;</channel></rss>", "Frontpage", now); throw new Exception("DTD accepted"); } catch (XmlException) { }
    try { FeedParser.Parse("<html>Access denied</html>", "Frontpage", now); throw new Exception("HTML accepted"); } catch (FormatException) { }
});
Test("Only HTTPS deal URLs and approved image hosts are accepted", () =>
{
    Assert(!FeedParser.IsDealUrl("file:///c:/windows/system32/cmd.exe"));
    Assert(!FeedParser.IsDealUrl("https://slickdeals.net.evil.test/f/123"));
    Assert(!FeedParser.IsDealUrl("https://user@slickdeals.net/f/123"));
    Assert(!FeedParser.IsDealUrl("https://slickdeals.net:8443/f/123"));
    Assert(!FeedParser.IsImageUrl("https://tracker.invalid/a.jpg"));
    Assert(FeedParser.Parse(Rss().Replace("https://slickdeals.net/f/", "https://evil.test/f/"), "Frontpage", now).Count == 0);
});
Test("Main page counts win over sidebar, prose and Nuxt references", () =>
{
    var p = FeedParser.ParsePage(Page(15, 12));
    Assert(p.Score == 15 && p.Comments == 12); Assert(p.PostedAt.HasValue); Assert(p.Expired == false);
    Assert(FeedParser.ParsePage("<script>{\"commentCount\":123}</script><div>Previous deal: 300 comments</div>").Comments is null);
});
Test("Page parser tolerates attribute order, single quotes, commas and header fallback", () =>
{
    var p = FeedParser.ParsePage("<meta content='41' data-type='integer' name='dealScore'><h3 data-a class='commentsSectionV2__commentCount'>1,234 Comments</h3><meta content='yes' name='expired'>");
    Assert(p.Score == 41 && p.Comments == 1234 && p.Expired == true);
});
Test("Settings enforce useful ranges and an enabled feed", () =>
{
    foreach (var s in new[] { new WatchSettings { PollMinutes = 0 }, new WatchSettings { ThumbThreshold = -1 }, new WatchSettings { Frontpage = false, Popular = false }, new WatchSettings { DetailChecksPerPoll = 100 } })
    { try { s.Validate(); throw new Exception("Invalid settings accepted"); } catch (ArgumentException) { } }
});
AsyncTest("State persists alert deduplication across restart", async () =>
{
    string dir = Temp();
    try
    {
        var store = new StateStore(dir); var state = new WatchState(); var d = Deal(31, 1); state.Deals.Add(d);
        AlertRules.Observe(state, d, true, true, now); await store.SaveAsync(state);
        var loaded = new StateStore(dir).Load();
        Assert(loaded.Alerts.Count == 1); Assert(AlertRules.Observe(loaded, loaded.Deals.Single(), true, true, now) is null);
    }
    finally { Directory.Delete(dir, true); }
});
AsyncTest("Corrupt state is preserved and recovered from backup", async () =>
{
    string dir = Temp();
    try
    {
        var store = new StateStore(dir); var state = new WatchState(); state.Deals.Add(Deal(3, 4));
        await store.SaveAsync(state); await store.SaveAsync(state);
        await File.WriteAllTextAsync(Path.Combine(dir, "state.json"), "corrupt");
        var reader = new StateStore(dir); Assert(reader.Load().Deals.Count == 1); Assert(reader.Warning is not null);
        Assert(Directory.GetFiles(dir, "*.unreadable-*").Length == 1);
    }
    finally { Directory.Delete(dir, true); }
});
AsyncTest("Live-style initial poll is quiet even when page score exceeds RSS", async () =>
{
    string dir = Temp();
    try
    {
        var state = new WatchState { Settings = new() { DetailChecksPerPoll = 1 } };
        var client = new FakeClient((url, _) => Task.FromResult(new FeedResponse(url.Contains("newsearch") ? Rss(20) : Page(31, 60))));
        var watcher = new WatchService(state, new StateStore(dir), client);
        int popups = 0; watcher.AlertsReady += _ => popups++;
        await watcher.PollAsync();
        Assert(state.Deals.Count == 1); Assert(state.Deals.Single().Sources.Count == 2);
        Assert(state.Deals.Single().Comments == 60); Assert(state.Alerts.Count == 0 && popups == 0);
        Assert(state.InitializedFeeds.Count == 2);
    }
    finally { Directory.Delete(dir, true); }
});
AsyncTest("Already-tracked deals crossing thresholds alert during later polls", async () =>
{
    string dir = Temp();
    try
    {
        var state = new WatchState { Settings = new() { Popular = false, DetailChecksPerPoll = 1 }, InitializedFeeds = ["Frontpage"] };
        var d = Deal(30, 2); state.Deals.Add(d);
        var client = new FakeClient((url, _) => Task.FromResult(new FeedResponse(url.Contains("newsearch") ? Rss(31) : Page(31, 3))));
        var watcher = new WatchService(state, new StateStore(dir), client);
        int popups = 0; watcher.AlertsReady += a => popups += a.Count;
        await watcher.PollAsync(); await watcher.PollAsync();
        Assert(state.Alerts.Count == 1 && popups == 1);
    }
    finally { Directory.Delete(dir, true); }
});
AsyncTest("Off-feed recent deals continue to receive comment checks", async () =>
{
    string dir = Temp();
    try
    {
        var state = new WatchState { Settings = new() { Popular = false, DetailChecksPerPoll = 1 }, InitializedFeeds = ["Frontpage"] };
        state.Deals.Add(Deal(15, 49));
        var client = new FakeClient((url, _) => Task.FromResult(url.Contains("newsearch") ? new FeedResponse(null, true) : new FeedResponse(Page(16, 51))));
        await new WatchService(state, new StateStore(dir), client).PollAsync();
        Assert(state.Deals.Single().Comments == 51 && state.Alerts.Count == 1);
    }
    finally { Directory.Delete(dir, true); }
});
AsyncTest("429 backs off, does not prime feeds and retains cached data", async () =>
{
    string dir = Temp();
    try
    {
        var state = new WatchState(); state.Deals.Add(Deal(12, 10));
        var client = new FakeClient((_, _) => throw new FeedRequestException("Too many requests", now.AddMinutes(20)));
        var watcher = new WatchService(state, new StateStore(dir), client);
        await watcher.PollAsync(); await watcher.PollAsync();
        Assert(client.Calls == 1); Assert(state.InitializedFeeds.Count == 0 && state.Deals.Count == 1);
        Assert(watcher.BackoffUntil > now && watcher.Error is not null);
    }
    finally { Directory.Delete(dir, true); }
});
AsyncTest("Concurrent refreshes do not overlap; paused watcher does not request", async () =>
{
    string dir = Temp();
    try
    {
        var client = new FakeClient(async (_, token) => { await Task.Delay(100, token); return new FeedResponse("<rss><channel/></rss>"); });
        var watcher = new WatchService(new WatchState { Settings = new() { Popular = false } }, new StateStore(dir), client);
        await Task.WhenAll(watcher.PollAsync(), watcher.PollAsync()); Assert(client.Calls == 1);
        watcher.TogglePaused(); await watcher.PollAsync(); Assert(client.Calls == 1);
    }
    finally { Directory.Delete(dir, true); }
});
AsyncTest("A failed page does not erase previous counts", async () =>
{
    string dir = Temp();
    try
    {
        var state = new WatchState { Settings = new() { Popular = false, DetailChecksPerPoll = 1 }, InitializedFeeds = ["Frontpage"] }; state.Deals.Add(Deal(15, 40));
        var client = new FakeClient((url, _) => Task.FromResult(url.Contains("newsearch") ? new FeedResponse(null, true) : new FeedResponse("<html>Unavailable</html>")));
        await new WatchService(state, new StateStore(dir), client).PollAsync();
        Assert(state.Deals.Single().Comments == 40); Assert(state.Deals.Single().DetailError is not null);
    }
    finally { Directory.Delete(dir, true); }
});

Test("macOS startup arguments survive XML escaping and spaces", () =>
{
    string[] args = ["/Applications/Slick & Watch.app/Contents/MacOS/SlickWatch", "--tray"];
    var document = XDocument.Parse(DesktopIntegration.LaunchAgent(args));
    Assert(document.Descendants("array").Single().Elements("string").Select(e => e.Value).SequenceEqual(args));
    Assert(document.Descendants("true").Count() == 1);
});
Test("Linux startup quoting protects field codes, quotes and shell characters", () =>
{
    Assert(DesktopIntegration.DesktopArgument("/home/qi/My App/SlickWatch") == "\"/home/qi/My App/SlickWatch\"");
    Assert(DesktopIntegration.DesktopArgument("50%") == "\"50%%\"");
    Assert(DesktopIntegration.DesktopArgument("$x") == "\"\\\\$x\"");
    Assert(DesktopIntegration.DesktopArgument("a\"b") == "\"a\\\\\"b\"");
    try { DesktopIntegration.DesktopArgument("bad\nExec=other"); throw new Exception("Accepted an injected line"); }
    catch (ArgumentException) { }
    Assert(DesktopIntegration.LinuxDesktopEntry(["/opt/SlickWatch", "--tray"]).Contains("Exec=\"/opt/SlickWatch\" \"--tray\"\n"));
});
Test("Single instance activation reaches the first process profile", () =>
{
    string dir = Temp();
    try
    {
        using var first = new SingleInstance(dir);
        using var signal = new ManualResetEventSlim();
        first.Listen(signal.Set);
        using var second = new SingleInstance(dir);
        Assert(first.IsPrimary && !second.IsPrimary);
        Assert(second.ActivateExistingAsync().GetAwaiter().GetResult());
        Assert(signal.Wait(TimeSpan.FromSeconds(3)), "Activation was not delivered");
    }
    finally { Directory.Delete(dir, true); }
});
Test("Legacy Windows preference files retain startup and saved deal data", () =>
{
    const string json = """{"Version":1,"Settings":{"StartWithWindows":true,"ThumbThreshold":30,"CommentThreshold":50},"Deals":[{"Id":"123456","Saved":true}],"AcknowledgedDeals":["123456"]}""";
    var state = System.Text.Json.JsonSerializer.Deserialize<WatchState>(json)!;
    Assert(state.Settings.StartWithWindows && state.Deals.Single().Saved && state.AcknowledgedDeals.Contains("123456"));
});

int failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + "\n" + ex); }
}
Console.WriteLine($"\n{tests.Count - failed}/{tests.Count} checks passed.");
return failed == 0 ? 0 : 1;

sealed class FakeClient(Func<string, CancellationToken, Task<FeedResponse>> handler) : IFeedClient
{
    public int Calls { get; private set; }
    public Task<FeedResponse> GetAsync(string url, bool conditional, CancellationToken cancellationToken) { Calls++; return handler(url, cancellationToken); }
}

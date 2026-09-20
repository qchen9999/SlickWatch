using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SlickWatch.Core;

public static class FeedParser
{
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern,
        RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    private static string Replace(string text, string pattern, string replacement) => Regex.Replace(text, pattern,
        replacement, RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    public static string PlainText(string html)
    {
        html = Replace(html, @"<(script|style)\b[^>]*>.*?</\1>", "");
        html = Replace(html, @"<\s*(br\s*/?|/p|/div|/li|/blockquote)\s*>", "\n");
        html = Replace(html, @"<[^>]+>", "");
        html = WebUtility.HtmlDecode(html);
        html = Replace(html, @"[\t \u00a0]+", " ");
        return Replace(html, @"\n\s*\n\s*\n", "\n\n").Trim();
    }

    public static bool IsDealUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && (uri.Host == "slickdeals.net" || uri.Host == "www.slickdeals.net") &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && Match(uri.AbsolutePath, @"^/f/\d+(?:[-/]|$)").Success;
    public static bool IsImageUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host == "slickdeals.net" || uri.Host == "static.slickdealscdn.com");

    public static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = Replace(value, @"^[A-Za-z]{3},\s*", ""); // Ignore incorrect weekday labels.
        value = Replace(value, @"\b(?:GMT|UTC)\b", "+00:00");
        value = Replace(value, @"([+-]\d{2})(\d{2})$", "$1:$2");
        value = Replace(value, @"^(\d{1,2}\s+[A-Za-z]{3}\s+)(\d{2})(\s)", "${1}20$2$3");
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces |
            DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    public static List<Deal> Parse(string xml, string source, DateTimeOffset now)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 6_000_000 });
        var doc = XDocument.Load(reader);
        if (doc.Root?.Name.LocalName != "rss" || doc.Root.Element("channel") is null)
            throw new FormatException("The server did not return an RSS feed.");
        var deals = new List<Deal>();
        foreach (var item in doc.Descendants("item").Take(500))
        {
            var url = item.Element("link")?.Value.Trim();
            if (!IsDealUrl(url)) continue;
            var uri = new Uri(url!);
            var id = Match(uri.AbsolutePath, @"^/f/(\d+)").Groups[1].Value;
            string title = PlainText(item.Element("title")?.Value ?? "");
            if (title.Length == 0) continue;
            string html = item.Elements().FirstOrDefault(e => e.Name.LocalName == "encoded")?.Value
                ?? item.Element("description")?.Value ?? "";
            string content = PlainText(html);
            // Only the leading RSS-generated score is trusted, never counts mentioned in deal prose.
            var scoreMatch = Match(content, @"^Thumb\s+Score:\s*([+-]?[\d,]+)\b");
            var image = Match(html, "<img\\b[^>]*\\bsrc\\s*=\\s*[\"']([^\"']+)[\"']");
            var imageUrl = WebUtility.HtmlDecode(image.Groups[1].Value);
            var deal = new Deal
            {
                Id = id, Title = title, Url = uri.GetLeftPart(UriPartial.Path),
                Description = Replace(content, @"^Thumb\s+Score:\s*[+-]?[\d,]+\s*", "").Trim(),
                ImageUrl = IsImageUrl(imageUrl) ? imageUrl : null,
                Author = item.Elements().FirstOrDefault(e => e.Name.LocalName == "creator")?.Value ?? "",
                PostedAt = ParseDate(item.Element("pubDate")?.Value),
                Category = item.Element("category")?.Value ?? source,
                FirstSeenAt = now, LastSeenAt = now, Sources = [source],
                Score = ParseInt(scoreMatch.Groups[1].Value)
            };
            if (deal.Score.HasValue) deal.ScoreCheckedAt = now;
            deals.Add(deal);
        }
        return deals.GroupBy(d => d.Id).Select(g => g.First()).ToList();
    }

    private static int? ParseInt(string value) => int.TryParse(value.Replace(",", ""),
        NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number) ? number : null;

    public static PageMetrics ParsePage(string html)
    {
        // Scope to the main deal metadata and main comment header, avoiding sidebar deals,
        // prior-deal links and Nuxt's reference-index values (which are not actual counts).
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            var name = Attribute(tag.Value, "name");
            if (name is not null) meta[name] = Attribute(tag.Value, "content") ?? "";
        }
        meta.TryGetValue("dealScore", out var rawScore);
        var comments = Match(html, "<(?:span|h[1-6])\\b[^>]*\\bclass\\s*=\\s*[\"'][^\"']*(?:dealDetailsSocialActions__commentCount|commentsSectionV2__commentCount)[^\"']*[\"'][^>]*>\\s*([\\d,]+)(?:\\s+Comments)?\\s*<");
        meta.TryGetValue("schema-date-published", out var posted);
        if (posted is null) meta.TryGetValue("published_at", out posted);
        meta.TryGetValue("expired", out var expired);
        meta.TryGetValue("primaryCategory", out var category);
        return new(ParseInt(rawScore ?? ""), ParseInt(comments.Groups[1].Value), ParseDate(posted),
            expired?.ToLowerInvariant() switch { "yes" => true, "no" => false, _ => null }, category);
    }
    private static string? Attribute(string tag, string name)
    {
        var match = Match(tag, "\\b" + Regex.Escape(name) + "\\s*=\\s*([\"'])(.*?)\\1");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[2].Value) : null;
    }
}

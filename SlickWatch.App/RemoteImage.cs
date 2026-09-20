using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using System.Net.Http;

namespace SlickWatch.App;

public sealed class RemoteImage : Image
{
    public static readonly StyledProperty<string?> UrlProperty = AvaloniaProperty.Register<RemoteImage, string?>(nameof(Url));
    public string? Url { get => GetValue(UrlProperty); set => SetValue(UrlProperty, value); }
    public static bool Offline { get; set; }
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim Slots = new(4);
    private static readonly Dictionary<string, WeakReference<Bitmap>> Cache = [];
    private CancellationTokenSource? _request;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == UrlProperty) { CancelRequest(); Source = null; if (this.IsAttachedToVisualTree()) Load(); }
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Load(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { CancelRequest(); Source = null; base.OnDetachedFromVisualTree(e); }
    private void CancelRequest() { if (_request is { } request) _ = request.CancelAsync(); }
    private async void Load()
    {
        string? url = Url;
        if (Offline || !FeedParser.IsImageUrl(url)) return;
        if (Cache.TryGetValue(url!, out var entry) && entry.TryGetTarget(out var cached)) { Source = cached; return; }
        CancelRequest();
        using var request = new CancellationTokenSource(); _request = request;
        try
        {
            await Slots.WaitAsync(request.Token);
            try
            {
                // Android's native HTTP handler can drain a socket while disposing a response.
                // Keep the complete response lifetime (and cancellation callbacks) off the UI thread.
                var bitmap = await Task.Run(async () =>
                {
                    using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, request.Token);
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 5_000_000) return null;
                    await using var stream = await response.Content.ReadAsStreamAsync(request.Token);
                    using var buffer = new MemoryStream(); var bytes = new byte[8192]; int count;
                    while ((count = await stream.ReadAsync(bytes, request.Token)) > 0)
                    { if (buffer.Length + count > 5_000_000) return null; buffer.Write(bytes, 0, count); }
                    buffer.Position = 0;
                    return Bitmap.DecodeToWidth(buffer, 480);
                }, request.Token);
                if (bitmap is null) return;
                if (request.IsCancellationRequested || Url != url) { bitmap.Dispose(); return; }
                if (Cache.Count >= 256) Cache.Clear();
                Cache[url!] = new WeakReference<Bitmap>(bitmap); Source = bitmap;
            }
            finally { Slots.Release(); }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or ArgumentException or NotSupportedException) { }
        finally { if (ReferenceEquals(_request, request)) _request = null; }
    }
}

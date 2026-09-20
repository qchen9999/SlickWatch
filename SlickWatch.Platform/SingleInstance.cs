using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace SlickWatch.Platform;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    public bool IsPrimary { get; }

    public SingleInstance(string directory)
    {
        string path = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];
        _mutex = new Mutex(true, (OperatingSystem.IsWindows() ? @"Local\" : "") + "SlickWatch-" + identity, out bool created);
        IsPrimary = created;
        _pipeName = "SlickWatch-Show-" + identity;
    }
    public void Listen(Action activate)
    {
        if (!IsPrimary || _listener is not null) return;
        _listener = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(3));
                    byte[] data = new byte[1];
                    if (await server.ReadAsync(data, deadline.Token) == 1 && data[0] == 1) activate();
                }
                catch (OperationCanceledException) { }
                catch (IOException)
                {
                    try { await Task.Delay(200, _stop.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
            }
        });
    }
    public async Task<bool> ActivateExistingAsync()
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(timeout.Token);
            await client.WriteAsync(new byte[] { 1 }, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) { return false; }
    }
    public void Dispose()
    {
        _stop.Cancel();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        // Dispose on the same thread that created this instance (the application UI thread).
    }
}

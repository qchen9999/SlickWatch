using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace SlickWatch.App.Testing;

internal static class MacReopenCheck
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WindowAction(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ApplicationAction();

    internal static async Task RunAsync(MainWindow window, string libraryPath, string captures)
    {
        nint library = NativeLibrary.Load(Path.GetFullPath(libraryPath));
        try
        {
            var close = Marshal.GetDelegateForFunctionPointer<WindowAction>(NativeLibrary.GetExport(library, "sw_close_window"));
            var reopen = Marshal.GetDelegateForFunctionPointer<ApplicationAction>(NativeLibrary.GetExport(library, "sw_reopen_application"));
            var visible = Marshal.GetDelegateForFunctionPointer<WindowAction>(NativeLibrary.GetExport(library, "sw_window_is_visible"));
            nint handle = window.TryGetPlatformHandle()!.Handle;
            var evidence = new List<string>();
            try
            {
                // A second cycle catches handlers accidentally detached on close.
                for (int cycle = 1; cycle <= 2; cycle++)
                {
                    if (close(handle) != 1) throw new InvalidOperationException("Native red close button was not found.");
                    await WaitUntilAsync(() => !window.IsVisible && visible(handle) == 0);
                    evidence.Add($"Cycle {cycle}: red close button hid the native window.");
                    if (reopen() != 1) throw new InvalidOperationException("Cocoa reopen callback was not available.");
                    evidence.Add($"Cycle {cycle}: delivered Cocoa Dock reopen callback.");
                    await WaitUntilAsync(() => window.IsVisible && visible(handle) == 1 && window.WindowState != WindowState.Minimized);
                    evidence.Add($"Cycle {cycle}: native window reopened successfully.");
                }
            }
            finally { await File.WriteAllLinesAsync(Path.Combine(captures, "mac-reopen-check.txt"), evidence); }
        }
        finally { NativeLibrary.Free(library); }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(5))
                throw new InvalidOperationException("Mac close/reopen check timed out waiting for the native window state.");
            await Task.Delay(25);
        }
    }
}

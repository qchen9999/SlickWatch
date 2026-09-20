using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SlickWatch.Platform;

public static class DesktopIntegration
{
    public const string AppId = "com.qchen9999.SlickWatch";
    public static string DataDirectory => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "SlickWatch")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SlickWatch");

    public static string AutoStartFile => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", AppId + ".plist")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
            ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart", AppId + ".desktop");

    public static void SetAutoStart(bool enabled, string executable, string? managedAssembly = null)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('\n') || executable.Contains('\r'))
            throw new ArgumentException("Startup requires an absolute executable path.");
        string[] arguments = managedAssembly is null ? [executable, "--tray"] : [executable, managedAssembly, "--tray"];
        if (OperatingSystem.IsWindows()) { SetWindowsStartup(enabled, arguments); return; }
        string file = AutoStartFile;
        if (!enabled) { if (File.Exists(file)) File.Delete(file); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, OperatingSystem.IsMacOS() ? LaunchAgent(arguments) : LinuxDesktopEntry(arguments));
    }

    public static string LaunchAgent(IEnumerable<string> arguments) => new XDocument(
        new XDeclaration("1.0", "UTF-8", null),
        new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
        new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
            new XElement("key", "Label"), new XElement("string", AppId),
            new XElement("key", "ProgramArguments"), new XElement("array", arguments.Select(a => new XElement("string", a))),
            new XElement("key", "RunAtLoad"), new XElement("true")))).ToString();

    public static string LinuxDesktopEntry(IEnumerable<string> arguments) =>
        "[Desktop Entry]\nType=Application\nName=SlickWatch\nComment=Slickdeals deal alerts\nExec=" +
        string.Join(" ", arguments.Select(DesktopArgument)) + "\nTerminal=false\nStartupNotify=false\nX-GNOME-Autostart-enabled=true\n";

    public static string DesktopArgument(string value)
    {
        if (value.Contains('\n') || value.Contains('\r') || value.Contains('\0')) throw new ArgumentException("Invalid startup argument.");
        // Escape the quoted Exec argument, then the desktop-file string layer. % is an Exec field code.
        string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");
        return "\"" + escaped.Replace("\\", "\\\\").Replace("%", "%%") + "\"";
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsStartup(bool enabled, IEnumerable<string> arguments)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("SlickWatch", string.Join(" ", arguments.Select(a => "\"" + a.Replace("\"", "") + "\"")));
        else key.DeleteValue("SlickWatch", false);
    }

    public static async Task<bool> OpenUrlAsync(string url)
    {
        var start = new ProcessStartInfo { UseShellExecute = false };
        if (OperatingSystem.IsWindows()) { start.FileName = url; start.UseShellExecute = true; }
        else { start.FileName = OperatingSystem.IsMacOS() ? "/usr/bin/open" : "xdg-open"; start.ArgumentList.Add(url); }
        using var process = Process.Start(start);
        if (process is null) return start.UseShellExecute;
        if (!start.UseShellExecute) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await process.WaitForExitAsync(timeout.Token); return process.ExitCode == 0; }
        return true;
    }

    public static void PlayAlertSound()
    {
        if (OperatingSystem.IsWindows()) MessageBeep(0x40);
    }
    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool MessageBeep(uint type);
}

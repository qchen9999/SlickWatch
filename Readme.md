# SlickWatch

An Avalonia / .NET tray app for Windows, macOS and Linux that watches Slickdeals Frontpage + Popular RSS feeds and notifies you when a deal's **thumb score is greater than 30 OR its comment count is greater than 50**. Both thresholds are adjustable.

## Start

Build the app using the instructions below. Published builds include the .NET runtime; keep the entire published folder together in a permanent location if you enable startup at sign-in. Generated executables and local deal history are excluded from Git.

| Platform | Start | Background behavior |
| --- | --- | --- |
| Windows x64 | `artifacts/win-x64/SlickWatch.exe` | Closing or minimizing hides the window in the tray. |
| macOS Apple Silicon | `artifacts/osx-arm64/SlickWatch.app` | Closing hides the window; use the menu bar icon to reopen. |
| macOS Intel | `artifacts/osx-x64/SlickWatch.app` | Same application; separate Intel build. |
| Linux x64 | `artifacts/linux-x64/SlickWatch` | Closing minimizes to the task switcher; the tray icon appears on desktops that support it. |

Download packaged builds from [GitHub Releases](https://github.com/qchen9999/SlickWatch/releases/latest). On macOS, quit the old version, extract the archive and replace the complete `SlickWatch.app` in Applications. The app includes its Finder/Dock icon. These test builds are ad-hoc signed, not notarized: if macOS blocks opening, use **System Settings → Privacy & Security → Open Anyway** for the copy you downloaded from this repository ([Apple's instructions](https://support.apple.com/en-us/102445)).

On Linux, extract the archive into a permanent folder, then run `./install-desktop.sh` from that folder to add SlickWatch with its icon to your applications menu. This installs a launcher and icon for your user only; it does not copy the app or enable startup at sign-in. Run the script again if you move the folder. After upgrading, re-save Preferences with startup enabled if you want to refresh an older sign-in entry's icon. An existing pinned shortcut may need to be removed and pinned again after replacing the app.

- Browse deal cards with images, descriptions, URLs, posting dates, thumb scores, comments and a rank within the current view.
- Use **Matches**, **Saved deals**, search, feed filters and score/date/comment sorting.
- **View deal** opens your default browser. **Read description** shows the full RSS description, count timestamps, category, author and a copyable URL.
- Use the tray/menu bar icon to reopen SlickWatch, check now, pause/resume, open preferences, test an alert or exit. Windows may place the icon under its overflow arrow.
- **Exit SlickWatch** in the dashboard or tray stops the app. Linux keeps the dashboard accessible because tray support varies by desktop (GNOME may require an extension).
- **Test alert** previews a fading popup. It stays for 12 seconds and pauses dismissal while hovered. Larger batches are combined; all alerts appear in history.
- Alerts use Avalonia popup windows on every platform, not the OS notification center. Placement and focus depend on the desktop/window manager. Popups do not integrate with system Do Not Disturb; disable desktop alerts in Preferences when needed. Optional sound is currently Windows-only.
- Startup at sign-in and sound are **off by default**. On Linux, startup opens the dashboard so the app remains accessible without a tray.
- Version 2 reads the existing Windows data file without resetting saved deals or alert history. Exit version 1 before replacing its files.

## What is checked

The defaults are five-minute polling, both feeds enabled, and 12 deal-page checks per polling cycle. Requests are sequential, page requests are spaced by one second, and feeds use conditional requests when supported. Manual refresh cannot overlap an existing poll. Website throttling and access errors cause a retry delay; cached deals stay available.

RSS provides the **net thumb score**, not a separate count of positive voters. A score of exactly 30 or exactly 50 comments does not qualify; 31 thumbs or 51 comments does. The first scan of each newly enabled feed loads its existing deals silently. Each qualifying deal alerts only once across subsequent checks, source duplication and restarts. A previously seen deal can alert when its counts rise later. Muting popups still records qualifying deals in alert history; re-enabling popups does not replay them.

Slickdeals RSS does not provide a trustworthy current comment count. SlickWatch therefore reads the main comment counter and score from each public deal page. Numbers mentioned inside deal descriptions, sidebar deals and unrelated posts are deliberately ignored. No sign-in or private API is used.

**Coverage and timing:** feeds expose a finite window of deals. The app cannot recover deals it missed while closed, and does not watch every Slickdeals forum post. Page checks rotate across collected deals posted within the past three days, including deals that have left the feed. With 60 eligible deals and 12 checks every five minutes, one full rotation takes roughly 25 minutes plus request time. New arrivals or website failures can extend that delay. Increase page checks to at most 25 in Preferences if needed. Older collected deals still receive RSS score updates if present in a feed, but no longer get page checks.

Unknown counts show **—** rather than zero. Hover the metrics to see their timestamps and page-check errors. Cached counts can be stale. Page layout changes may temporarily prevent comment extraction; a warning is shown and the app retries later. A deal is marked expired only after its page reports that status, so stock and availability must still be checked on the site.

RSS publication time can reflect feed promotion. The card initially labels that time **RSS published**, then replaces it with the original **Posted** time after the page supplies it. Dates are displayed in your computer's local timezone. Rank is calculated locally from the displayed sort order; it is not a claimed site-wide Slickdeals rank.

## Your data

Preferences, up to seven days of unsaved feed history, saved deals, the latest 200 alert records and a persistent deduplication ledger are stored in `state.json` under the data folder below. Saved deals are retained. Saves use a temporary file and backup; unreadable files are preserved for recovery. Preferences includes a JSON export. Images are loaded from Slickdeals image hosts and are not included in the export.

The app contacts Slickdeals for feeds, public pages and images. There is no telemetry or account requirement. This is an independent, unofficial reader.

| Platform | Data folder | Optional sign-in registration |
| --- | --- | --- |
| Windows | `%LOCALAPPDATA%\SlickWatch` | Current-user Run registry entry named `SlickWatch` |
| macOS | `~/Library/Application Support/SlickWatch` | `~/Library/LaunchAgents/com.qchen9999.SlickWatch.plist` |
| Linux | `$XDG_DATA_HOME/SlickWatch` or `~/.local/share/SlickWatch` | `$XDG_CONFIG_HOME/autostart/com.qchen9999.SlickWatch.desktop` or `~/.config/autostart/…` |

To remove the app, disable **Start SlickWatch when I sign in**, choose **Exit SlickWatch**, then remove its application folder. Keep the data folder to retain history. Startup changes take effect at the next sign-in. macOS may ask you to allow the background item in System Settings.

## Build and maintain

The solution uses C# / .NET 10 and Avalonia 12.1.2, with no WPF or Windows Forms dependency. `SlickWatch.Core` handles feeds and alert rules; `SlickWatch.Platform` handles OS integration and single-instance activation; `SlickWatch.App` contains the shared interface; `SlickWatch.Tests` checks core and platform behavior.

Install the .NET 10 SDK from `global.json`. From the repository root, run in PowerShell 7:

```powershell
./build.ps1                              # current operating system and CPU
./build.ps1 -RuntimeIdentifier win-x64
./build.ps1 -RuntimeIdentifier linux-x64
./build.ps1 -RuntimeIdentifier osx-arm64
./build.ps1 -RuntimeIdentifier osx-x64
```

The script runs behavior checks, publishes under `artifacts/<runtime>/`, and assembles `.app` bundles for macOS. `-OutputDirectory <path>` overrides the output folder; use `./build.ps1 -RuntimeIdentifier win-x64 -OutputDirectory ./app` for the original Windows app location. `win-arm64` and `linux-arm64` are additional build targets outside the desktop test matrix.

On macOS the script applies a local ad-hoc signature. Public distribution still needs an Apple Developer ID signature and notarization; these are not supplied by this repository. Build Mac bundles on macOS before distribution: cross-publishing from Windows does not perform signing or preserve Unix executable permissions. See [Avalonia's macOS deployment guide](https://docs.avaloniaui.net/docs/deployment/macos).

Linux needs a graphical desktop with X11/XWayland and the native .NET dependencies. On Ubuntu, install `libx11-6 libice6 libsm6 libfontconfig1` plus `xdg-utils` for browser links. See [Avalonia's Linux deployment guide](https://docs.avaloniaui.net/docs/deployment/linux). A self-contained build includes .NET but does not replace these OS libraries.

Without PowerShell, build directly:

```sh
dotnet run --project SlickWatch.Tests -c Release
dotnet publish SlickWatch.App -c Release -r linux-x64 --self-contained true -o artifacts/linux-x64
chmod +x artifacts/linux-x64/SlickWatch
cp packaging/linux/{SlickWatch.png,com.qchen9999.SlickWatch.desktop,install-desktop.sh} artifacts/linux-x64/
chmod +x artifacts/linux-x64/install-desktop.sh
```

Open `SlickWatch.slnx` in a compatible IDE. The build script disables workload resolution to avoid an unrelated installer problem on the original development machine; desktop builds do not need mobile workloads.

## Verification

GitHub Actions builds the Windows, Linux and macOS packages, runs behavior checks, and launches the desktop app for offline smoke checks. Reports and screenshots are uploaded as separate artifacts. Checks exercise search, feed filters, saved views, sorting, pagination, preferences, popups and dashboard close behavior. Mac checks decode the packaged ICNS file and verify that Finder's native icon lookup returns the radar artwork. Linux checks inspect the real X11 window icon and window class, validate the installed desktop entry, and launch it from a path with spaces and special characters. Linux CI uses a virtual X display, so tray/menu interaction still needs a real desktop check.

The Windows ICO is the source artwork. `packaging/export-icons.ps1` converts it to the checked-in PNG and ICNS formats on Windows; normal builds need no image tools. macOS and Linux load PNG for the window/tray icon. The Mac bundle icon is copied before signing, and Linux packages include the PNG, desktop-entry template and installer.

To run checks separately:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet run --project SlickWatch.Tests/SlickWatch.Tests.csproj -c Release
```

Developer smoke testing uses `--smoke-test --data-dir <directory> --capture-dir <directory>` to render screenshots, check desktop behavior, write a JSON report and exit. It uses preview data and makes no feed requests by default. Add `--live` to fetch real feeds and deal pages. Use absolute paths and a fresh directory to check first-run behavior. Without `--data-dir`, smoke tests create a separate temporary profile. Successful cross-publishing alone does not establish that a target OS was tested.

Other launch options: `--tray` requests background startup where supported; `--data-dir <directory>` selects a separate profile. Reopening the same profile activates the existing instance instead of starting another polling loop.

## Verified data sources

- [Frontpage RSS](https://slickdeals.net/newsearch.php?mode=frontpage&searcharea=deals&searchin=first&rss=1)
- [Popular RSS](https://slickdeals.net/newsearch.php?mode=popdeals&searcharea=deals&searchin=first&rss=1)
- Public deal pages linked by those feeds, checked on September 19, 2026.
- [Avalonia documentation](https://docs.avaloniaui.net/)
- [FreeDesktop startup command escaping](https://specifications.freedesktop.org/desktop-entry/latest/exec-variables.html)

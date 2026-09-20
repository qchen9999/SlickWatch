# SlickWatch

A Windows tray app that watches Slickdeals Frontpage + Popular RSS feeds and notifies you when a deal's **thumb score is greater than 30 OR its comment count is greater than 50**. Both thresholds are adjustable.

## Start

Build the app using the instructions below, then open `app/SlickWatch.exe`. The Windows x64 build includes its .NET runtime; no separate runtime installation is needed to run the published app. Keep the executable in a permanent location if you enable startup at sign-in. This repository contains source code; generated executables and local deal history are excluded from Git.

- Browse deal cards with images, descriptions, URLs, posting dates, thumb scores, comments and a rank within the current view.
- Use **Matches**, **Saved deals**, search, feed filters and score/date/comment sorting.
- **View deal** opens your default browser. **Read description** shows the full RSS description, count timestamps, category, author and a copyable URL.
- Closing or minimizing the window leaves SlickWatch in the tray. Click its green radar icon to reopen it. Windows may place this icon under the tray's overflow arrow.
- Right-click the tray icon for check now, pause/resume, preferences, a test notification or **Exit**.
- **Test alert** previews the sliding popup. Popups stay for 12 seconds, pause dismissal while hovered, and do not take keyboard focus. Larger batches are combined into one popup; all alerts appear in history.
- Startup at Windows sign-in and notification sounds are **off by default**. Enable them in Preferences if wanted.

## What is checked

The defaults are five-minute polling, both feeds enabled, and 12 deal-page checks per polling cycle. Requests are sequential, page requests are spaced by one second, and feeds use conditional requests when supported. Manual refresh cannot overlap an existing poll. Website throttling and access errors cause a retry delay; cached deals stay available.

RSS provides the **net thumb score**, not a separate count of positive voters. A score of exactly 30 or exactly 50 comments does not qualify; 31 thumbs or 51 comments does. The first scan of each newly enabled feed loads its existing deals silently. Each qualifying deal alerts only once across subsequent checks, source duplication and restarts. A previously seen deal can alert when its counts rise later. Muting popups still records qualifying deals in alert history; re-enabling popups does not replay them.

Slickdeals RSS does not provide a trustworthy current comment count. SlickWatch therefore reads the main comment counter and score from each public deal page. Numbers mentioned inside deal descriptions, sidebar deals and unrelated posts are deliberately ignored. No sign-in or private API is used.

**Coverage and timing:** feeds expose a finite window of deals. The app cannot recover deals it missed while closed, and does not watch every Slickdeals forum post. Page checks rotate across collected deals posted within the past three days, including deals that have left the feed. With 60 eligible deals and 12 checks every five minutes, one full rotation takes roughly 25 minutes plus request time. New arrivals or website failures can extend that delay. Increase page checks to at most 25 in Preferences if needed. Older collected deals still receive RSS score updates if present in a feed, but no longer get page checks.

Unknown counts show **—** rather than zero. Hover the metrics to see their timestamps and page-check errors. Cached counts can be stale. Page layout changes may temporarily prevent comment extraction; a warning is shown and the app retries later. A deal is marked expired only after its page reports that status, so stock and availability must still be checked on the site.

RSS publication time can reflect feed promotion. The card initially labels that time **RSS published**, then replaces it with the original **Posted** time after the page supplies it. Dates are displayed in your computer's local timezone. Rank is calculated locally from the displayed sort order; it is not a claimed site-wide Slickdeals rank.

## Your data

Preferences, up to seven days of unsaved feed history, saved deals, the latest 200 alert records and a persistent deduplication ledger are stored in `%LOCALAPPDATA%\SlickWatch\state.json`. Saved deals are retained. Saves use a temporary file and backup; unreadable files are preserved for recovery. Preferences includes a JSON export of your collected deals. Images are loaded from Slickdeals image hosts and are not included in the JSON export.

The app contacts Slickdeals for feeds, public pages and images. There is no telemetry or account requirement. This is an independent, unofficial reader.

To remove the app, disable **Start in the tray when I sign in to Windows**, exit from the tray and delete the app folder. Keep the local data folder if you want to retain history for a later install.

## Build and maintain

The solution and build script are in the repository root. The app uses C# / .NET 10, WPF, the Windows Forms tray icon and built-in .NET libraries. There are no external application package dependencies.

Install the .NET 10 SDK on Windows, then run from the repository root:

```powershell
./build.ps1
```

This runs the behavior checks and publishes a self-contained Windows x64 executable to `app/`. Open `SlickWatch.slnx` in a compatible IDE to edit the app. The scripts set `MSBuildEnableWorkloadResolver=false` to avoid an unrelated workload installer problem observed with the development machine's SDK.

To run checks separately:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet run --project SlickWatch.Tests/SlickWatch.Tests.csproj -c Release
```

Useful launch options: `--tray` starts hidden; `--data-dir <directory>` selects an isolated data directory. Developer smoke testing uses `--smoke-test --data-dir <directory> --capture-dir <directory>` to do one live polling cycle, render screenshots of the dashboard/preferences/notification, check close-to-tray behavior, write a JSON report and exit. Use absolute paths and a fresh data directory for first-run verification.

## Verified data sources

- [Frontpage RSS](https://slickdeals.net/newsearch.php?mode=frontpage&searcharea=deals&searchin=first&rss=1)
- [Popular RSS](https://slickdeals.net/newsearch.php?mode=popdeals&searcharea=deals&searchin=first&rss=1)
- Public deal pages linked by those feeds, checked on September 19, 2026.
- [Microsoft: single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Microsoft: Windows tray icons](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0)

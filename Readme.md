# SlickWatch

An Avalonia / .NET tray app for Windows, macOS and Linux that watches Slickdeals Frontpage + Popular RSS feeds and notifies you when a deal's **thumb score is greater than 30 OR its comment count is greater than 50**. Both thresholds are adjustable.

An Android preview is also available. It runs independently on your phone, with no server, account, or desktop companion required.

## Android preview

Download `SlickWatch-android-arm64.apk` from the [Android preview release](https://github.com/qchen9999/SlickWatch/releases/tag/android-v0.1.0). It supports **64-bit ARM devices running Android 8.0 or later**. Open the APK on your phone and allow installation from the app you used to download it when Android asks. Future APKs use the same signing key and can be installed over this preview to retain your data.

- Browse phone-sized cards with images, descriptions, dates, scores and comments. Search, sort, save deals, and switch between All deals, Matches, Saved and Alerts.
- In **Settings**, use **Notification permission** to allow Android notifications, then **Send test notification**. These are native notifications in the notification shade; Android controls sound and Do Not Disturb through the notification channel settings.
- Foreground checks use your chosen interval (five minutes by default, with four deal-page checks per cycle on Android). Background checks use Android WorkManager, at **a minimum of 15 minutes**, and require a network connection. Battery management, Doze and device-specific restrictions can delay checks. This preview does not promise immediate alerts. See [Android's scheduling documentation](https://developer.android.com/develop/background-work/background-tasks/persistent/getting-started/define-work).
- **Pause** stops foreground checks and cancels scheduled background work. Turning off **Check in the background** still allows checks while the app is open. Android force-stop prevents work until you open SlickWatch again.
- The first scan loads existing deals quietly. Later qualifying deals alert once; tapping a single-deal notification opens its Slickdeals page. Notification history remains available when notifications are blocked.
- Settings, saved deals and history stay in the app's private storage on this device. No syncing or cloud backup is enabled. Uninstalling or clearing app data removes them. The Android preview currently has no JSON export.

Android uses the same feed parser, strict thresholds, throttling and deduplication rules as the desktop app. One process-wide coordinator serializes the foreground screen and background worker; the schedule and retry delay survive process restarts. A background run is limited to three minutes, so a slow connection can complete only part of a page-check rotation.

## Start

Build the app using the instructions below. Published builds include the .NET runtime; keep the entire published folder together in a permanent location if you enable startup at sign-in. Generated executables and local deal history are excluded from Git.

| Platform | Start | Background behavior |
| --- | --- | --- |
| Windows x64 | `artifacts/win-x64/SlickWatch.exe` | Closing or minimizing hides the window in the tray. |
| macOS Apple Silicon | `artifacts/osx-arm64/SlickWatch.app` | Closing hides the window; click the Dock icon or choose **Open SlickWatch** from the menu bar icon to reopen. |
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
cp packaging/linux/{SlickWatch.png,com.qchen9999.SlickWatch.desktop,install-desktop.sh,launch.sh} artifacts/linux-x64/
chmod +x artifacts/linux-x64/install-desktop.sh
```

Open `SlickWatch.slnx` in a compatible IDE. The build script disables workload resolution to avoid an unrelated installer problem on the original development machine; desktop builds do not need mobile workloads.

### Build Android

`SlickWatch.Android` is a separate host referencing `SlickWatch.Core`; it is deliberately outside the desktop solution so desktop contributors do not need the Android workload. Install the pinned .NET SDK, its Android workload, Android SDK platform 36/build tools, and JDK 21. Set `ANDROID_HOME` and `JAVA_HOME` to their installation directories. Then run:

```powershell
$env:MSBuildEnableWorkloadResolver = 'true'
dotnet workload install android --skip-manifest-update
dotnet build SlickWatch.Android -t:InstallAndroidDependencies -p:MSBuildEnableWorkloadResolver=true -p:AcceptAndroidSdkLicenses=True
./build-android.ps1 -Configuration Debug
./build-android.ps1 -Configuration Debug -RuntimeIdentifier android-x64 # emulator
```

The APK appears under `artifacts/SlickWatch-android-arm64.apk` (or `android-x64`). Use `-Dotnet`, `-AndroidSdkDirectory` and `-JavaSdkDirectory` to supply explicit tool paths. Release builds require `-KeyStore <file> -PasswordFile <file>`, using alias `slickwatch`. Never commit signing keys or passwords. The Android workflow reads `ANDROID_KEYSTORE_BASE64` and `ANDROID_KEYSTORE_PASSWORD` from repository secrets; pull requests build with a development key. Back up the release signing key securely to preserve upgrade compatibility.

The Android project pins the Lifecycle package family to the version required by WorkManager. It uses Avalonia 12's application host and activity view factory so recreating an activity does not reuse a detached desktop window. Native Android services supply scheduling, notifications, permission requests, and browser launching.

## Verification

The Android preview is exercised on an Android 16.1 x64 emulator, including live feeds/images, notification permission, saved deals, pause/resume, settings, process recreation, and a WorkManager run that recreates the process and posts a native notification with no activity open. Physical ARM64 device testing is still needed. Shared tests cover mobile scheduling, retry delays across restarts, alert deduplication, and serialized state changes.

Debug APKs have an emulator verification hook: launch the main activity with boolean intent extra `slickwatch.verify-worker=true` to enqueue a one-time worker after 20 seconds and immediately finish the activity. Kill the background process (without force-stop) before that delay to exercise a cold worker start. This uses the normal polling rules and data, so the next check must be due. The hook is excluded from Release APKs; normal background work remains periodic with a minimum interval of 15 minutes.

GitHub Actions builds the Windows, Linux and macOS packages, runs behavior checks, and launches the desktop app for offline smoke checks. Reports and screenshots are uploaded as separate artifacts. Checks exercise search, feed filters, saved views, sorting, pagination, preferences, popups and dashboard close behavior. Mac checks decode the packaged ICNS file and verify that Finder's native icon lookup returns the radar artwork. Linux checks inspect the real X11 window icon and window class, validate the installed desktop entry, and launch it from a path with spaces and special characters. Linux CI uses a virtual X display, so tray/menu interaction still needs a real desktop check.

Mac smoke checks also invoke the native red close button and Cocoa's Dock reopen callback, verifying that the existing window reappears across repeated cycles. The menu bar's **Open SlickWatch** callback is checked separately. The native test bridge is built only for CI checks and is excluded from downloadable app packages.

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

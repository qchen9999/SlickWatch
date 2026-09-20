#!/usr/bin/env bash
set -euo pipefail

# Keep the extracted app in its permanent location before installing the launcher.
app_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
data_dir=${XDG_DATA_HOME:-"$HOME/.local/share"}
if [[ "$data_dir" != /* ]]; then data_dir="$HOME/.local/share"; fi
if [[ "$app_dir" == *$'\n'* || "$app_dir" == *$'\r'* ]]; then
    printf 'The app folder cannot contain line breaks.\n' >&2
    exit 1
fi

# Quote the Exec argument, then escape the desktop-entry string layer. A percent
# is a desktop field code even inside quotes; never evaluate the resulting text.
executable="$app_dir/SlickWatch"
escaped=${executable//\\/\\\\}
escaped=${escaped//\"/\\\"}
escaped=${escaped//\`/\\\`}
escaped=${escaped//\$/\\\$}
escaped=${escaped//\\/\\\\}
escaped=${escaped//%/%%}
escaped=${escaped//$'\t'/\\t}

launcher="$data_dir/applications/com.qchen9999.SlickWatch.desktop"
icons="$data_dir/icons/hicolor/256x256/apps"
mkdir -p -- "$(dirname -- "$launcher")" "$icons"
install -m 644 -- "$app_dir/SlickWatch.png" "$icons/com.qchen9999.SlickWatch.png"
{
    cat -- "$app_dir/com.qchen9999.SlickWatch.desktop"
    printf 'Exec="%s"\n' "$escaped"
} > "$launcher"
chmod 644 -- "$launcher"
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$(dirname -- "$launcher")"
fi
printf 'Installed SlickWatch in your applications menu.\nKeep the app at: %s\n' "$app_dir"

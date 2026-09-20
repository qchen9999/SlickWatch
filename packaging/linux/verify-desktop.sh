#!/usr/bin/env bash
set -euo pipefail
app_dir=$(cd -- "$1" && pwd -P)
captures=$2
mkdir -p -- "$captures"

# Inspect the actual X11 property while the smoke-test dashboard is open.
"$app_dir/SlickWatch" --smoke-test --data-dir "$captures" --capture-dir "$captures" &
app_pid=$!
trap 'kill "$app_pid" 2>/dev/null || true' EXIT
for ((attempt=0; attempt<1000; attempt++)); do
    [[ -s "$captures/native-window-id.txt" ]] && break
    if ! kill -0 "$app_pid" 2>/dev/null; then
        cat "$captures/startup-error.txt" >&2 || true
        wait "$app_pid"
        exit 1
    fi
    sleep 0.02
done
xprop -len 1000000 -id "$(cat "$captures/native-window-id.txt")" -notype -f _NET_WM_ICON 32c _NET_WM_ICON WM_CLASS > "$captures/window-icon.txt"
wait "$app_pid"
trap - EXIT
python3 - "$captures/window-icon.txt" <<'PY'
import pathlib, sys
properties = pathlib.Path(sys.argv[1]).read_text()
line = next(line for line in properties.splitlines() if line.startswith('_NET_WM_ICON = '))
values = [int(value.strip()) for value in line.split(' = ', 1)[1].split(',')]
width, height = values[:2]
assert width >= 32 and height >= 32
pixels = values[2:2 + width * height]
assert len(pixels) == width * height, 'Incomplete native window icon'
teal = sum((p >> 24) > 127 and ((p >> 8) & 255) > ((p >> 16) & 255) * 1.3 and ((p >> 8) & 255) >= (p & 255) * 0.9 for p in pixels)
assert teal > width * height / 5, 'Window icon is blank or not the radar image'
assert 'WM_CLASS = "SlickWatch", "SlickWatch"' in properties
print(f'PASS: X11 received a {width}x{height} radar icon and matching window class')
PY

# Install into an isolated profile, with a path that exercises desktop Exec
# escaping. A tiny stand-in executable confirms the installed launcher works.
test_root=$(mktemp -d)
trap 'rm -rf -- "$test_root"' EXIT
test_app="$test_root/Space \"quote\" \$cash %percent \`tick\` \\slash"
mkdir -p -- "$test_app"
cp -- "$app_dir/install-desktop.sh" "$app_dir/com.qchen9999.SlickWatch.desktop" "$app_dir/SlickWatch.png" "$test_app/"
cat > "$test_app/SlickWatch" <<'SH'
#!/usr/bin/env bash
printf 'launched\n' > "$XDG_DATA_HOME/launch-ok"
SH
chmod +x -- "$test_app/SlickWatch"
export XDG_DATA_HOME="$test_root/profile data"
bash "$test_app/install-desktop.sh"
launcher="$XDG_DATA_HOME/applications/com.qchen9999.SlickWatch.desktop"
desktop-file-validate "$launcher"
cmp -- "$app_dir/SlickWatch.png" "$XDG_DATA_HOME/icons/hicolor/256x256/apps/com.qchen9999.SlickWatch.png"
gio launch "$launcher"
for ((attempt=0; attempt<100; attempt++)); do
    [[ -s "$XDG_DATA_HOME/launch-ok" ]] && break
    sleep 0.02
done
test "$(cat "$XDG_DATA_HOME/launch-ok")" = launched
printf 'PASS: Linux launcher resolves its icon and launches from paths containing special characters\n'

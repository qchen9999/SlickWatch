"""Exercise a Release APK in an isolated emulator; no live feed access is required."""
import pathlib
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

package = "com.qchen9999.slickwatch"
output = pathlib.Path("work/android-smoke")
output.mkdir(parents=True, exist_ok=True)


def adb(*args):
    return subprocess.check_output(["adb", *args], text=True, stderr=subprocess.STDOUT).strip()


if not adb("get-serialno").startswith("emulator-"):
    raise RuntimeError("Use an isolated emulator for release verification")

try:
    adb("install", "-r", sys.argv[1])
    adb("shell", "svc", "wifi", "disable")
    adb("shell", "svc", "data", "disable")
    component = adb("shell", "cmd", "package", "resolve-activity", "--brief", package).splitlines()[-1]
    adb("logcat", "-c")
    for cycle in range(3):
        adb("shell", "am", "force-stop", package)
        # The development-only worker hook must not finish a Release activity.
        adb("shell", "am", "start", "-n", component, "--ez", "slickwatch.verify-worker", "true")
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            time.sleep(2)
            activity = adb("shell", "dumpsys", "activity", "activities")
            if not any(package in line and ("topResumedActivity=" in line or "mResumedActivity:" in line) for line in activity.splitlines()):
                continue
            try:
                adb("shell", "uiautomator", "dump", "/sdcard/slickwatch-release.xml")
                tree = ET.fromstring(adb("shell", "cat", "/sdcard/slickwatch-release.xml"))
                if any(node.get("text") == "Settings" for node in tree.iter("node")):
                    break
            except (subprocess.CalledProcessError, ET.ParseError):
                pass
        else:
            raise RuntimeError(f"Release dashboard did not appear on launch {cycle + 1}")
        print(f"PASS Release launch {cycle + 1}", flush=True)
    adb("shell", "screencap", "-p", "/sdcard/slickwatch-release.png")
    adb("pull", "/sdcard/slickwatch-release.png", str(output / "dashboard.png"))
    (output / "result.txt").write_text("PASS: three Release process launches; dashboard rendered; debug hook excluded.\n")
finally:
    (output / "logcat.txt").write_text(adb("logcat", "-d"), encoding="utf-8")
    adb("shell", "svc", "wifi", "enable")
    adb("shell", "svc", "data", "enable")

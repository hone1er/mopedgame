#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

FULL_MODE=0
SKIP_MISSING_UNITY=0

usage() {
    cat <<'EOF'
Usage: ./scripts/verify.sh [--full] [--skip-missing-unity]

Modes:
  (default)             Run Gate 1 (dotnet test) and Gate 2 (Python static validators).
  --full                Run Gates 1-2 strictly, plus Unity Gates 3-5 (EditMode, PlayMode,
                        macOS arm64 player build, and a headless player boot smoke that scans
                        logs/player_boot.log for unhandled exceptions). Exits non-zero if Unity 6 LTS
                        is not installed. PLAYER_BOOT_SMOKE_SECONDS (default 6) sets the boot window.
  --skip-missing-unity  Pass --skip-missing-unity to Gate 2 (only valid without --full).
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --full)
            FULL_MODE=1
            shift
            ;;
        --skip-missing-unity)
            SKIP_MISSING_UNITY=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "[ERROR] Unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [[ "$FULL_MODE" -eq 1 && "$SKIP_MISSING_UNITY" -eq 1 ]]; then
    echo "[ERROR] --full cannot be combined with --skip-missing-unity." >&2
    exit 2
fi

find_dotnet() {
    if command -v dotnet >/dev/null 2>&1; then
        command -v dotnet
        return 0
    fi
    local candidate
    for candidate in \
        "/usr/local/share/dotnet/dotnet" \
        "/opt/homebrew/bin/dotnet" \
        "$HOME/.dotnet/dotnet"; do
        if [[ -x "$candidate" ]]; then
            echo "$candidate"
            return 0
        fi
    done
    return 1
}

find_unity() {
    if [[ -n "${UNITY_BIN:-}" ]]; then
        if [[ -x "$UNITY_BIN" ]]; then
            echo "$UNITY_BIN"
            return 0
        fi
        echo "[ERROR] UNITY_BIN is set to '$UNITY_BIN', which is not an executable binary." >&2
        return 1
    fi

    local selected
    selected="$(python3 -c '
import glob, os, re
candidates = [
    p for p in glob.glob("/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity")
    if os.access(p, os.X_OK)
]
def version_key(path):
    ver = path.split("/Editor/")[1].split("/")[0]
    nums = [int(x) for x in re.findall(r"\d+", ver)]
    is_unity6 = 1 if ver.startswith("6000.") else 0
    return (is_unity6, nums, ver)
if candidates:
    candidates.sort(key=version_key, reverse=True)
    print(candidates[0])
')"
    if [[ -n "$selected" ]]; then
        echo "$selected"
        return 0
    fi
    return 1
}

check_nunit_xml() {
    local xml_path="$1"
    local label="$2"
    python3 -c '
import sys, xml.etree.ElementTree as ET
from pathlib import Path
xml_path, label = Path(sys.argv[1]), sys.argv[2]
if not xml_path.is_file():
    print(f"[FAIL] {label}: expected test result XML missing at {xml_path}", file=sys.stderr)
    sys.exit(1)
root = ET.parse(xml_path).getroot()
total = int(root.attrib.get("total", "0"))
passed = int(root.attrib.get("passed", "0"))
failed = int(root.attrib.get("failed", "0"))
skipped = int(root.attrib.get("skipped", "0"))
result = root.attrib.get("result", "Unknown")
print(f"  NUnit {label}: result={result} total={total} passed={passed} failed={failed} skipped={skipped}")
if failed > 0 or result.startswith("Failed"):
    print(f"[FAIL] {label}: test failures reported in {xml_path}", file=sys.stderr)
    sys.exit(1)
if total == 0 or passed == 0:
    print(f"[FAIL] {label}: no tests executed (total={total})", file=sys.stderr)
    sys.exit(1)
' "$xml_path" "$label"
}

# >>> player_boot_smoke
# Gate 5 boot smoke: launches a built macOS player headlessly (-batchmode -nographics, so no window server, display, or
# GPU is required; stdin is /dev/null so it never grabs the terminal), lets it run for a bounded smoke window, then stops
# it if it is still alive in its main loop. Fails on: missing/non-arm64 executable, an early non-zero exit (crash), a
# missing/empty player log, or any unhandled exception / native crash marker in the log.
# Usage: player_boot_smoke <App.app> <player_log_path> [smoke_seconds]
PLAYER_BOOT_FATAL_REGEX='(^|[^A-Za-z0-9_])(NullReferenceException|MissingReferenceException|CatalogLoadException)([^A-Za-z0-9_]|$)|^[A-Za-z0-9_.`]*Exception(:|$)|Unhandled [Ee]xception|Crash!!!|Received signal SIG(SEGV|BUS|ABRT|ILL|FPE)|Segmentation fault'
PLAYER_BOOT_PID=""

player_boot_cleanup() {
    if [[ -n "${PLAYER_BOOT_PID:-}" ]] && kill -0 "$PLAYER_BOOT_PID" 2>/dev/null; then
        kill -KILL "$PLAYER_BOOT_PID" 2>/dev/null || true
    fi
    PLAYER_BOOT_PID=""
}

player_boot_smoke() {
    local app="$1"
    local log_path="$2"
    local smoke_seconds="${3:-6}"
    if [[ ! "$smoke_seconds" =~ ^[0-9]+$ ]] || (( smoke_seconds < 1 )); then
        smoke_seconds=6
    fi
    local plist="$app/Contents/Info.plist"
    local exe_name=""
    local exe=""

    if [[ -f "$plist" && -x /usr/libexec/PlistBuddy ]]; then
        exe_name="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$plist" 2>/dev/null || true)"
    fi
    if [[ -z "$exe_name" && -d "$app/Contents/MacOS" ]]; then
        exe_name="$(ls -1 "$app/Contents/MacOS" 2>/dev/null | head -n 1 || true)"
    fi
    exe="$app/Contents/MacOS/$exe_name"
    if [[ -z "$exe_name" || ! -f "$exe" || ! -x "$exe" ]]; then
        echo "[FAIL] Gate 5 boot smoke: player executable missing under $app/Contents/MacOS" >&2
        return 1
    fi

    if command -v lipo >/dev/null 2>&1; then
        local archs
        archs="$(lipo -archs "$exe" 2>/dev/null || true)"
        if [[ " $archs " != *" arm64 "* ]]; then
            echo "[FAIL] Gate 5 boot smoke: $exe is not an arm64 binary (lipo archs: '${archs:-unknown}')" >&2
            return 1
        fi
        echo "  Player executable: $exe (archs: $archs)"
    fi

    local stdout_log="${log_path%.log}.stdout.log"
    rm -f "$log_path" "$stdout_log"
    trap player_boot_cleanup EXIT
    trap 'player_boot_cleanup; exit 130' INT TERM

    "$exe" -batchmode -nographics -logFile "$log_path" </dev/null >"$stdout_log" 2>&1 &
    PLAYER_BOOT_PID=$!
    echo "  Launched player pid $PLAYER_BOOT_PID headless for a ${smoke_seconds}s boot smoke window (log: $log_path)"

    local ticks=$((smoke_seconds * 4))
    local waited=0
    while (( waited < ticks )) && kill -0 "$PLAYER_BOOT_PID" 2>/dev/null; do
        sleep 0.25
        waited=$((waited + 1))
    done

    local stopped_by_gate=0
    local boot_window_lines=""
    if kill -0 "$PLAYER_BOOT_PID" 2>/dev/null; then
        stopped_by_gate=1
        # Only judge what the player logged during the boot window, not shutdown noise caused by our own SIGTERM.
        boot_window_lines="$(awk 'END { print NR }' "$log_path" 2>/dev/null || true)"
        kill -TERM "$PLAYER_BOOT_PID" 2>/dev/null || true
        local grace=0
        while (( grace < 20 )) && kill -0 "$PLAYER_BOOT_PID" 2>/dev/null; do
            sleep 0.25
            grace=$((grace + 1))
        done
        if kill -0 "$PLAYER_BOOT_PID" 2>/dev/null; then
            kill -KILL "$PLAYER_BOOT_PID" 2>/dev/null || true
        fi
    fi

    local status=0
    wait "$PLAYER_BOOT_PID" 2>/dev/null || status=$?
    PLAYER_BOOT_PID=""
    trap - EXIT INT TERM

    if [[ "$stopped_by_gate" -eq 1 ]]; then
        echo "  Player still alive in main loop after ${smoke_seconds}s; stopped by gate (exit $status, expected)."
    elif [[ "$status" -ne 0 ]]; then
        echo "[FAIL] Gate 5 boot smoke: player exited early with code $status (crash on boot). See $log_path / $stdout_log" >&2
        [[ -f "$log_path" ]] && tail -n 40 "$log_path" >&2
        return 1
    else
        echo "  Player exited cleanly (code 0) within the smoke window."
    fi

    if [[ ! -s "$log_path" ]]; then
        echo "[FAIL] Gate 5 boot smoke: player log $log_path is missing or empty; cannot prove a clean boot." >&2
        return 1
    fi

    local scan_lines
    scan_lines="$(awk 'END { print NR }' "$log_path")"
    if [[ "$stopped_by_gate" -eq 1 && "$boot_window_lines" =~ ^[0-9]+$ ]]; then
        scan_lines="$boot_window_lines"
    fi
    if [[ "$scan_lines" -eq 0 ]]; then
        echo "[FAIL] Gate 5 boot smoke: player wrote nothing to $log_path during the ${smoke_seconds}s boot window." >&2
        return 1
    fi

    local hits
    hits="$(head -n "$scan_lines" "$log_path" | grep -nE "$PLAYER_BOOT_FATAL_REGEX" 2>/dev/null | head -n 20 || true)"
    if [[ -n "$hits" ]]; then
        echo "[FAIL] Gate 5 boot smoke: unhandled exception / crash markers in $log_path:" >&2
        echo "$hits" >&2
        return 1
    fi

    echo "  Boot log clean: scanned $scan_lines boot-window lines, zero unhandled exceptions or crash markers."
}
# <<< player_boot_smoke

echo "=== Gate 1: Headless .NET Core & Data Tests ==="
DOTNET_BIN="$(find_dotnet || true)"
if [[ -z "$DOTNET_BIN" ]]; then
    echo "[ERROR] dotnet SDK not found on PATH or standard install locations." >&2
    exit 1
fi

if [[ ! -d "tests" ]]; then
    echo "[ERROR] tests/ directory not found." >&2
    exit 1
fi

if [[ -f "tests/MopedSim.Tests.sln" ]]; then
    "$DOTNET_BIN" test "tests/MopedSim.Tests.sln" --configuration Release
else
    CSPROJ_FOUND=0
    while IFS= read -r csproj; do
        CSPROJ_FOUND=1
        echo "[WARN] tests/MopedSim.Tests.sln not found; running $csproj directly."
        "$DOTNET_BIN" test "$csproj" --configuration Release
    done < <(find tests -maxdepth 2 -name "*.csproj" | sort)
    if [[ "$CSPROJ_FOUND" -eq 0 ]]; then
        echo "[ERROR] No tests/MopedSim.Tests.sln or test .csproj files found under tests/." >&2
        exit 1
    fi
fi

echo ""
echo "=== Gate 2: Python Static Boundary, Data & Asset Validators ==="
if [[ "$FULL_MODE" -eq 1 ]]; then
    python3 tools/validators/validate_all.py --strict
elif [[ "$SKIP_MISSING_UNITY" -eq 1 ]]; then
    python3 tools/validators/validate_all.py --skip-missing-unity
else
    python3 tools/validators/validate_all.py
fi

if [[ "$FULL_MODE" -eq 0 ]]; then
    echo ""
    echo "[PASS] Gates 1-2 passed. Run './scripts/verify.sh --full' to run Unity Gates 3-5."
    exit 0
fi

echo ""
echo "=== Checking Unity 6 LTS Editor for Full Gates 3-5 ==="
UNITY_EDITOR="$(find_unity || true)"
if [[ -z "$UNITY_EDITOR" ]]; then
    echo "[ERROR] Unity 6 LTS editor binary not found; cannot execute Gates 3-5 (--full)." >&2
    echo "        Set UNITY_BIN=/path/to/Unity or install Unity 6000.x LTS via Unity Hub at:" >&2
    echo "        /Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity" >&2
    exit 1
fi
echo "Using Unity Editor: $UNITY_EDITOR"

if [[ ! -d "unity/Assets" ]]; then
    echo "[ERROR] unity/Assets directory does not exist; cannot execute Unity Gates 3-5." >&2
    exit 1
fi

mkdir -p "$REPO_ROOT/logs"
LOG_DIR="$REPO_ROOT/logs"
UNITY_PROJECT="$REPO_ROOT/unity"

# Fails if Unity compiled project scripts (Assets/ or local MopedSim packages) with any C# warning or error.
check_unity_compile_log() {
    local log_path="$1"
    local label="$2"
    local hits
    hits="$(grep -E "^(Assets/|Packages/com\.mopedgame\.).*: (warning|error) CS[0-9]+" "$log_path" 2>/dev/null | sort -u || true)"
    if [[ -n "$hits" ]]; then
        echo "[FAIL] $label: project C# compiler diagnostics found in $log_path:" >&2
        echo "$hits" >&2
        return 1
    fi
}

# Runs a Unity Test Framework pass. NOTE: never pass -quit together with -runTests;
# Unity exits on its own once the run completes (-quit would shut it down before tests run).
run_unity_tests() {
    local platform="$1"
    local xml_path="$2"
    local log_path="$3"
    rm -f "$xml_path"
    local status=0
    "$UNITY_EDITOR" -batchmode -nographics -projectPath "$UNITY_PROJECT" \
        -runTests -testPlatform "$platform" \
        -testResults "$xml_path" \
        -logFile "$log_path" || status=$?
    echo "  Unity $platform exit code: $status (log: $log_path)"
    if grep -q "Multiple Unity instances cannot open the same project" "$log_path" 2>/dev/null; then
        echo "[FAIL] $platform: another Unity instance has the project open; close it and re-run." >&2
        return 1
    fi
    check_unity_compile_log "$log_path" "$platform" || return 1
    check_nunit_xml "$xml_path" "$platform" || return 1
    if [[ "$status" -ne 0 ]]; then
        echo "[FAIL] $platform: Unity exited with code $status." >&2
        return 1
    fi
}

echo ""
echo "=== Gate 3: Unity EditMode Tests ==="
run_unity_tests EditMode "$LOG_DIR/editmode.xml" "$LOG_DIR/unity_editmode.log"

echo ""
echo "=== Gate 4: Unity PlayMode Scenario Tests ==="
run_unity_tests PlayMode "$LOG_DIR/playmode.xml" "$LOG_DIR/unity_playmode.log"

echo ""
echo "=== Gate 5: macOS arm64 Standalone Dev Build Smoke Check ==="
BUILD_APP="$UNITY_PROJECT/Builds/MacOS/arm64/MopedGame.app"
rm -rf "$BUILD_APP"
BUILD_STATUS=0
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$UNITY_PROJECT" \
    -executeMethod MopedSim.Editor.CliBuild.BuildMacOSDev \
    -logFile "$LOG_DIR/unity_build.log" -quit || BUILD_STATUS=$?
echo "  Unity build exit code: $BUILD_STATUS (log: $LOG_DIR/unity_build.log)"
check_unity_compile_log "$LOG_DIR/unity_build.log" "Build" || exit 1
if [[ "$BUILD_STATUS" -ne 0 ]]; then
    echo "[FAIL] Gate 5: Unity build exited with code $BUILD_STATUS." >&2
    exit 1
fi
if [[ ! -d "$BUILD_APP" ]]; then
    echo "[FAIL] Gate 5: expected player bundle missing at $BUILD_APP" >&2
    exit 1
fi
echo "  Built: $BUILD_APP ($(du -sh "$BUILD_APP" | cut -f1))"

echo ""
echo "=== Gate 5b: Headless Standalone Player Boot Smoke ==="
player_boot_smoke "$BUILD_APP" "$LOG_DIR/player_boot.log" "${PLAYER_BOOT_SMOKE_SECONDS:-6}" || exit 1

echo ""
echo "[PASS] All 5 verification gates passed."

#!/usr/bin/env bash
# Profile the release macOS arm64 player on Apple Silicon.
#
# Usage: ./scripts/profile_player.sh [--build] [--seconds N] [--app PATH] [--no-xctrace]
#   --build        Build the release player first (CliBuild.BuildMacOSRelease; needs UNITY_BIN or the default 6000.6.10f1 install).
#   --seconds N    Seconds to run the windowed player (default 30).
#   --app PATH     Player .app (default unity/Builds/MacOS/arm64-release/MopedGame.app).
#   --no-xctrace   Skip the Instruments Time Profiler trace and only sample CPU/RSS with ps.
#
# Output: logs/perf/<timestamp>/{player.log, samples.csv, summary.txt, [timeprofiler.trace]}
# NOTE: the player is launched idle (title/garage, no ride input), so CPU/RSS describe an idle scene unless an autoplay flag is
# passed via PLAYER_ARGS; use PerformanceSoakTests for riding frame stats. The player is launched windowed; if it supports -autoplayCapture, set PLAYER_ARGS="-autoplayCapture" to use it.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

DO_BUILD=0
SECONDS_TO_RUN=30
APP="unity/Builds/MacOS/arm64-release/MopedGame.app"
USE_XCTRACE=1

while [[ $# -gt 0 ]]; do
    if [[ ( "$1" == "--seconds" || "$1" == "--app" ) && $# -lt 2 ]]; then
        echo "[ERROR] $1 requires a value" >&2
        exit 2
    fi
    case "$1" in
        --build) DO_BUILD=1; shift ;;
        --seconds) SECONDS_TO_RUN="${2:-}"; shift 2 ;;
        --app) APP="${2:-}"; shift 2 ;;
        --no-xctrace) USE_XCTRACE=0; shift ;;
        -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
        *) echo "[ERROR] Unknown option: $1" >&2; exit 2 ;;
    esac
done

if [[ ! "$SECONDS_TO_RUN" =~ ^[0-9]+$ ]] || (( SECONDS_TO_RUN < 3 )); then
    echo "[ERROR] --seconds must be an integer >= 3" >&2
    exit 2
fi

if [[ "$DO_BUILD" -eq 1 ]]; then
    UNITY_BIN="${UNITY_BIN:-/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity}"
    if [[ ! -x "$UNITY_BIN" ]]; then
        echo "[ERROR] Unity not found at $UNITY_BIN (set UNITY_BIN)." >&2
        exit 1
    fi
    mkdir -p logs
    echo "[profile] Building release player..."
    "$UNITY_BIN" -batchmode -nographics -projectPath unity \
        -executeMethod MopedSim.Editor.CliBuild.BuildMacOSRelease -quit -logFile "$REPO_ROOT/logs/release_build.log"
fi

PLIST="$APP/Contents/Info.plist"
EXE_NAME=""
if [[ -f "$PLIST" && -x /usr/libexec/PlistBuddy ]]; then
    EXE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$PLIST" 2>/dev/null || true)"
fi
if [[ -z "$EXE_NAME" && -d "$APP/Contents/MacOS" ]]; then
    EXE_NAME="$(ls -1 "$APP/Contents/MacOS" | head -n 1 || true)"
fi
EXE="$APP/Contents/MacOS/$EXE_NAME"
if [[ -z "$EXE_NAME" || ! -x "$EXE" ]]; then
    echo "[ERROR] Player executable not found under $APP (build with --build)." >&2
    exit 1
fi

OUT_DIR="logs/perf/$(date +%Y%m%d_%H%M%S)"
mkdir -p "$OUT_DIR"
PLAYER_LOG="$OUT_DIR/player.log"
SAMPLES="$OUT_DIR/samples.csv"
SUMMARY="$OUT_DIR/summary.txt"

PLAYER_PID=""
XCTRACE_PID=""
cleanup() {
    if [[ -n "$XCTRACE_PID" ]] && kill -0 "$XCTRACE_PID" 2>/dev/null; then
        kill -INT "$XCTRACE_PID" 2>/dev/null || true
        wait "$XCTRACE_PID" 2>/dev/null || true
    fi
    if [[ -n "$PLAYER_PID" ]] && kill -0 "$PLAYER_PID" 2>/dev/null; then
        kill -TERM "$PLAYER_PID" 2>/dev/null || true
        sleep 1
        kill -KILL "$PLAYER_PID" 2>/dev/null || true
    fi
}
trap cleanup EXIT
trap 'cleanup; exit 130' INT TERM

# shellcheck disable=SC2086
"$EXE" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "$PLAYER_LOG" ${PLAYER_ARGS:-} </dev/null >"$OUT_DIR/player.stdout.log" 2>&1 &
PLAYER_PID=$!
echo "[profile] Launched player pid $PLAYER_PID for ${SECONDS_TO_RUN}s (output: $OUT_DIR)"
sleep 2

TRACE_NOTE="not requested"
if [[ "$USE_XCTRACE" -eq 1 ]]; then
    if command -v xctrace >/dev/null 2>&1 && xcrun --find xctrace >/dev/null 2>&1; then
        xctrace record --template 'Time Profiler' --attach "$PLAYER_PID" --time-limit "${SECONDS_TO_RUN}s" \
            --output "$OUT_DIR/timeprofiler.trace" --no-prompt >"$OUT_DIR/xctrace.log" 2>&1 &
        XCTRACE_PID=$!
        TRACE_NOTE="xctrace Time Profiler (pending)"
    else
        TRACE_NOTE="xctrace unavailable; ps sampling only"
    fi
else
    TRACE_NOTE="skipped (--no-xctrace); ps sampling only"
fi

echo "second,cpu_percent,rss_mb" >"$SAMPLES"
second=0
while (( second < SECONDS_TO_RUN )) && kill -0 "$PLAYER_PID" 2>/dev/null; do
    sleep 1
    second=$((second + 1))
    # ps %cpu on macOS is a decayed average; adequate for coarse trend, Time Profiler is the precise source.
    line="$(ps -o %cpu=,rss= -p "$PLAYER_PID" 2>/dev/null || true)"
    if [[ -n "$line" ]]; then
        echo "$second,$(echo "$line" | awk '{printf "%.1f,%.1f", $1, $2/1024}')" >>"$SAMPLES"
    fi
done

STILL_ALIVE=0
if kill -0 "$PLAYER_PID" 2>/dev/null; then
    STILL_ALIVE=1
fi

if [[ -n "$XCTRACE_PID" ]]; then
    wait "$XCTRACE_PID" 2>/dev/null || true
    XCTRACE_PID=""
    if [[ -d "$OUT_DIR/timeprofiler.trace" ]]; then
        TRACE_NOTE="xctrace Time Profiler -> $OUT_DIR/timeprofiler.trace"
    else
        TRACE_NOTE="xctrace failed (see $OUT_DIR/xctrace.log); ps sampling only"
    fi
fi

cleanup
PLAYER_PID=""

FATAL_REGEX='NullReferenceException|MissingReferenceException|Unhandled [Ee]xception|Crash!!!|Received signal SIG(SEGV|BUS|ABRT|ILL|FPE)'
FATAL_COUNT=0
if [[ -f "$PLAYER_LOG" ]]; then
    FATAL_COUNT="$(grep -Ec "$FATAL_REGEX" "$PLAYER_LOG" || true)"
fi

{
    echo "Player:           $EXE"
    echo "Archs:            $(lipo -archs "$EXE" 2>/dev/null || echo unknown)"
    echo "Requested secs:   $SECONDS_TO_RUN"
    echo "Samples taken:    $(( $(wc -l <"$SAMPLES") - 1 ))"
    echo "Survived window:  $([[ "$STILL_ALIVE" -eq 1 ]] && echo yes || echo no)"
    echo "Trace:            $TRACE_NOTE"
    awk -F, 'NR>1 {n++; cpu+=$2; if ($2>cmax) cmax=$2; if ($3>rmax) rmax=$3; last=$3; if (n==1) first=$3}
        END { if (n>0) printf "CPU avg/max (%%):   %.1f / %.1f\nRSS first/last/max: %.1f / %.1f / %.1f MB\nRSS growth:       %.1f MB\n", cpu/n, cmax, first, last, rmax, last-first; else print "No samples captured." }' "$SAMPLES"
    echo "Log fatal lines:  $FATAL_COUNT"
} | tee "$SUMMARY"

echo "[profile] Summary written to $SUMMARY"
if [[ "$STILL_ALIVE" -ne 1 || "$FATAL_COUNT" -gt 0 ]]; then
    echo "[FAIL] Player exited early or logged fatal exceptions." >&2
    exit 1
fi

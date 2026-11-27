#!/usr/bin/env bash
# Boots the built macOS player windowed, runs the DevAutoplayCapture script, and prints the report + PNG list.
# Usage: ./scripts/capture_gameplay.sh [--build] [outDir]
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

UNITY="${UNITY_BIN:-/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity}"
APP="unity/Builds/MacOS/arm64/MopedGame.app"
TIMEOUT_SECONDS="${CAPTURE_TIMEOUT_SECONDS:-180}"
DO_BUILD=0
OUT_DIR=""

for arg in "$@"; do
    case "$arg" in
        --build) DO_BUILD=1 ;;
        -h|--help) sed -n 2,3p "$0"; exit 0 ;;
        *) OUT_DIR="$arg" ;;
    esac
done
[[ -n "$OUT_DIR" ]] || OUT_DIR="logs/autoplay/$(date +%Y%m%d-%H%M%S)"
mkdir -p logs "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"

if [[ "$DO_BUILD" -eq 1 ]]; then
    echo "[capture] building macOS player..."
    "$UNITY" -batchmode -nographics -projectPath unity -executeMethod MopedSim.Editor.CliBuild.BuildMacOSDev \
        -logFile logs/unity_build.log -quit
fi

EXE_DIR="$APP/Contents/MacOS"
[[ -d "$EXE_DIR" ]] || { echo "[capture] missing $EXE_DIR (run with --build)" >&2; exit 2; }
EXE="$(find "$EXE_DIR" -maxdepth 1 -type f -perm -u+x | head -n 1)"
[[ -n "$EXE" ]] || { echo "[capture] no executable in $EXE_DIR" >&2; exit 2; }

rm -f "$OUT_DIR"/report.json "$OUT_DIR"/*.png
echo "[capture] launching $EXE -> $OUT_DIR"
"$EXE" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 \
    -autoplayCapture "$OUT_DIR" -logFile "$OUT_DIR/player.log" &
PID=$!

START=$SECONDS
TIMED_OUT=0
while kill -0 "$PID" 2>/dev/null; do
    if (( SECONDS - START >= TIMEOUT_SECONDS )); then
        echo "[capture] timeout after ${TIMEOUT_SECONDS}s; killing player" >&2
        kill "$PID" 2>/dev/null || true
        sleep 2
        kill -9 "$PID" 2>/dev/null || true
        TIMED_OUT=1
        break
    fi
    sleep 1
done
set +e
wait "$PID" 2>/dev/null
PLAYER_EXIT=$?
set -e
[[ "$TIMED_OUT" -eq 1 ]] && PLAYER_EXIT=124

echo "[capture] player exit code: $PLAYER_EXIT"
STATUS=0
if [[ -f "$OUT_DIR/report.json" ]]; then
    python3 -I - "$OUT_DIR/report.json" <<'PY'
import json, sys
r = json.load(open(sys.argv[1]))
print("passed=%s totalSeconds=%.1f fps min/avg/max=%.1f/%.1f/%.1f samples=%d" % (
    r["passed"], r["totalSeconds"], r["fpsMin"], r["fpsAvg"], r["fpsMax"], r["frameSamples"]))
for s in r["steps"]:
    print("  [%s] %-28s state=%-14s speed=%.1f rpm=%.0f %s %s" % (
        "PASS" if s["passed"] else "FAIL", s["name"], s.get("state", ""), s["speedMps"], s["rpm"], s["screenshots"], s["note"]))
for e in r["exceptions"]:
    print("  EXCEPTION: " + e[:300])
sys.exit(0 if r["passed"] else 1)
PY
    STATUS=$?
else
    echo "[capture] no report.json was written (see $OUT_DIR/player.log)" >&2
    STATUS=1
fi

echo "[capture] screenshots:"
ls -l "$OUT_DIR"/*.png 2>/dev/null || echo "  (none)"
if [[ "$STATUS" -ne 0 || "$PLAYER_EXIT" -ne 0 ]]; then
    exit 1
fi

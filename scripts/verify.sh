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
                        and macOS arm64 player build). Exits non-zero if Unity 6 LTS is not installed.
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
echo "[PASS] All 5 verification gates passed."

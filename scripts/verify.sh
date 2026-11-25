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
if failed > 0 or result == "Failed":
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

mkdir -p logs

echo ""
echo "=== Gate 3: Unity EditMode Tests ==="
"$UNITY_EDITOR" -batchmode -nographics -projectPath unity \
    -runTests -testPlatform EditMode \
    -testResults logs/editmode.xml \
    -logFile logs/unity_editmode.log -quit
check_nunit_xml logs/editmode.xml "EditMode"

echo ""
echo "=== Gate 4: Unity PlayMode Scenario Tests ==="
"$UNITY_EDITOR" -batchmode -nographics -projectPath unity \
    -runTests -testPlatform PlayMode \
    -testResults logs/playmode.xml \
    -logFile logs/unity_playmode.log -quit
check_nunit_xml logs/playmode.xml "PlayMode"

echo ""
echo "=== Gate 5: macOS arm64 Standalone Dev Build Smoke Check ==="
"$UNITY_EDITOR" -batchmode -nographics -projectPath unity \
    -executeMethod MopedSim.Editor.CliBuild.BuildMacOSDev \
    -logFile logs/unity_build.log -quit

echo ""
echo "[PASS] All 5 verification gates passed."

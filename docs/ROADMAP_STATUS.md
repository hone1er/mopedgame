# ROADMAP_STATUS.md — Host Prerequisites, Milestone Tracker & Verification Log

**Current Phase**: `M0–M5` headless work verified via Gates 1–2; Unity `6000.6.10f1` installed and ready for Gates 3–5 (`./scripts/verify.sh --full`)  
**Last Updated**: `2026-11-25`

---

## 1. Host Environment & Prerequisites Checklist

### Verified Host Baseline (`2026-11-25`)
| Component | Observed Value | Notes |
| :--- | :--- | :--- |
| Machine / Arch | Apple M4 Pro (`arm64`) | Native Apple Silicon required for Unity & Blender |
| Unified Memory | 24 GiB (`25,769,803,776` bytes) | Comfortable for Unity 6 + Blender + Rider/VS Code |
| Free Disk Space | **72 GiB** free / 460 GiB (`df -h /`, re-checked after toolchain installs) | Keep the Unity Editor install to the macOS module only |
| OS / Xcode | macOS `27.2.1` (`26B131`), Xcode `26.3` | Ready for native macOS builds |
| Package Tools | Homebrew `7.0.16`, Python `3.15.2`, Node `v26.13.1`, Git, `gh`, `jq` | Already installed |

### Required Installations
*(Originally all absent on `2026-11-25`; statuses below were re-verified with the listed commands on `2026-11-25`.)*

| Prerequisite | Est. Disk Footprint | Install Command / Source | Verification Command | Status |
| :--- | :--- | :--- | :--- | :--- |
| **.NET SDK** (runs `dotnet test`, builds `netstandard2.1`) | ~0.8 GiB | `brew install --cask dotnet-sdk` | `dotnet --version` → `11.0.100` | `[x] Installed` |
| **Blender** (Apple Silicon) | ~1.2 GiB | `brew install --cask blender` | `/Applications/Blender.app/Contents/MacOS/Blender --version` → `Blender 5.3.0` | `[x] Installed` |
| **Git LFS** (for `.glb`, audio, binary assets) | ~0.05 GiB | `brew install git-lfs && git lfs install` | `git lfs version` → `git-lfs/3.8.1` | `[x] Installed` |
| **Unity Hub** | ~0.5 GiB | `brew install --cask unity-hub` | `ls -d "/Applications/Unity Hub.app"` | `[x] Installed` |
| **Unity 6 Editor (`6000.6.10f1` `arm64`)** | ~11–16 GiB | Unity Hub → Unity `6000.6.10f1` (`Apple Silicon`) + `Mac Build Support` | `ls /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity` → `/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/MacOS/Unity` | `[x] Installed` |

> **Storage Tip**: Install only the **macOS Build Support** module alongside the Unity 6 LTS (`arm64`) Editor; skip Android/iOS/Windows modules to keep room for `Library/` caches and builds.

---

## 2. Milestone Status Tracker (`M0`–`M7`)

Full deliverables, out-of-scope boundaries, and verifiable exit gates for each milestone live in [docs/PROJECT_GUIDE.md](PROJECT_GUIDE.md) Section 7.

| Milestone | Title | Status | Entry Criteria | Required Verification Gates | Target / Notes |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **M0** | **Walking Skeleton & CI Gates** | `Gates 1–2 Complete` / `Gates 3–5 awaiting Unity 6 Editor` | Host prerequisites installed | Gates 1, 2, 3, 5 (`scripts/verify.sh --full`) | `src/MopedSim.Core`, `src/MopedSim.Data`, `tests/`, `tools/validators`, `scripts/verify.sh`, and the `unity/` project + Editor build/scaffold scripts exist; `verify.sh` (Gates 1–2) passes. Gates 3/5 cannot run until the Editor is installed. |
| **M1** | **Headless 2-Stroke Simulation Kernel** | `Complete` (verified via Gates 1–2) | `M0` Gates 1–2 passing | Gate 1 (`dotnet test` soak & property tests), Gate 2 (`validate_data.py`) | Venturi-aware AFR, CHT, premix, plug chop, points heat-soak, pipe curves, assembly DAG, torque/strip/seize/vibration, fault engine, determinism, save migration. |
| **M2** | **Dynamic Moped Riding Prototype** | `Authored — awaiting Unity Editor runtime` | `M1` passing | Gates 1, 2, 3, 4 (PlayMode ride telemetry scenarios) | `MopedSim.Ride` scripts + EditMode/PlayMode tests written and syntax-checked with Roslyn (C# 9.0, 0 diagnostics); not yet compiled or executed in Unity. |
| **M3** | **Interactive Garage & Wrenching Loop** | `Authored — awaiting Unity Editor runtime` | `M1` + `M2` passing | Gates 1, 2, 3, 4 (PlayMode teardown/jet/rebuild scenarios) | `MopedSim.Garage` / `MopedSim.UI` scripts + PlayMode wrenching scenarios written and Roslyn C# 9.0 checked; Core wrenching rules verified headlessly (Gate 1). |
| **M4** | **Breakdown, Roadside Repair & Tow Loop** | `Authored — awaiting Unity Editor runtime` | `M2` + `M3` passing | Gates 1, 2, 3, 4 (Roadside fault recovery PlayMode scenarios) | Tool roll (6 → 10 slots), roadside repairs, Pedal-of-Shame & Buddy Tow verified in Core (Gate 1); Unity PlayMode scenarios authored and Roslyn-checked, not executed. |
| **M5** | **Blender Asset Kit & Audio Sensory Layer** | `Asset kit complete (Gate 2)` / audio driver authored | `M3` passing | Gates 1, 2 (strict `.glb` budget + socket parity), 3, 4 | Procedural headless Blender kit: **51** `.glb` files under `unity/Assets/Art/Models/`, all passing `validate_assets.py`. `TwoStrokeAudioDriver` authored; in-Editor import/audio checks await Unity. |
| **M6** | **SF / Oakland Bay Area World & Economy** | `Not Started` (pending Unity) | `M4` + `M5` passing | Gates 1, 2, 3, 4 (Zone streaming + gig/rally E2E scenarios) | Groundwork only: zone scaffolds, `ZoneStreamManager`, `BayAreaWeatherController` + `KarlTheFogVolume`, Core `TryPurchasePart`. Gigs, rallies, and zone content not started. |
| **M7** | **Polish, Balance, Saves & Release Candidate** | `Not Started` (pending Unity) | `M6` passing | All Gates 1–5 + 60-min soak + fresh-save E2E walkthrough | Save schema v1 + migrator exist (Gate 1); release build blocked on Unity Editor. |

---

## 3. Verification Log

Agents MUST append an entry below whenever a milestone gate or major verification run completes. Never record unexecuted checks.

| Date | Commit SHA | Milestone | Gates Run | Outcome | Observed Evidence Summary |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `2026-11-25` | `29e4b47` (base) | `Pre-M0` | Doc link & git status check | `PASS` | Created `AGENTS.md`, `docs/PROJECT_GUIDE.md`, `docs/ARCHITECTURE_DECISIONS.md`, `docs/ROADMAP_STATUS.md`, updated `README.md`; verified host hardware & zero broken doc links. |
| `2026-11-25` | `98ce0fb` | `M0`–`M5` (cross-lane integration polish) | Gate 1, Gate 2, validator unit tests, Roslyn C# 9.0 syntax check (Gates 3–5 **not run**: no Unity Editor) | `PASS` | `./scripts/verify.sh` exit 0: `MopedSim.Core.Tests` **40/40 passed**, `MopedSim.Data.Tests` **111/111 passed** (0 failed, 0 skipped, 0 build warnings); Gate 2 `80 passed, 0 skipped, 0 warnings, 0 failed`. `python3 -m unittest discover -s tools/validators -p 'test_*.py'`: `Ran 11 tests … OK`. Roslyn 4.8 C# 9.0 check over `unity/Assets/Scripts` + `unity/Assets/Tests`: `files=45 diagnostics=0 nullableTypeAnnotations=0` (3 define sets). No `src/*/bin` or `src/*/obj` (outputs in `artifacts/`). |

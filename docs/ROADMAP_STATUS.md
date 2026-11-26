# ROADMAP_STATUS.md — Host Prerequisites, Milestone Tracker & Verification Log

**Current Phase**: `M0`–`M6` exit outcomes verified via all 5 gates + Gate 5b boot smoke on Unity `6000.6.10f1` (`./scripts/verify.sh --full` exit 0); `M7` next  
**Last Updated**: `2026-11-26`

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
| **M0** | **Walking Skeleton & CI Gates** | `Complete` (Gates 1–5) | Host prerequisites installed | Gates 1, 2, 3, 5 (`scripts/verify.sh --full`) | `verify.sh --full` exit 0 on Unity `6000.6.10f1`: EditMode `CoreCallableFromAppTests` (Core callable from `MopedSim.App`, bit-identical telemetry inside Unity) passes; Gate 5 builds `MopedGame.app` (`lipo -archs` → `arm64`) with 0 project C# warnings/errors. Gate 5 builds the player but does not launch it (no automated boot check yet). |
| **M1** | **Headless 2-Stroke Simulation Kernel** | `Complete` (verified via Gates 1–2) | `M0` Gates 1–2 passing | Gate 1 (`dotnet test` soak & property tests), Gate 2 (`validate_data.py`) | Venturi-aware AFR, CHT, premix, plug chop, points heat-soak, pipe curves, assembly DAG, torque/strip/seize/vibration, fault engine, determinism, save migration. |
| **M2** | **Dynamic Moped Riding Prototype** | `Exit outcomes verified` (Gates 1–4) | `M1` passing | Gates 1, 2, 3, 4 (PlayMode ride telemetry scenarios) | PlayMode `RideScenarioTests` 3/3 (aero tuck ≥ 3 mph, 18% hill pedal-assist + CHT rise, Muni rail shallow-vs-perpendicular slip) and `TelemetryBridgeAllocationTests` 1/1 (zero GC per tick) pass in Unity `6000.6.10f1`. Feel/tuning playtest not yet done. |
| **M3** | **Interactive Garage & Wrenching Loop** | `Exit outcomes verified` (Gates 1–4) | `M1` + `M2` passing | Gates 1, 2, 3, 4 (PlayMode teardown/jet/rebuild scenarios) | PlayMode `GarageWrenchScenarioTests` 3/3 (cylinder DAG blocking + piston inspect, head-nut strip/repair/under-torque leak on dyno, `#56`→`#68` jet updates AFR/RPM/plug chop) pass in Unity. |
| **M4** | **Breakdown, Roadside Repair & Tow Loop** | `Exit outcomes verified` (Gates 1–4) | `M2` + `M3` passing | Gates 1, 2, 3, 4 (Roadside fault recovery PlayMode scenarios) | PlayMode `RoadsideRepairScenarioTests` 3/3 (fouled plug with/without tools, hard seize → tow + scored cylinder persists to Garage) pass in Unity. |
| **M5** | **Blender Asset Kit & Audio Sensory Layer** | `Complete` (Gates 1–5) | `M3` passing | Gates 1, 2 (strict `.glb` budget + socket parity), 3, 4 | **69** committed `.glb` files (51 bike/garage/test + 18 Bay Area World modules) pass `validate_assets.py` (Gate 2 `98 passed`) and import in Unity; EditMode `BikeVisualAssemblerSocketBindingTests` 5/5 (parts mount under parent `SOCKET_*`), `TwoStrokeAudioTests` + PlayMode `AudioTelemetryScenarioTests` 5/5 (deterministic ping / four-stroke blubber / powerband / leak cues, 0 B GC per tick). Outcome 1 (`blender --background … generate_all.py`) was not re-run in the 2026-11-26 verification; the committed exports it produced are what Gate 2 validated. |
| **M6** | **SF / Oakland Bay Area World & Economy** | `Complete` (Gates 1–5) | `M4` + `M5` passing | Gates 1, 2, 3, 4 (Zone streaming + gig/rally E2E scenarios) | PlayMode `BayAreaWorldAndEconomyTests` 4/4 on the real generated scenes + real catalogs: (1) `ZoneTransitionPortal` triggers stream `WestOakland → OaklandHills → BayCrossing → SF_Mission` with real scene content, zero unexpected logs, worst frame < 2 s; (2) cargo gigs refused on the stock wire rack → milk crate bought + mounted → 2-crate `gig_mission_parts_rush` delivered in `Zone_SF_Mission` → cash collected once → `sprocket_front_11t` bought + installed in Garage, save/reload keeps reputation, inventory, decal; (3) `Zone_SF_Mission` `KarlTheFogVolume` raises air density / lowers grip in `BayAreaWeatherController`, `AppCompositionRoot` pushes both into the riding `MopedBikeController`, Core AFR leans vs. a clear-air control; plus 0 B GC per frame for `AppCompositionRoot.Update` with an active gig, job HUD, and rally marker mirror. Courier dispatch / rally mirror live on Bootstrap `AppRoot`. **Known gaps**: Core `RallyPackSimulator.UpdatePackGap` (`Array.Sort`) allocates per tick under Mono (Core-lane handoff), so frames with an active rally are not GC-free; catalog checkpoint coordinates do not yet line up with the scaffold road blockouts (beacons sit at catalog positions); `gig_oakland_burrito_crate` routes West Oakland → Oakland Hills, so the SF delivery outcome uses `gig_mission_parts_rush` (burrito crate covered as the refusal case). |
| **M7** | **Polish, Balance, Saves & Release Candidate** | `Not Started` | `M6` passing | All Gates 1–5 + 60-min soak + fresh-save E2E walkthrough | Save schema v1 + migrator exist (Gate 1); dev player build works (Gate 5). |

---

## 3. Verification Log

Agents MUST append an entry below whenever a milestone gate or major verification run completes. Never record unexecuted checks.

| Date | Commit SHA | Milestone | Gates Run | Outcome | Observed Evidence Summary |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `2026-11-25` | `29e4b47` (base) | `Pre-M0` | Doc link & git status check | `PASS` | Created `AGENTS.md`, `docs/PROJECT_GUIDE.md`, `docs/ARCHITECTURE_DECISIONS.md`, `docs/ROADMAP_STATUS.md`, updated `README.md`; verified host hardware & zero broken doc links. |
| `2026-11-25` | `98ce0fb` | `M0`–`M5` (cross-lane integration polish) | Gate 1, Gate 2, validator unit tests, Roslyn C# 9.0 syntax check (Gates 3–5 **not run**: no Unity Editor) | `PASS` | `./scripts/verify.sh` exit 0: `MopedSim.Core.Tests` **40/40 passed**, `MopedSim.Data.Tests` **111/111 passed** (0 failed, 0 skipped, 0 build warnings); Gate 2 `80 passed, 0 skipped, 0 warnings, 0 failed`. `python3 -m unittest discover -s tools/validators -p 'test_*.py'`: `Ran 11 tests … OK`. Roslyn 4.8 C# 9.0 check over `unity/Assets/Scripts` + `unity/Assets/Tests`: `files=45 diagnostics=0 nullableTypeAnnotations=0` (3 define sets). No `src/*/bin` or `src/*/obj` (outputs in `artifacts/`). |
| `2026-11-25` | `HEAD` (this commit) | `M0`–`M5` (first full Unity run) | Gates 1–5 (`./scripts/verify.sh --full`, Unity `6000.6.10f1`) | `PASS` | Exit 0. Gate 1: `MopedSim.Core.Tests` **40/40**, `MopedSim.Data.Tests` **111/111**. Gate 2: `80 passed, 0 skipped, 0 warnings, 0 failed`. Gate 3: `logs/editmode.xml` result=Passed **15/15**. Gate 4: `logs/playmode.xml` result=Passed **10/10**. Gate 5: `unity/Builds/MacOS/arm64/MopedGame.app` built (`167M`, `lipo -archs` → `arm64`), player not launched. 0 project `warning CS`/`error CS` after forced recompile (`Library/ScriptAssemblies` + `Library/Bee` cleared). Fixes: `-quit` removed from `-runTests` gates (Unity was quitting before tests ran), absolute `-testResults` paths, Unity exit-code + compiler-diagnostic checks, `FindFirstObjectByType`→`FindAnyObjectByType` (CS0618), CLI build no longer rewrites committed scenes. |
| `2026-11-26` | `HEAD` (this commit) | `M5` + `M6` (Bay Area zones, courier/rally/swap meet/paint booth wiring) | Gates 1–5 + Gate 5b (`./scripts/verify.sh --full`, Unity `6000.6.10f1`), validator unit tests | `PASS` | Exit 0 after a forced recompile (`Library/ScriptAssemblies` + `Library/Bee` cleared; 0 project `warning CS`/`error CS` in the EditMode, PlayMode, and build logs). Gate 1: `MopedSim.Core.Tests` **54/54**, `MopedSim.Data.Tests` **122/122**. Gate 2: `98 passed, 0 skipped, 0 warnings, 0 failed`. Gate 3: `logs/editmode.xml` result=Passed **48/48** (10 new: input key-binding parse/apply, save bridge reputation/inventory/customization roundtrip, job board + swap meet views, 18 World modules import + beacon emission remap, generated scene contents). Gate 4: `logs/playmode.xml` result=Passed **19/19** (4 new `BayAreaWorldAndEconomyTests`; `AudioTelemetryScenarioTests` now creates its own `AudioListener`: this run logged "There are no audio listeners in the scene" for that fixture, which `LogAssert.NoUnexpectedReceived` rejects (cause inferred as host audio-device dependent, not re-checked on clean `main`)). Gate 5: `MopedGame.app` built (`173M`, `arm64`). Gate 5b: player alive after 6 s, stopped by gate, **32** boot-window log lines scanned, zero exceptions / crash markers. `python3 -m unittest discover -s tools/validators -p 'test_*.py'`: `Ran 15 tests … OK`. |

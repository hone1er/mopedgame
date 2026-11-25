# ROADMAP_STATUS.md — Host Prerequisites, Milestone Tracker & Verification Log

**Current Phase**: `Pre-M0` (Architecture & Engineering Documentation Complete; Toolchain Installation Pending)  
**Last Updated**: `2026-11-25`

---

## 1. Host Environment & Prerequisites Checklist

### Verified Host Baseline (`2026-11-25`)
| Component | Observed Value | Notes |
| :--- | :--- | :--- |
| Machine / Arch | Apple M4 Pro (`arm64`) | Native Apple Silicon required for Unity & Blender |
| Unified Memory | 24 GiB (`25,769,803,776` bytes) | Comfortable for Unity 6 + Blender + Rider/VS Code |
| Free Disk Space | **54 GiB** free / 460 GiB (`88%` used) | Keep toolchain footprint lean (~13–20 GiB total) |
| OS / Xcode | macOS `27.2.1` (`26B131`), Xcode `26.3` | Ready for native macOS builds |
| Package Tools | Homebrew `7.0.16`, Python `3.15.2`, Node `v26.13.1`, Git, `gh`, `jq` | Already installed |

### Required Installations Before M0 Execution
*(All items below were verified **absent** on `2026-11-25` and must be installed before running Gate 1–5 verification.)*

| Prerequisite | Est. Disk Footprint | Install Command / Source | Verification Command | Status |
| :--- | :--- | :--- | :--- | :--- |
| **.NET 8 SDK** (runs `dotnet test` & builds `netstandard2.1`) | ~0.8 GiB | `brew install --cask dotnet-sdk` (or `dotnet-sdk@8`) | `dotnet --info` | `[ ] Pending` |
| **Blender** (4.2+ LTS / current Apple Silicon) | ~1.2 GiB | `brew install --cask blender` | `/Applications/Blender.app/Contents/MacOS/Blender --version` | `[ ] Pending` |
| **Git LFS** (for `.glb`, audio, binary assets) | ~0.05 GiB | `brew install git-lfs && git lfs install` | `git lfs version` | `[ ] Pending` |
| **Unity Hub + Unity 6 LTS (`arm64` + Mac Build Support)** | ~11–16 GiB | `brew install --cask unity-hub` → install latest Unity 6000.x LTS (`Apple Silicon`) + `Mac Build Support (IL2CPP/Mono)` | `ls /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity` | `[ ] Pending` |

> **Storage Tip**: With 54 GiB currently free, avoid installing Android/iOS/Windows Unity build modules unless explicitly targeted later; install only the **macOS Build Support** module alongside Unity 6 LTS (`arm64`) to keep ~30+ GiB free for Library caches and builds.

---

## 2. Milestone Status Tracker (`M0`–`M7`)

Full deliverables, out-of-scope boundaries, and verifiable exit gates for each milestone live in [docs/PROJECT_GUIDE.md](PROJECT_GUIDE.md) Section 7.

| Milestone | Title | Status | Entry Criteria | Required Verification Gates | Target / Notes |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **M0** | **Walking Skeleton & CI Gates** | `Not Started` | Host prerequisites installed (`dotnet`, `blender`, `git-lfs`, Unity 6 LTS) | Gates 1, 2, 3, 5 (`scripts/verify.sh --full`) | First implementation milestone: `src/MopedSim.Core`, `src/MopedSim.Data`, `unity/` URP project, `tools/blender` smoke cube/chassis, `verify.sh` |
| **M1** | **Headless 2-Stroke Simulation Kernel** | `Not Started` | `M0` passing | Gate 1 (`dotnet test` soak & property tests), Gate 2 (`validate_data.py`) | AFR, CHT, premix, spark plug chop, points heat-soak, expansion pipe curves, assembly DAG, torque/seize/vibration |
| **M2** | **Dynamic Moped Riding Prototype** | `Not Started` | `M1` passing | Gates 1, 2, 3, 4 (PlayMode ride telemetry scenarios) | Lightweight two-wheel physics, aero tuck, pedal-start/assist, drum brake fade, West Oakland test oval |
| **M3** | **Interactive Garage & Wrenching Loop** | `Not Started` | `M1` + `M2` passing | Gates 1, 2, 3, 4 (PlayMode teardown/jet/rebuild scenarios) | Workstand teardown DAG, torque wrench UI, carb jetting, plug chop workbench, center-stand test/dyno rig |
| **M4** | **Breakdown, Roadside Repair & Tow Loop** | `Not Started` | `M2` + `M3` passing | Gates 1, 2, 3, 4 (Roadside fault recovery PlayMode scenarios) | Tool roll loadout, roadside diagnostics, fouled plug/clogged jet/points/master link fixes, Pedal-of-Shame & Buddy Tow |
| **M5** | **Blender Asset Kit & Audio Sensory Layer** | `Not Started` | `M3` passing | Gates 1, 2 (strict `.glb` budget + socket parity), 3, 4 | 3 bike archetypes (`Maxer E50`, `Bramble PA50`, `Targa A35`), upgrade parts, garage props, dynamic 2-stroke audio |
| **M6** | **SF / Oakland Bay Area World & Economy** | `Not Started` | `M4` + `M5` passing | Gates 1, 2, 3, 4 (Zone streaming + gig/rally E2E scenarios) | 4 additive zones (`WestOakland`, `OaklandHills`, `BayCrossing`, `SF_Mission`), Muni tracks, Karl the Fog, gigs, rallies |
| **M7** | **Polish, Balance, Saves & Release Candidate** | `Not Started` | `M6` passing | All Gates 1–5 + 60-min soak + fresh-save E2E walkthrough | Save migrations, accessibility/hints toggle, performance profiling on M4 Pro, final macOS `arm64` build |

---

## 3. Verification Log

Agents MUST append an entry below whenever a milestone gate or major verification run completes. Never record unexecuted checks.

| Date | Commit SHA | Milestone | Gates Run | Outcome | Observed Evidence Summary |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `2026-11-25` | `29e4b47` (base) | `Pre-M0` | Doc link & git status check | `PASS` | Created `AGENTS.md`, `docs/PROJECT_GUIDE.md`, `docs/ARCHITECTURE_DECISIONS.md`, `docs/ROADMAP_STATUS.md`, updated `README.md`; verified host hardware & zero broken doc links. |
| *YYYY-MM-DD* | *sha* | *M0..M7* | *Gates 1..5* | *PASS/FAIL* | *Paste exact test counts, validator summary, build artifact path* |

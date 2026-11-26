<img src="assets/icon.svg" width="64" height="64" alt="">

# MopedGame

A stylized **San Francisco / Oakland Bay Area 2-stroke moped riding, wrenching, and garage simulator** built in **Unity 6 LTS (URP, C#)** with a decoupled pure-.NET simulation kernel (`MopedSim.Core` + `MopedSim.Data`) and a headless **Blender CLI `.glb`** asset pipeline.

Ride lightweight 49cc–70cc pedal mopeds until something rattles loose, overheats, or clogs—diagnose it on the curb with your tool roll (or suffer the Pedal of Shame), tear down top-ends and dial in carb jetting on the garage workstand, and rip across West Oakland flats, Oakland Hills switchbacks, and fog-chilled SF Muni rails.

---

## Getting Started (How to Run the Game)

### 1. Prerequisites
Ensure Git LFS `.glb` assets are pulled and the host toolchain is installed (see [docs/ROADMAP_STATUS.md](docs/ROADMAP_STATUS.md) for macOS install commands):
- **Git LFS** (`git lfs pull`)
- **.NET SDK** (`.NET 8+` / `.NET 11.0`)
- **Python 3** (for static boundary, catalog, and `.glb` validators)
- **Blender** (`5.x+`, only required when regenerating `.glb` models)
- **Unity 6 (`6000.6.10f1`, Apple Silicon `arm64` + Mac Build Support)** installed via **Unity Hub**

### 2. Play in the Unity Editor
1. Open **Unity Hub** → **Add → Add project from disk** → select `~/MopedGame/unity` (or the `unity/` folder in this repository).
2. Open the project in **Unity `6000.6.10f1`** (Unity automatically resolves the local `src/MopedSim.Core` and `src/MopedSim.Data` UPM packages plus URP, Input System, `glTFast`, and `Newtonsoft.Json`).
3. The generated scenes are committed. After changing a scene populator, run **`MopedSim → Scenes → Rebuild All Scaffold Scenes`** (`SceneScaffoldBuilder.BuildAllScenes`). It first makes sure URP is the active pipeline (`MopedSim → Rendering → Ensure URP Pipeline Assets`, ADR-007), then regenerates `Bootstrap.unity`, `Garage.unity`, `UI_Overlay.unity`, and the four Bay Area `Zone_*.unity` scenes, and registers them in `EditorBuildSettings`.
4. Open `unity/Assets/Scenes/Bootstrap.unity` and press **Play** (`⌘P`). You start in the Garage with the assembled `.glb` moped on the workstand. Press **`F1`/`Tab`** to ride that same bike, paint and all, on West Oakland's Maritime straight, and press it again to go back to the workstand.
5. **Asset showcase**: open `unity/Assets/Scenes/AssetGallery.unity` (regenerate it with **`MopedSim → Scenes → Build Asset Gallery Scene`**) and press Play. It lays out all **69** `.glb` models in a labelled grid grouped into **Bike Parts**, **Garage Stations**, **Bay Area World Kit**, and **Test**. Fly with `WASD` (`Q`/`E` down/up, `Shift` for speed) and hold the right mouse button to look around. It is a showcase only and is not part of the player build.

### 3. Build & Launch Standalone macOS (`arm64`) App
- **From Unity Editor**: Run **`MopedSim → Build → macOS arm64 (Dev)`** (`CliBuild.BuildMacOSDev`). The build stages the catalogs plus the bike-part `.glb` models into `StreamingAssets` (git-ignored), so the player assembles and rides the real 3D moped.
- **From Terminal (CLI)**:
  ```bash
  ./scripts/verify.sh --full
  open unity/Builds/MacOS/arm64/MopedGame.app
  ```
  *(If Unity is installed outside `/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity`, set `UNITY_BIN=/path/to/Unity` before running `./scripts/verify.sh --full`.)*

### 4. Controls (`InputAdapter`)

| Action | Keyboard | Gamepad |
| :--- | :--- | :--- |
| **Throttle** | `W` / `Up Arrow` | `Right Trigger` |
| **Steer Left / Right** | `A` / `D` or `Left` / `Right Arrow` | `Left Stick` |
| **Rear Brake** | `S` / `Down Arrow` | `East Button` (`B` / `Circle`) |
| **Front Brake** | `Left Shift` | `Left Trigger` |
| **Pedal-Start / Pedal-Assist** | `Space` | `South Button` (`A` / `Cross`) |
| **Aero Tuck** | `Left Ctrl` / `Z` | `Left Stick Click` |
| **Choke** | `C` | `D-Pad Up` |
| **Decompressor** | `Q` | `Left Shoulder` |
| **Interact (Garage / Roadside)** | `E` | `West Button` (`X` / `Square`) |
| **Toggle Camera** | `V` | `Select / Share` |
| **Garage ↔ Ride** | `F1` / `Tab` | `North Button` (`Y` / `Triangle`) |
| **Settings Menu** | `F2` / `Escape` | `Start / Options` |
| **Parts Shop** | `F3` | — |
| **Cycle Paint Swatch (live on the bike)** | `F4` | — |

The HUD changes with the mode. The **Garage** shows the wrench HUD, plug chop chart, and tool roll. **Ride** shows the speed/RPM/CHT HUD, the job board, and diagnostic hints. **Roadside repair** shows the tool roll, the wrench HUD, and hints. A cheat-sheet banner across the top of the screen lists the hotkeys.

### 5. Headless Tests, Validators & Asset Generation (No Unity Needed)
```bash
# Run Gate 1 (dotnet test on Core + Data) and Gate 2 (boundary, schema, and .glb socket checks)
./scripts/verify.sh

# Regenerate all 69 procedural .glb models (46 bike parts, 4 garage stations, 18 Bay Area world modules, 1 test cube).
# Output is byte-reproducible: two back-to-back runs produce identical SHA-256 hashes for every file.
blender --background --python tools/blender/generate_all.py -- --out unity/Assets/Art/Models
```

---

## Documentation Index

- **[AGENTS.md](AGENTS.md)** — Mandatory engineering rules, repo structure, 5-gate verification commands, and multi-agent coordination lanes. **Read first.**
- **[docs/PROJECT_GUIDE.md](docs/PROJECT_GUIDE.md)** — Master specification covering the 2-stroke simulation model, assembly dependency DAG, fault matrix, riding physics, Bay Area zones, Blender `SOCKET_*` asset contracts, and M0–M7 milestones with verifiable outcomes.
- **[docs/ARCHITECTURE_DECISIONS.md](docs/ARCHITECTURE_DECISIONS.md)** — Settled Architectural Decision Records (`ADR-001`–`ADR-007`), covering engine selection, the engine-agnostic core kernel, asset validation, build-output/catalog staging isolation, and URP activation with runtime part-model staging.
- **[docs/REFERENCES_VINTAGE_MOPEDS.md](docs/REFERENCES_VINTAGE_MOPEDS.md)** — Curated vintage moped visual, proportion, and homage naming reference sheet from `pedalcodearmy.com/stable`.
- **[docs/ROADMAP_STATUS.md](docs/ROADMAP_STATUS.md)** — Host prerequisites checklist, current milestone status, and verification log.

---

## Technology Stack (Settled)

- **Engine**: Unity 6 LTS (URP, `arm64`) — chosen over Unreal Engine 5 for text-native C#/YAML/JSON agent iteration, headless CLI tests, and local Apple Silicon storage footprint (`ADR-001`).
- **Simulation Kernel**: Engine-agnostic `MopedSim.Core` + `MopedSim.Data` targeting `netstandard2.1` / C# 9.0 (`noEngineReferences: true`) with sub-2s `dotnet test` execution (`ADR-002`).
- **3D Assets**: Reproducible headless Blender Python scripts generating `.glb` assets with validated `SOCKET_*` transforms imported via `com.unity.cloud.gltfast` (`ADR-005`).

---

## Current Status & Next Step

**Status**: Pure-C# simulation kernel & data packages (`MopedSim.Core` + `MopedSim.Data`), JSON schemas/catalogs, 51 procedural Blender `.glb` assets, Python static validators, and Unity 6 URP module scripts/tests are on `main` and passing Gates 1–2 (`./scripts/verify.sh`).  
**Next Step**: Install the **Unity 6 LTS Editor (`arm64` + Mac Build Support)** inside Unity Hub (see [docs/ROADMAP_STATUS.md](docs/ROADMAP_STATUS.md)) to open `unity/`, rebuild the scaffold scenes, and run Unity Gates 3–5 (`./scripts/verify.sh --full`).

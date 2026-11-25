# Architecture Decision Records (ADRs) — MopedGame

This log records settled technical decisions for **MopedGame**, their rationale, consequences, and objective conditions under which they should be revisited. All implementation agents must respect accepted ADRs unless a formal superseding ADR is approved.

---

## ADR-001: Engine Choice — Unity 6 LTS (URP, C#) Over Unreal Engine 5

- **Status**: Accepted
- **Date**: 2026-11-25

### Context
MopedGame is a long-term, multi-agent project combining deep 2-stroke mechanical simulation, interactive garage wrenching, dynamic two-wheel riding across stylized SF/Oakland Bay Area zones, and procedural 3D assets. We evaluated Unity 6 LTS (Universal Render Pipeline, C#) vs. Unreal Engine 5 (C++ / Blueprints) against two concrete criteria:
1. **AI-Agent & Automated Testing Ergonomics**: Agents iterate via text diffs, modular refactoring, schema validation, and fast headless test execution.
2. **Verified Host Hardware & Storage Constraints**:
   - Host: Apple M4 Pro (`arm64`), 24 GiB unified memory, macOS `27.2.1`, Xcode `26.3`.
   - Storage: **54 GiB free disk** out of 460 GiB total (`88%` used).

### Decision
Build MopedGame in **Unity 6 LTS** using **URP (Universal Render Pipeline)** and **C#**, paired with a decoupled pure-.NET simulation layer (`ADR-002`).

### Consequences
- **Positive**:
  - **Storage & Compute Fit**: Unity 6 LTS (`arm64` + macOS Build Support) + .NET SDK + Blender fits within ~13–20 GiB, leaving safe headroom inside the host's 54 GiB free disk. Unreal Engine 5 (typically 60–120+ GiB with engine symbols, Derived Data Cache, and Xcode intermediates) cannot comfortably fit without external storage.
  - **Text-Native Agent Workflow**: C# files, `.asmdef` manifests, `.json` catalogs, and YAML prefabs/scenes are inspectable, diffable, and reviewable by agents without opening a GUI editor.
  - **Fast Headless Verification**: Pure C# logic compiles and runs via `dotnet test` in seconds, and Unity supports native headless CLI tests (`-batchmode -nographics -runTests`).
  - **Clean Stylized Bay Area Aesthetic**: URP on Apple Silicon provides responsive frame rates and crisp stylized lighting (coastal fog, golden hour bay reflections, anodized metal/paint shading) without Nanite/Lumen hardware overhead.
- **Negative / Mitigations**:
  - Unity physics (`PhysX` `WheelCollider`) handles four-wheel cars better than two-wheel mopeds out of the box → mitigated by writing a custom lightweight raycast/sphere-cast two-wheel suspension & lean controller in `MopedSim.Ride` driven by `MopedSim.Core` torque/brake outputs.
  - Unity `.unity` scene files are noisy YAML when multiple agents edit the same scene → mitigated by `ADR-004` (additive scenes + code-composed registries).

### Revisit Triggers
- Only revisit if project requirements shift to photorealistic Lumen/Nanite visual fidelity and dedicated >200 GiB workstation storage becomes mandatory.

---

## ADR-002: Headless Engine-Agnostic Simulation Kernel (`MopedSim.Core` & `MopedSim.Data`)

- **Status**: Accepted
- **Date**: 2026-11-25

### Context
MopedGame's core appeal depends on authentic mechanical interactions: 2-stroke air-fuel ratios, cylinder head temperatures (`CHT`), spark plug chops, intake air leaks, premix lubrication, points/condenser heat soak, variator/clutch slip, fastener torque tolerances, and assembly disassembly dependency graphs. If this logic lives inside Unity `MonoBehaviour` classes, testing requires launching Unity (slow, brittle, hard to fuzz), and UI/physics scripts tightly couple to mechanical state.

### Decision
Isolate all simulation rules, state machines, assembly DAGs, fault logic, and catalog/save persistence into two **engine-agnostic local UPM packages**:
1. `src/MopedSim.Core/` (`package.json` + `MopedSim.Core.asmdef` + `MopedSim.Core.csproj`)
2. `src/MopedSim.Data/` (`package.json` + `MopedSim.Data.asmdef` + `MopedSim.Data.csproj`)

Both packages MUST enforce:
- `"noEngineReferences": true` in `.asmdef`.
- `<TargetFramework>netstandard2.1</TargetFramework>` in `.csproj`.
- `<LangVersion>9.0</LangVersion>`, `<Nullable>enable</Nullable>`, and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- Zero dependency on `UnityEngine` or `UnityEditor`.
- Serialization in `MopedSim.Data` uses `Newtonsoft.Json` (matching Unity's official `com.unity.nuget.newtonsoft-json` package).

### Consequences
- **Positive**:
  - Sub-2-second headless unit, property, and 60-minute thermal soak tests via `dotnet test` with zero Unity Editor launch overhead.
  - Pinning `LangVersion 9.0` + `netstandard2.1` prevents the classic trap where code passes `dotnet test` using C# 11/12 syntax and then fails inside Unity's C# compiler.
  - Deterministic simulation (`explicit dt` + `ISimRandom`) enables exact bug reproduction from saved seeds and telemetry logs.
- **Negative / Mitigations**:
  - Requires explicit DTOs (`EngineTelemetrySnapshot`, `RideInputState`, `WrenchActionResult`) at the Core ↔ Unity boundary instead of mutating scene transforms directly → enforced by architecture tests and Gate 2 boundary checks.

### Revisit Triggers
- Revisit `LangVersion` only when Unity bumps its minimum supported C# language version across all CI/local targets.

---

## ADR-003: Data-Driven Part Catalogs & Core-Enforced Assembly Dependency Graph (DAG)

- **Status**: Accepted
- **Date**: 2026-11-25

### Context
A moped simulator grows through content velocity: new cylinders (50cc stock vs. 70cc kitted), carburetors, jet sets, expansion pipes, variator weights, ignition upgrades, chassis styles, and mechanical faults. Hardcoding compatibility or disassembly order in C# classes makes content additions error-prone and breaks clean separation between garage UI and mechanical reality.

### Decision
Represent all parts, bike archetypes, fastener specs, fault definitions, delivery gigs, and Bay Area zone metadata as schema-validated JSON catalogs under `data/catalogs/` paired with JSON Schemas under `data/schemas/`. Model the physical bike as a **Directed Acyclic Graph (`BikeAssemblyGraph`)** in `MopedSim.Core` where every mount slot declares:
- Accepted part categories and mechanical interfaces (e.g., `E50_Case`, `15mm_Spigot`, `2Bolt_ExhaustFlange`).
- Required removal preconditions (`BlockedBy` slots/fasteners — e.g., cylinder cannot slide off studs until all 4 head nuts, exhaust header, and intake manifold are removed).
- Fastener state (`CurrentTorqueNm`, `TargetTorqueNm`, `ToleranceNm`, `StripThresholdNm`, `IsStripped`, `IsSeized`, `VibrationLoosenRate`).
- Orientation / installation checks (e.g., piston exhaust arrow facing exhaust port, ring gap alignment, intake gasket presence).

### Consequences
- **Positive**:
  - Adding a new 70cc kit, reed intake, or pipe requires adding a JSON catalog entry and a validated `.glb` mesh — zero changes to garage wrenching logic.
  - Garage UI simply queries `BikeAssemblyGraph.GetInteractableNodes()` and renders visual highlights for reachable bolts/parts; invalid teardowns are physically impossible and return structured mechanic feedback.
- **Negative / Mitigations**:
  - Cross-reference mistakes in JSON (typos in slot IDs or missing `.glb` paths) could fail at runtime → mitigated by Gate 2 (`tools/validators/validate_data.py` + `MopedSim.Data.Tests`) checking 100% referential integrity on every run.

### Revisit Triggers
- If catalog scale exceeds tens of thousands of entries where JSON parsing affects boot latency, pre-compile catalogs into binary blobs during build while keeping JSON as source of truth.

---

## ADR-004: Modular Additive Scenes & Code-Composed Runtime

- **Status**: Accepted
- **Date**: 2026-11-25

### Context
Unity `.unity` scene files serialize huge YAML object graphs with fragile fileIDs and local GUIDs. When multiple developers or AI agents modify one shared gameplay scene concurrently, YAML merge conflicts almost always corrupt scene references.

### Decision
Partition scenes into small, single-responsibility additive layers and keep scene contents minimal:
- `Bootstrap.unity`: Thin composition root (`AppBootstrap`) loading `MopedSim.Data`, initializing `SimulationWorld`, and managing transitions.
- `Garage.unity`: Workstand, workbench, pegboard, milk-crate storage, premix station, DIY test rig, and interactive camera rigs.
- `UI_Overlay.unity`: HUD, tool roll drawer, plug-chop guide, parts catalog browser, and diagnostic inspector.
- `Zone_WestOakland.unity`, `Zone_OaklandHills.unity`, `Zone_BayCrossing.unity`, `Zone_SF_Mission.unity`: Self-contained Bay Area map chunks loaded additively.

Runtime bike assemblies, props, and interactive colliders are instantiated dynamically from validated `.glb` prefabs and catalog IDs via C# factories rather than hand-placed deep scene hierarchies. Furthermore, `MopedSim.Editor` provides deterministic scene-generation/validation utilities so scenes can be audited or rebuilt cleanly.

### Consequences
- **Positive**:
  - Agents working on Ride, Garage, UI, and individual Bay Area neighborhoods work in disjoint scene and script folders without merge collisions.
  - Zone streaming keeps memory footprint low on 24 GiB Apple Silicon Macs and supports future Bay Area neighborhood expansions cleanly.
- **Negative / Mitigations**:
  - Cross-scene communication cannot rely on drag-and-drop Inspector `UnityEvent` references → solved cleanly via C# service interfaces / typed event channels registered in `MopedSim.App`.

### Revisit Triggers
- If Addressables streaming is needed for post-v1 map expansion beyond the initial 4 Bay Area zones, swap the additive `SceneManager` loader inside `MopedSim.World` behind the existing `IZoneLoader` interface.

---

## ADR-005: Headless Blender CLI → glTF 2.0 (`.glb`) Pipeline with `glTFast` & Python Socket Validation

- **Status**: Accepted
- **Date**: 2026-11-25

### Context
MopedGame needs recognizable mechanical assemblies (frames, forks, E50/variated crankcases, finned cylinders, Dellorto-style carbs, expansion chambers, spoke/mag wheels, milk crates, Bay Area street props) whose parts snap accurately together in the garage and on the road. Hand-modeling unscripted `.blend` binary files makes iteration, scale consistency, and multi-agent verification difficult.

### Decision
Generate all 3D models via version-controlled **Python scripts in `tools/blender/`** executed headlessly (`blender --background --python ...`), exporting binary **glTF 2.0 (`.glb`)** files directly into `unity/Assets/Art/Models/` and importing them into Unity 6 via **`com.unity.cloud.gltfast`**.

Every mechanical part `.glb` MUST satisfy a strict **Socket & Budget Contract**:
- Metric scale (`1.0 unit = 1.0 meter`), applied transforms (`scale = (1, 1, 1)`), Y-up `+Z` forward (`export_yup=True` in Blender glTF exporter).
- Explicit Empty nodes named `SOCKET_<SLOT_ID>` for attachment points (e.g., `SOCKET_ENGINE_MOUNT`, `SOCKET_CYLINDER_BASE`, `SOCKET_CARB_MOUNT`, `SOCKET_EXHAUST_PORT`) and `BOLT_<FASTENER_ID>` for wrench targets.
- Because `.glb` files embed a standard UTF-8 JSON scene graph chunk, a zero-dependency Python script (`tools/validators/validate_assets.py`) parses every `.glb` binary header + JSON chunk headlessly to verify triangle budgets, bounding box scale, UV0 presence, node transforms, and 1:1 `SOCKET_*` parity against `data/catalogs/parts.json` **without launching Blender or Unity**.

### Consequences
- **Positive**:
  - Full asset reproducibility: geometry, UV unwrapping, material slots, and socket coordinates are version-controlled Python diffs.
  - Instant automated Gate 2 QA: any missing socket, flipped axis, non-unit scale, or polycount explosion fails CI in milliseconds.
  - Artists or agents can still load the generated `.blend` intermediates in Blender GUI for visual polish while keeping `.glb` exports deterministic.
- **Negative / Mitigations**:
  - `glTFast` material defaults use standard glTF PBR (`MetallicRoughness`), which needs consistent palette/shader mapping in URP → define a shared stylized URP master material preset and material remapper in `MopedSim.Editor`.

### Revisit Triggers
- If custom skeletal character animations (rider tuck/pedal animations) require `.fbx` for Unity Humanoid retargeting, add an exception strictly for rigged character files while keeping all rigid moped parts, garage props, and Bay Area environment modules on `.glb`.

---

## ADR-006: Generated Build Outputs Live Outside Unity-Imported Source Folders

- **Status**: Accepted
- **Date**: 2026-11-25

### Context
`src/MopedSim.Core` and `src/MopedSim.Data` are both .NET projects (built by Gate 1) and local UPM packages (imported by Unity through `unity/Packages/manifest.json`). Unity imports every file inside a package folder, so default `dotnet build` output (`src/*/bin/**/*.dll`, `src/*/obj/**`) would be imported as precompiled plugins that duplicate the source-compiled assemblies. Separately, standalone players cannot read the repo's `data/catalogs/`, yet catalogs must stay single-sourced there.

### Decision
- `src/*/*.csproj` set `BaseOutputPath` / `BaseIntermediateOutputPath` to `<repo>/artifacts/bin|obj/$(MSBuildProjectName)/`. The SDK is imported explicitly (`Sdk.props` / `Sdk.targets`) so the intermediate path is set before restore reads it. `artifacts/` is git-ignored, and `validate_boundaries.py` (Gate 2) fails on any `.dll` / `.pdb` / `.mdb` inside a pure-C# UPM package.
- `MopedSim.Editor.CliBuild` copies `data/catalogs/*.json` into `unity/Assets/StreamingAssets/Catalogs/` immediately before `BuildPlayer`, validates the staged copy through `MopedSim.Data`, and refreshes the AssetDatabase. That folder is generated and git-ignored; `data/catalogs/` remains the only source of truth.

### Consequences
- **Positive**: Unity never sees stray build binaries; Gate 1 and Unity can share one source tree. Player builds always ship the exact validated catalogs from the commit being built.
- **Negative / Mitigations**: The `.csproj` files use explicit SDK imports instead of `<Project Sdk="...">`. A developer with pre-ADR `src/*/bin|obj` folders must delete them once; Gate 2 reports them.

### Revisit Triggers
- If MopedSim.Core/Data ever ship as precompiled DLL packages instead of source UPM packages, or if catalogs move to Addressables/remote content.


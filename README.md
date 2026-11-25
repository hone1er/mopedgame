<img src="assets/icon.svg" width="64" height="64" alt="">

# MopedGame

A stylized **San Francisco / Oakland Bay Area 2-stroke moped riding, wrenching, and garage simulator** built in **Unity 6 LTS (URP, C#)** with a decoupled pure-.NET simulation kernel (`MopedSim.Core`) and a headless **Blender CLI `.glb`** asset pipeline.

Ride lightweight 49cc–70cc pedal mopeds until something rattles loose, overheats, or clogs—diagnose it on the curb with your tool roll (or suffer the Pedal of Shame), tear down top-ends and dial in carb jetting on the garage workstand, and rip across West Oakland flats, Oakland Hills switchbacks, and fog-chilled SF Muni rails.

---

## Documentation Index

- **[AGENTS.md](AGENTS.md)** — Mandatory engineering rules, repo structure, 5-gate verification commands, and multi-agent coordination lanes. **Read first.**
- **[docs/PROJECT_GUIDE.md](docs/PROJECT_GUIDE.md)** — Master specification covering the 2-stroke simulation model, assembly dependency DAG, fault matrix, riding physics, Bay Area zones, Blender `SOCKET_*` asset contracts, and M0–M7 milestones with verifiable outcomes.
- **[docs/ARCHITECTURE_DECISIONS.md](docs/ARCHITECTURE_DECISIONS.md)** — Settled Architectural Decision Records (`ADR-001`–`ADR-006`), including engine selection, engine-agnostic core kernel, asset validation, and build-output isolation.
- **[docs/ROADMAP_STATUS.md](docs/ROADMAP_STATUS.md)** — Host prerequisites checklist, current milestone status (`Pre-M0`), and verification log.

---

## Technology Stack (Settled)

- **Engine**: Unity 6 LTS (URP, `arm64`) — chosen over Unreal Engine 5 for text-native C#/YAML/JSON agent iteration, headless CLI tests, and local Apple Silicon storage footprint (`ADR-001`).
- **Simulation Kernel**: Engine-agnostic `MopedSim.Core` + `MopedSim.Data` targeting `netstandard2.1` / C# 9.0 (`noEngineReferences: true`) with sub-2s `dotnet test` execution (`ADR-002`).
- **3D Assets**: Reproducible headless Blender Python scripts generating `.glb` assets with validated `SOCKET_*` transforms imported via `com.unity.cloud.gltfast` (`ADR-005`).

---

## Current Status & Next Step

**Status**: `Pre-M0` (Architecture & project documentation complete).  
**Next Step**: Install host prerequisites listed in [docs/ROADMAP_STATUS.md](docs/ROADMAP_STATUS.md) (`.NET SDK`, `Blender`, `git-lfs`, and `Unity 6 LTS arm64`), then execute **Milestone M0 (Walking Skeleton & CI Gates)**.

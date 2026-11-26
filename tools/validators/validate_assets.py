#!/usr/bin/env python3
"""Zero-dependency binary glTF 2.0 (.glb) validator for MopedGame.

Enforces PROJECT_GUIDE.md §5.1-5.4 & ADR-005:
- Parses glTF 2.0 binary header ('glTF', version 2, length) & JSON/BIN chunks.
- Checks every .glb referenced in data/catalogs/parts.json (when meshAssetPath is set),
  plus unity/Assets/Art/Models/Test/smoke_test_cube.glb (SOCKET_TEST_ANCHOR),
  plus Models/Garage/*.glb, Bay Area world modules under Models/World/**/*.glb,
  and any other .glb under unity/Assets/Art/Models/.
- Verifies non-empty mesh primitives, triangle count <= category budget
  (Small <= 1500, Major <= 6500, Garage <= 4000, World module <= 3500),
  TEXCOORD_0 (UV0) on every primitive, unit node scale [1, 1, 1] (or default),
  metric bounding box < 25m per axis (< 30m for World modules: 20m road tiles,
  warehouse blocks, bridge towers), and 1:1 presence of all requiredSockets /
  BOLT_* nodes in the active glTF scene.
"""

import argparse
import json
import math
import struct
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Set, Tuple


GLB_MAGIC = b"glTF"
CHUNK_TYPE_JSON = 0x4E4F534A  # "JSON"
CHUNK_TYPE_BIN = 0x004E4942   # "BIN\0"

BUDGET_SMALL = 1500
BUDGET_MAJOR = 6500
BUDGET_GARAGE = 4000
BUDGET_WORLD = 3500
MAX_PART_EXTENT_METERS = 25.0
MAX_WORLD_EXTENT_METERS = 30.0

MAJOR_CATEGORIES = {
    "Frame",
    "Fork",
    "Crankcase",
    "EngineCase",
    "Cylinder",
    "CylinderHead",
    "Exhaust",
    "Wheel",
}

SMALL_CATEGORIES = {
    "Piston",
    "SparkPlug",
    "Intake",
    "Carburetor",
    "AirFilter",
    "FuelTank",
    "Ignition",
    "Drivetrain",
    "Clutch",
    "Variator",
    "Gearbox",
    "Shock",
    "Shocks",
    "Suspension",
    "Handlebars",
    "Seat",
    "Headlight",
    "Light",
    "CargoRack",
    "Rack",
    "Jet",
    "MainJet",
    "PilotJet",
    "Gasket",
    "Tool",
    "Spare",
    "Test",
}

CANONICAL_FALLBACK_SOCKETS: Dict[str, Tuple[int, List[str]]] = {
    "Models/Test/smoke_test_cube.glb": (BUDGET_SMALL, ["SOCKET_TEST_ANCHOR"]),
    "Models/Garage/garage_workstand.glb": (BUDGET_GARAGE, ["SOCKET_BIKE_MOUNT"]),
    "Models/Garage/garage_dyno_roller.glb": (BUDGET_GARAGE, ["SOCKET_BIKE_MOUNT"]),
    "Models/Garage/garage_workbench.glb": (BUDGET_GARAGE, ["SOCKET_WORK_SURFACE"]),
    "Models/Garage/garage_premix_can.glb": (BUDGET_GARAGE, ["SOCKET_POUR_SPOUT"]),
}

CATALOG_COLLECTION_KEYS = ("parts", "items", "entries", "data")


@dataclass
class ValidationReport:
    errors: List[str] = field(default_factory=list)
    warnings: List[str] = field(default_factory=list)
    passes: List[str] = field(default_factory=list)
    skips: List[str] = field(default_factory=list)

    def error(self, msg: str) -> None:
        self.errors.append(msg)

    def warn(self, msg: str) -> None:
        self.warnings.append(msg)

    def passed(self, msg: str) -> None:
        self.passes.append(msg)

    def skip(self, msg: str) -> None:
        self.skips.append(msg)


def _mat4_identity() -> List[float]:
    return [
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0,
    ]


def _mat4_mul(a: Sequence[float], b: Sequence[float]) -> List[float]:
    """Multiply two column-major 4x4 matrices."""
    out = [0.0] * 16
    for col in range(4):
        for row in range(4):
            out[col * 4 + row] = (
                a[0 * 4 + row] * b[col * 4 + 0]
                + a[1 * 4 + row] * b[col * 4 + 1]
                + a[2 * 4 + row] * b[col * 4 + 2]
                + a[3 * 4 + row] * b[col * 4 + 3]
            )
    return out


def _trs_to_mat4(
    t: Sequence[float], r: Sequence[float], s: Sequence[float]
) -> List[float]:
    qx, qy, qz, qw = float(r[0]), float(r[1]), float(r[2]), float(r[3])
    xx, yy, zz = qx * qx, qy * qy, qz * qz
    xy, xz, yz = qx * qy, qx * qz, qy * qz
    wx, wy, wz = qw * qx, qw * qy, qw * qz
    sx, sy, sz = float(s[0]), float(s[1]), float(s[2])
    return [
        (1.0 - 2.0 * (yy + zz)) * sx,
        (2.0 * (xy + wz)) * sx,
        (2.0 * (xz - wy)) * sx,
        0.0,
        (2.0 * (xy - wz)) * sy,
        (1.0 - 2.0 * (xx + zz)) * sy,
        (2.0 * (yz + wx)) * sy,
        0.0,
        (2.0 * (xz + wy)) * sz,
        (2.0 * (yz - wx)) * sz,
        (1.0 - 2.0 * (xx + yy)) * sz,
        0.0,
        float(t[0]),
        float(t[1]),
        float(t[2]),
        1.0,
    ]


def _transform_point(m: Sequence[float], p: Sequence[float]) -> Tuple[float, float, float]:
    x, y, z = float(p[0]), float(p[1]), float(p[2])
    return (
        m[0] * x + m[4] * y + m[8] * z + m[12],
        m[1] * x + m[5] * y + m[9] * z + m[13],
        m[2] * x + m[6] * y + m[10] * z + m[14],
    )


def _is_finite_number(val: object) -> bool:
    return isinstance(val, (int, float)) and not isinstance(val, bool) and math.isfinite(float(val))


def parse_glb(glb_path: Path) -> Tuple[dict, Optional[int]]:
    """Parse a binary glTF 2.0 (.glb) file and return (json_dict, bin_chunk_length)."""
    data = glb_path.read_bytes()
    if len(data) < 12:
        raise ValueError(f"file too small ({len(data)} bytes) for glTF 2.0 header")

    magic, version, total_length = struct.unpack_from("<4sII", data, 0)
    if magic != GLB_MAGIC:
        raise ValueError(f"invalid magic {magic!r}, expected b'glTF'")
    if version != 2:
        raise ValueError(f"unsupported glTF version {version}, expected 2")
    if total_length != len(data):
        raise ValueError(f"header length {total_length} != actual file size {len(data)}")

    offset = 12
    gltf_json: Optional[dict] = None
    bin_chunk_len: Optional[int] = None
    chunk_index = 0

    while offset < total_length:
        if offset + 8 > total_length:
            raise ValueError(f"truncated chunk header at offset {offset}")
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        if chunk_len % 4 != 0:
            raise ValueError(f"chunk {chunk_index} length {chunk_len} is not 4-byte aligned")
        payload_start = offset + 8
        payload_end = payload_start + chunk_len
        if payload_end > total_length:
            raise ValueError(f"chunk {chunk_index} overruns file length ({payload_end} > {total_length})")

        payload = data[payload_start:payload_end]
        if chunk_index == 0:
            if chunk_type != CHUNK_TYPE_JSON:
                raise ValueError(f"first chunk must be JSON (0x{CHUNK_TYPE_JSON:08X}), got 0x{chunk_type:08X}")
            try:
                parsed = json.loads(payload.decode("utf-8"))
            except Exception as exc:
                raise ValueError(f"invalid UTF-8/JSON in glTF JSON chunk: {exc}") from exc
            if not isinstance(parsed, dict):
                raise ValueError("glTF JSON chunk root must be an object")
            gltf_json = parsed
        elif chunk_type == CHUNK_TYPE_BIN:
            if bin_chunk_len is not None:
                raise ValueError("multiple BIN chunks found in .glb")
            bin_chunk_len = chunk_len

        offset = payload_end
        chunk_index += 1

    if gltf_json is None:
        raise ValueError("missing JSON chunk")

    buffers = gltf_json.get("buffers", [])
    if isinstance(buffers, list) and buffers:
        buf0 = buffers[0]
        if isinstance(buf0, dict) and "uri" not in buf0:
            declared = buf0.get("byteLength", 0)
            if bin_chunk_len is None:
                raise ValueError("buffers[0] has no uri but .glb has no BIN chunk")
            if not isinstance(declared, int) or declared > bin_chunk_len:
                raise ValueError(
                    f"buffers[0].byteLength ({declared}) exceeds BIN chunk size ({bin_chunk_len})"
                )

    return gltf_json, bin_chunk_len


def _node_local_matrix_and_scale_check(
    node: dict, node_idx: int, label: str, errors: List[str]
) -> List[float]:
    if "matrix" in node:
        mat = node["matrix"]
        if not isinstance(mat, list) or len(mat) != 16 or not all(_is_finite_number(v) for v in mat):
            errors.append(f"{label}: node[{node_idx}] has invalid 'matrix'")
            return _mat4_identity()
        fmat = [float(v) for v in mat]
        for axis_name, col in (("X", 0), ("Y", 1), ("Z", 2)):
            mag = math.sqrt(
                fmat[col * 4 + 0] ** 2 + fmat[col * 4 + 1] ** 2 + fmat[col * 4 + 2] ** 2
            )
            if abs(mag - 1.0) > 1e-4:
                errors.append(
                    f"{label}: node[{node_idx}] ({node.get('name', '<unnamed>')}) "
                    f"matrix {axis_name}-scale {mag:.5f} != 1.0"
                )
        return fmat

    scale = node.get("scale", [1.0, 1.0, 1.0])
    if not isinstance(scale, list) or len(scale) != 3 or not all(_is_finite_number(v) for v in scale):
        errors.append(f"{label}: node[{node_idx}] has invalid 'scale' {scale!r}")
        scale = [1.0, 1.0, 1.0]
    else:
        if any(abs(float(s) - 1.0) > 1e-4 for s in scale):
            errors.append(
                f"{label}: node[{node_idx}] ({node.get('name', '<unnamed>')}) "
                f"non-unit scale {scale!r}; expected [1, 1, 1]"
            )

    translation = node.get("translation", [0.0, 0.0, 0.0])
    if (
        not isinstance(translation, list)
        or len(translation) != 3
        or not all(_is_finite_number(v) for v in translation)
    ):
        errors.append(f"{label}: node[{node_idx}] has invalid 'translation' {translation!r}")
        translation = [0.0, 0.0, 0.0]

    rotation = node.get("rotation", [0.0, 0.0, 0.0, 1.0])
    if (
        not isinstance(rotation, list)
        or len(rotation) != 4
        or not all(_is_finite_number(v) for v in rotation)
    ):
        errors.append(f"{label}: node[{node_idx}] has invalid 'rotation' {rotation!r}")
        rotation = [0.0, 0.0, 0.0, 1.0]

    return _trs_to_mat4(translation, rotation, scale)


def inspect_glb_contract(
    glb_path: Path,
    *,
    rel_label: str,
    max_tris: int,
    required_nodes: Sequence[str],
    max_extent_m: float = MAX_PART_EXTENT_METERS,
) -> Tuple[List[str], int, Set[str]]:
    """Validate one .glb file against glTF 2.0, budget, UV0, scale, bbox, and socket contracts."""
    errors: List[str] = []
    try:
        gltf, _ = parse_glb(glb_path)
    except Exception as exc:
        return [f"{rel_label}: invalid .glb ({exc})"], 0, set()

    asset = gltf.get("asset", {})
    if not isinstance(asset, dict) or not str(asset.get("version", "")).startswith("2"):
        errors.append(f"{rel_label}: gltf.asset.version must be '2.0', got {asset!r}")

    nodes = gltf.get("nodes", [])
    meshes = gltf.get("meshes", [])
    accessors = gltf.get("accessors", [])
    scenes = gltf.get("scenes", [])

    if not isinstance(nodes, list):
        return [f"{rel_label}: 'nodes' must be a list"], 0, set()
    if not isinstance(meshes, list) or not meshes:
        return [f"{rel_label}: missing or empty 'meshes' list"], 0, set()
    if not isinstance(accessors, list):
        return [f"{rel_label}: 'accessors' must be a list"], 0, set()
    if not isinstance(scenes, list) or not scenes:
        return [f"{rel_label}: missing or empty 'scenes' list"], 0, set()

    scene_idx = gltf.get("scene", 0)
    if not isinstance(scene_idx, int) or scene_idx < 0 or scene_idx >= len(scenes):
        return [f"{rel_label}: invalid default scene index {scene_idx!r}"], 0, set()

    # Check unit scale on all declared nodes
    local_mats: List[List[float]] = []
    for idx, node in enumerate(nodes):
        if not isinstance(node, dict):
            errors.append(f"{rel_label}: node[{idx}] is not an object")
            local_mats.append(_mat4_identity())
            continue
        local_mats.append(_node_local_matrix_and_scale_check(node, idx, rel_label, errors))

    # Validate every mesh primitive structure and UV0 presence
    for m_idx, mesh in enumerate(meshes):
        if not isinstance(mesh, dict):
            errors.append(f"{rel_label}: mesh[{m_idx}] is not an object")
            continue
        prims = mesh.get("primitives")
        if not isinstance(prims, list) or not prims:
            errors.append(f"{rel_label}: mesh[{m_idx}] has no primitives")
            continue
        for p_idx, prim in enumerate(prims):
            if not isinstance(prim, dict):
                errors.append(f"{rel_label}: mesh[{m_idx}].primitives[{p_idx}] is not an object")
                continue
            attrs = prim.get("attributes")
            if not isinstance(attrs, dict) or "POSITION" not in attrs:
                errors.append(f"{rel_label}: mesh[{m_idx}].primitives[{p_idx}] missing POSITION attribute")
                continue
            if "TEXCOORD_0" not in attrs:
                errors.append(f"{rel_label}: mesh[{m_idx}].primitives[{p_idx}] missing TEXCOORD_0 (UV0)")

    # Traverse active scene nodes to accumulate triangle count, world AABB, and reachable node names
    root_nodes = scenes[scene_idx].get("nodes", [])
    if not isinstance(root_nodes, list) or not root_nodes:
        errors.append(f"{rel_label}: active scene[{scene_idx}] has no root nodes")
        return errors, 0, set()

    reachable_names: Set[str] = set()
    total_tris = 0
    mesh_node_count = 0
    world_min = [math.inf, math.inf, math.inf]
    world_max = [-math.inf, -math.inf, -math.inf]
    has_bbox = False
    visiting: Set[int] = set()

    def visit(node_idx: int, parent_mat: List[float]) -> None:
        nonlocal total_tris, mesh_node_count, has_bbox
        if not isinstance(node_idx, int) or node_idx < 0 or node_idx >= len(nodes):
            errors.append(f"{rel_label}: invalid child node index {node_idx!r}")
            return
        if node_idx in visiting:
            errors.append(f"{rel_label}: cycle detected in glTF node hierarchy at node[{node_idx}]")
            return

        visiting.add(node_idx)
        node = nodes[node_idx]
        if not isinstance(node, dict):
            visiting.remove(node_idx)
            return

        name = node.get("name")
        if isinstance(name, str) and name:
            reachable_names.add(name)

        world_mat = _mat4_mul(parent_mat, local_mats[node_idx])

        if "mesh" in node:
            mesh_idx = node["mesh"]
            if not isinstance(mesh_idx, int) or mesh_idx < 0 or mesh_idx >= len(meshes):
                errors.append(f"{rel_label}: node[{node_idx}] references invalid mesh {mesh_idx!r}")
            else:
                mesh_node_count += 1
                prims = meshes[mesh_idx].get("primitives", [])
                if isinstance(prims, list):
                    for p_idx, prim in enumerate(prims):
                        if not isinstance(prim, dict):
                            continue
                        attrs = prim.get("attributes", {})
                        if not isinstance(attrs, dict) or "POSITION" not in attrs:
                            continue
                        pos_acc_idx = attrs["POSITION"]
                        if not isinstance(pos_acc_idx, int) or pos_acc_idx < 0 or pos_acc_idx >= len(accessors):
                            errors.append(
                                f"{rel_label}: mesh[{mesh_idx}].primitives[{p_idx}] invalid POSITION accessor {pos_acc_idx!r}"
                            )
                            continue
                        pos_acc = accessors[pos_acc_idx]
                        if not isinstance(pos_acc, dict) or pos_acc.get("type") != "VEC3":
                            errors.append(
                                f"{rel_label}: mesh[{mesh_idx}].primitives[{p_idx}] POSITION accessor must be VEC3"
                            )
                            continue

                        # Triangle count
                        mode = prim.get("mode", 4)
                        if "indices" in prim:
                            idx_acc_i = prim["indices"]
                            if (
                                not isinstance(idx_acc_i, int)
                                or idx_acc_i < 0
                                or idx_acc_i >= len(accessors)
                                or not isinstance(accessors[idx_acc_i], dict)
                            ):
                                errors.append(
                                    f"{rel_label}: mesh[{mesh_idx}].primitives[{p_idx}] invalid indices accessor"
                                )
                                count = 0
                            else:
                                count = int(accessors[idx_acc_i].get("count", 0))
                        else:
                            count = int(pos_acc.get("count", 0))

                        if mode == 4:
                            if count <= 0 or count % 3 != 0:
                                errors.append(
                                    f"{rel_label}: mesh[{mesh_idx}].primitives[{p_idx}] TRIANGLES count {count} invalid"
                                )
                            else:
                                total_tris += count // 3
                        elif mode in (5, 6):
                            if count < 3:
                                errors.append(
                                    f"{rel_label}: mesh[{mesh_idx}].primitives[{p_idx}] strip/fan count {count} < 3"
                                )
                            else:
                                total_tris += count - 2
                        else:
                            errors.append(
                                f"{rel_label}: mesh[{mesh_idx}].primitives[{p_idx}] unsupported primitive mode {mode}"
                            )

                        # Bounding box sanity
                        pmin = pos_acc.get("min")
                        pmax = pos_acc.get("max")
                        if (
                            not isinstance(pmin, list)
                            or not isinstance(pmax, list)
                            or len(pmin) != 3
                            or len(pmax) != 3
                            or not all(_is_finite_number(v) for v in pmin + pmax)
                        ):
                            errors.append(
                                f"{rel_label}: mesh[{mesh_idx}].primitives[{p_idx}] POSITION accessor missing valid min/max"
                            )
                        else:
                            if any(float(pmin[i]) > float(pmax[i]) for i in range(3)):
                                errors.append(
                                    f"{rel_label}: mesh[{mesh_idx}].primitives[{p_idx}] POSITION accessor min > max"
                                )
                            else:
                                has_bbox = True
                                for cx in (pmin[0], pmax[0]):
                                    for cy in (pmin[1], pmax[1]):
                                        for cz in (pmin[2], pmax[2]):
                                            wx, wy, wz = _transform_point(world_mat, (cx, cy, cz))
                                            world_min[0] = min(world_min[0], wx)
                                            world_min[1] = min(world_min[1], wy)
                                            world_min[2] = min(world_min[2], wz)
                                            world_max[0] = max(world_max[0], wx)
                                            world_max[1] = max(world_max[1], wy)
                                            world_max[2] = max(world_max[2], wz)

        children = node.get("children", [])
        if isinstance(children, list):
            for child_idx in children:
                visit(child_idx, world_mat)

        visiting.remove(node_idx)

    for root_idx in root_nodes:
        visit(root_idx, _mat4_identity())

    if mesh_node_count == 0 or total_tris <= 0:
        errors.append(f"{rel_label}: active scene contains no non-empty triangle meshes (tris={total_tris})")
    elif total_tris > max_tris:
        errors.append(f"{rel_label}: triangle count {total_tris} exceeds budget {max_tris}")

    if not has_bbox:
        errors.append(f"{rel_label}: could not determine bounding box from POSITION accessors")
    else:
        extents = [world_max[i] - world_min[i] for i in range(3)]
        if max(extents) <= 1e-4:
            errors.append(f"{rel_label}: degenerate bounding box extents {extents!r}")
        for axis, span, lo, hi in zip(("X", "Y", "Z"), extents, world_min, world_max):
            if span >= max_extent_m or max(abs(lo), abs(hi)) >= max_extent_m:
                errors.append(
                    f"{rel_label}: metric bounding box {axis}-axis [{lo:.3f}, {hi:.3f}] (span {span:.3f}m) "
                    f"exceeds {max_extent_m}m limit"
                )

    for req_node in required_nodes:
        if req_node not in reachable_names:
            errors.append(
                f"{rel_label}: missing required node '{req_node}' in active glTF scene "
                f"(found nodes: {sorted(reachable_names)})"
            )

    return errors, total_tris, reachable_names


def _extract_catalog_entries(raw: object) -> Optional[List[dict]]:
    if isinstance(raw, list):
        return [item for item in raw if isinstance(item, dict)]
    if isinstance(raw, dict):
        for key in CATALOG_COLLECTION_KEYS:
            val = raw.get(key)
            if isinstance(val, list):
                return [item for item in val if isinstance(item, dict)]
    return None


def is_world_asset(rel_mesh_path: str) -> bool:
    """True for Bay Area modular world kit assets under Models/World/."""
    norm = rel_mesh_path.replace("\\", "/")
    return "/World/" in norm or norm.startswith("Models/World/") or norm.startswith("World/")


def infer_extent_for_asset(rel_mesh_path: str) -> float:
    """Resolve the metric bounding-box limit (meters per axis) for an asset path."""
    return MAX_WORLD_EXTENT_METERS if is_world_asset(rel_mesh_path) else MAX_PART_EXTENT_METERS


def infer_budget_for_asset(rel_mesh_path: str, part: Optional[dict] = None) -> int:
    """Resolve triangle budget from explicit part metadata, category, or canonical path."""
    if isinstance(part, dict):
        explicit_max = part.get("maxTriangles")
        if isinstance(explicit_max, int) and not isinstance(explicit_max, bool) and explicit_max > 0:
            return explicit_max
        asset_class = str(part.get("assetClass") or part.get("budgetClass") or "").strip().lower()
        if asset_class == "small":
            return BUDGET_SMALL
        if asset_class == "major":
            return BUDGET_MAJOR
        if asset_class == "garage":
            return BUDGET_GARAGE
        if asset_class == "world":
            return BUDGET_WORLD

    norm = rel_mesh_path.replace("\\", "/")
    if norm in CANONICAL_FALLBACK_SOCKETS:
        return CANONICAL_FALLBACK_SOCKETS[norm][0]
    if is_world_asset(norm):
        return BUDGET_WORLD
    if "/Garage/" in norm or norm.startswith("Models/Garage/"):
        return BUDGET_GARAGE
    if "/Test/" in norm or norm.startswith("Models/Test/"):
        return BUDGET_SMALL

    if isinstance(part, dict):
        category = str(part.get("category", "")).strip()
        if category in MAJOR_CATEGORIES:
            return BUDGET_MAJOR
        if category in SMALL_CATEGORIES:
            return BUDGET_SMALL

    fname = Path(norm).name
    if any(
        fname.startswith(prefix)
        for prefix in ("frame_", "fork_", "case_", "cyl_", "head_", "pipe_", "wheel_")
    ):
        return BUDGET_MAJOR
    if fname.startswith("garage_"):
        return BUDGET_GARAGE
    return BUDGET_SMALL


def _normalize_mesh_rel_path(raw_path: str) -> str:
    norm = raw_path.strip().replace("\\", "/")
    for prefix in ("unity/Assets/Art/", "Assets/Art/"):
        if norm.startswith(prefix):
            norm = norm[len(prefix):]
    return norm.lstrip("/")


def run_validation(
    repo_root: Path,
    *,
    strict: bool = False,
    skip_missing_unity: bool = False,
    allow_partial: bool = False,
) -> ValidationReport:
    report = ValidationReport()
    art_root = repo_root / "unity" / "Assets" / "Art"
    models_root = art_root / "Models"
    parts_catalog_path = repo_root / "data" / "catalogs" / "parts.json"

    unity_assets_present = models_root.exists()
    parts_present = parts_catalog_path.is_file()

    if not unity_assets_present:
        if skip_missing_unity or allow_partial or not strict:
            report.skip(f"{models_root}: not present yet; skipping .glb asset inspection")
            return report
        report.error(f"{models_root}: missing Unity 3D models directory")
        return report

    # Collect expectations per normalized relative path (under unity/Assets/Art/)
    expected_specs: Dict[str, Tuple[int, Set[str], List[str]]] = {}

    def register_expectation(rel_path: str, budget: int, sockets: Sequence[str], source: str) -> None:
        if rel_path not in expected_specs:
            expected_specs[rel_path] = (budget, set(sockets), [source])
        else:
            prev_budget, prev_sockets, prev_sources = expected_specs[rel_path]
            prev_sockets.update(sockets)
            prev_sources.append(source)
            expected_specs[rel_path] = (max(prev_budget, budget), prev_sockets, prev_sources)

    for rel_path, (budget, sockets) in CANONICAL_FALLBACK_SOCKETS.items():
        if rel_path == "Models/Test/smoke_test_cube.glb" or (art_root / rel_path).is_file() or strict:
            register_expectation(rel_path, budget, sockets, "canonical")

    if parts_present:
        try:
            raw_parts = json.loads(parts_catalog_path.read_text(encoding="utf-8"))
        except Exception as exc:
            report.error(f"{parts_catalog_path}: failed to parse JSON ({exc})")
            raw_parts = None
        entries = _extract_catalog_entries(raw_parts)
        if entries is None:
            if raw_parts is not None:
                report.error(f"{parts_catalog_path}: could not locate parts list")
        else:
            for idx, part in enumerate(entries):
                part_id = str(part.get("id", f"<index {idx}>"))
                has_3d = part.get("has3dMesh", True)
                mesh_rel = part.get("meshAssetPath")
                if has_3d is False:
                    if mesh_rel is not None and str(mesh_rel).strip() != "":
                        report.error(
                            f"{parts_catalog_path}: part '{part_id}' has has3dMesh=false "
                            f"but non-empty meshAssetPath '{mesh_rel}'"
                        )
                    continue
                if mesh_rel is None or str(mesh_rel).strip() == "":
                    if "has3dMesh" in part and has_3d is True:
                        report.error(
                            f"{parts_catalog_path}: part '{part_id}' has has3dMesh=true "
                            "but missing meshAssetPath"
                        )
                    continue
                if not isinstance(mesh_rel, str):
                    report.error(f"{parts_catalog_path}: part '{part_id}' has non-string meshAssetPath")
                    continue
                norm_rel = _normalize_mesh_rel_path(mesh_rel)
                if not norm_rel.lower().endswith(".glb"):
                    report.error(f"{parts_catalog_path}: part '{part_id}' meshAssetPath '{norm_rel}' must be .glb")
                    continue
                req_sockets = part.get("requiredSockets", [])
                if not isinstance(req_sockets, list) or not all(isinstance(s, str) for s in req_sockets):
                    report.error(f"{parts_catalog_path}: part '{part_id}' requiredSockets must be a list of strings")
                    req_sockets = []
                budget = infer_budget_for_asset(norm_rel, part)
                register_expectation(norm_rel, budget, req_sockets, f"part:{part_id}")
    else:
        if strict and not allow_partial:
            report.error(f"{parts_catalog_path}: missing parts.json catalog for asset validation")
        else:
            report.warn(f"{parts_catalog_path}: not found; validating discovered .glb files with manifest defaults")

    # Discover any extra .glb files (e.g., Garage props or M5 assets before parts.json merges)
    discovered_glbs: Set[str] = set()
    for glb_file in sorted(models_root.rglob("*.glb")):
        rel_art = glb_file.relative_to(art_root).as_posix()
        discovered_glbs.add(rel_art)
        if rel_art not in expected_specs:
            budget = infer_budget_for_asset(rel_art)
            default_sockets = CANONICAL_FALLBACK_SOCKETS.get(rel_art, (budget, []))[1]
            register_expectation(rel_art, budget, default_sockets, "discovered")

    # Validate every expected .glb
    for rel_path in sorted(expected_specs):
        budget, sockets, _sources = expected_specs[rel_path]
        abs_path = art_root / rel_path
        if not abs_path.is_file():
            report.error(f"{abs_path}: missing required .glb asset referenced by manifest/catalog")
            continue
        max_extent = infer_extent_for_asset(rel_path)
        errs, tris, found_nodes = inspect_glb_contract(
            abs_path,
            rel_label=rel_path,
            max_tris=budget,
            required_nodes=sorted(sockets),
            max_extent_m=max_extent,
        )
        if errs:
            for e in errs:
                report.error(e)
        else:
            report.passed(
                f"{rel_path}: valid .glb ({tris}/{budget} tris, UV0 ok, unit scale, "
                f"bbox < {max_extent:g}m, "
                f"{len(sockets)} required sockets verified, {len(found_nodes)} nodes)"
            )

    if not expected_specs and not discovered_glbs:
        report.error(f"{models_root}: no .glb files found")

    return report


def parse_args(argv: Optional[List[str]] = None) -> argparse.Namespace:
    default_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description="Validate MopedGame .glb assets, budgets, and sockets.")
    parser.add_argument("--repo-root", type=Path, default=default_root, help="Repository root path")
    parser.add_argument("--strict", action="store_true", help="Fail if models/ or parts.json are missing")
    parser.add_argument("--skip-missing-unity", action="store_true", help="Skip if unity/Assets/Art/Models is absent")
    parser.add_argument("--allow-partial", action="store_true", help="Allow partial workspace trees")
    return parser.parse_args(argv)


def main(argv: Optional[List[str]] = None) -> int:
    args = parse_args(argv)
    report = run_validation(
        args.repo_root.resolve(),
        strict=args.strict,
        skip_missing_unity=args.skip_missing_unity,
        allow_partial=args.allow_partial,
    )
    for item in report.passes:
        print(f"  [PASS] {item}")
    for item in report.skips:
        print(f"  [SKIP] {item}")
    for item in report.warnings:
        print(f"  [WARN] {item}")
    for item in report.errors:
        print(f"  [FAIL] {item}", file=sys.stderr)

    print(
        f"Asset check summary: {len(report.passes)} passed, "
        f"{len(report.skips)} skipped, {len(report.warnings)} warnings, {len(report.errors)} failed"
    )
    return 1 if report.errors else 0


if __name__ == "__main__":
    sys.exit(main())

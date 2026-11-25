#!/usr/bin/env python3
"""Self-contained unit tests for MopedGame Gate 2 static validators.

Run with:
    python3 -m unittest discover -s tools/validators -p 'test_*.py'
"""

import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path
from typing import List, Optional, Sequence

VALIDATORS_DIR = Path(__file__).resolve().parent
if str(VALIDATORS_DIR) not in sys.path:
    sys.path.insert(0, str(VALIDATORS_DIR))

import validate_assets  # noqa: E402
import validate_boundaries  # noqa: E402
import validate_data  # noqa: E402


def _build_minimal_glb(
    *,
    num_tris: int = 2,
    include_uv0: bool = True,
    node_scale: Optional[Sequence[float]] = None,
    extra_node_names: Sequence[str] = (),
    bbox_max: Sequence[float] = (0.5, 0.5, 0.5),
) -> bytes:
    """Build a valid binary glTF 2.0 (.glb) buffer in memory for testing."""
    # Vertex positions: 3 vertices (1 degenerate triangle) referenced by indices
    positions = struct.pack(
        "<9f",
        0.0, 0.0, 0.0,
        float(bbox_max[0]), 0.0, 0.0,
        0.0, float(bbox_max[1]), float(bbox_max[2]),
    )
    uvs = struct.pack("<6f", 0.0, 0.0, 1.0, 0.0, 0.0, 1.0)
    indices_list = [0, 1, 2] * num_tris
    indices = struct.pack(f"<{len(indices_list)}H", *indices_list)
    if len(indices) % 4 != 0:
        indices += b"\x00" * (4 - len(indices) % 4)

    pos_offset = 0
    uv_offset = len(positions)
    idx_offset = uv_offset + len(uvs)
    bin_data = positions + uvs + indices
    while len(bin_data) % 4 != 0:
        bin_data += b"\x00"

    attrs = {"POSITION": 0}
    if include_uv0:
        attrs["TEXCOORD_0"] = 1

    mesh_node: dict = {"name": "MeshRoot", "mesh": 0}
    if node_scale is not None:
        mesh_node["scale"] = list(node_scale)

    nodes: List[dict] = [mesh_node]
    children_indices: List[int] = []
    for name in extra_node_names:
        children_indices.append(len(nodes))
        nodes.append({"name": name, "translation": [0.0, 0.1, 0.0]})
    if children_indices:
        mesh_node["children"] = children_indices

    gltf = {
        "asset": {"version": "2.0"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": nodes,
        "meshes": [
            {
                "primitives": [
                    {
                        "attributes": attrs,
                        "indices": 2,
                        "mode": 4,
                    }
                ]
            }
        ],
        "buffers": [{"byteLength": len(bin_data)}],
        "bufferViews": [
            {"buffer": 0, "byteOffset": pos_offset, "byteLength": len(positions)},
            {"buffer": 0, "byteOffset": uv_offset, "byteLength": len(uvs)},
            {"buffer": 0, "byteOffset": idx_offset, "byteLength": len(indices_list) * 2},
        ],
        "accessors": [
            {
                "bufferView": 0,
                "componentType": 5126,
                "count": 3,
                "type": "VEC3",
                "min": [0.0, 0.0, 0.0],
                "max": [float(bbox_max[0]), float(bbox_max[1]), float(bbox_max[2])],
            },
            {
                "bufferView": 1,
                "componentType": 5126,
                "count": 3,
                "type": "VEC2",
            },
            {
                "bufferView": 2,
                "componentType": 5123,
                "count": len(indices_list),
                "type": "SCALAR",
            },
        ],
    }

    json_bytes = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    while len(json_bytes) % 4 != 0:
        json_bytes += b" "

    total_length = 12 + 8 + len(json_bytes) + 8 + len(bin_data)
    header = struct.pack("<4sII", b"glTF", 2, total_length)
    json_chunk = struct.pack("<II", len(json_bytes), 0x4E4F534A) + json_bytes
    bin_chunk = struct.pack("<II", len(bin_data), 0x004E4942) + bin_data
    return header + json_chunk + bin_chunk


def _write_json(path: Path, obj: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2), encoding="utf-8")


def _write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


class BoundaryValidatorTests(unittest.TestCase):
    def _create_valid_repo(self, root: Path) -> None:
        _write_json(
            root / "src/MopedSim.Core/MopedSim.Core.asmdef",
            {"name": "MopedSim.Core", "references": [], "noEngineReferences": True},
        )
        _write_json(
            root / "src/MopedSim.Core/package.json",
            {"name": "com.mopedgame.core", "version": "0.1.0"},
        )
        _write_text(
            root / "src/MopedSim.Core/MopedSim.Core.csproj",
            """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>""",
        )
        _write_text(
            root / "src/MopedSim.Core/Sim.cs",
            """// Never import UnityEngine in Core.
namespace MopedSim.Core
{
    public sealed class Sim
    {
        public string Note = "UnityEngine is forbidden";
    }
}""",
        )

        _write_json(
            root / "src/MopedSim.Data/MopedSim.Data.asmdef",
            {
                "name": "MopedSim.Data",
                "references": ["MopedSim.Core"],
                "noEngineReferences": True,
            },
        )
        _write_json(
            root / "src/MopedSim.Data/package.json",
            {"name": "com.mopedgame.data", "version": "0.1.0"},
        )
        _write_text(
            root / "src/MopedSim.Data/MopedSim.Data.csproj",
            """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\\MopedSim.Core\\MopedSim.Core.csproj" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>""",
        )
        _write_text(
            root / "tests/MopedSim.Core.Tests/MopedSim.Core.Tests.csproj",
            """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>""",
        )

        asmdefs = {
            "MopedSim.Ride": ["MopedSim.Core", "Unity.InputSystem"],
            "MopedSim.Garage": ["MopedSim.Core", "MopedSim.Data", "glTFast"],
            "MopedSim.World": ["MopedSim.Core", "MopedSim.Data"],
            "MopedSim.Audio": ["MopedSim.Core"],
            "MopedSim.UI": ["MopedSim.Core", "MopedSim.Data"],
            "MopedSim.App": [
                "MopedSim.Core",
                "MopedSim.Data",
                "MopedSim.Ride",
                "MopedSim.Garage",
                "MopedSim.World",
                "MopedSim.UI",
                "MopedSim.Audio",
            ],
            "MopedSim.Editor": ["MopedSim.Core", "MopedSim.App"],
        }
        for name, refs in asmdefs.items():
            payload: dict = {"name": name, "references": refs}
            if name == "MopedSim.Editor":
                payload["includePlatforms"] = ["Editor"]
            _write_json(root / f"unity/Assets/Scripts/{name}/{name}.asmdef", payload)

        _write_text(
            root / "unity/Assets/Scripts/MopedSim.App/AppRoot.cs",
            """#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
namespace MopedSim.App { public class AppRoot : MonoBehaviour {} }""",
        )
        _write_json(
            root / "unity/Packages/manifest.json",
            {
                "dependencies": {
                    "com.mopedgame.core": "file:../../src/MopedSim.Core",
                    "com.mopedgame.data": "file:../../src/MopedSim.Data",
                    "com.unity.cloud.gltfast": "6.0.0",
                    "com.unity.nuget.newtonsoft-json": "3.2.1",
                }
            },
        )

    def test_valid_boundaries_pass(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._create_valid_repo(root)
            rep = validate_boundaries.run_validation(root, strict=True)
            self.assertEqual(rep.errors, [])

    def test_unity_using_in_core_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._create_valid_repo(root)
            _write_text(
                root / "src/MopedSim.Core/Bad.cs",
                "using UnityEngine;\nnamespace MopedSim.Core { public class Bad {} }",
            )
            rep = validate_boundaries.run_validation(root, strict=True)
            self.assertTrue(any("UnityEngine" in e for e in rep.errors))

    def test_csharp10_file_scoped_namespace_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._create_valid_repo(root)
            _write_text(
                root / "src/MopedSim.Core/Modern.cs",
                "namespace MopedSim.Core;\npublic class Modern {}",
            )
            rep = validate_boundaries.run_validation(root, strict=True)
            self.assertTrue(any("file-scoped namespace" in e for e in rep.errors))

    def test_forbidden_unity_asmdef_ref_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._create_valid_repo(root)
            _write_json(
                root / "unity/Assets/Scripts/MopedSim.Ride/MopedSim.Ride.asmdef",
                {"name": "MopedSim.Ride", "references": ["MopedSim.Core", "MopedSim.Garage"]},
            )
            rep = validate_boundaries.run_validation(root, strict=True)
            self.assertTrue(any("MopedSim.Garage" in e for e in rep.errors))

    def test_missing_no_engine_references_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._create_valid_repo(root)
            _write_json(
                root / "src/MopedSim.Core/MopedSim.Core.asmdef",
                {"name": "MopedSim.Core", "references": [], "noEngineReferences": False},
            )
            rep = validate_boundaries.run_validation(root, strict=True)
            self.assertTrue(any("noEngineReferences" in e for e in rep.errors))


class DataValidatorTests(unittest.TestCase):
    def _create_valid_data(self, root: Path) -> None:
        _write_json(
            root / "data/schemas/part.schema.json",
            {
                "type": "object",
                "required": ["schemaVersion", "id", "category", "has3dMesh", "assetClass"],
                "properties": {
                    "schemaVersion": {"type": "integer", "minimum": 1},
                    "id": {"type": "string", "minLength": 1},
                    "category": {"type": "string"},
                    "has3dMesh": {"type": "boolean"},
                    "meshAssetPath": {"type": ["string", "null"]},
                    "assetClass": {"type": "string", "enum": ["Small", "Major", "Garage", "None"]},
                    "requiredSockets": {"type": "array", "items": {"type": "string"}},
                },
            },
        )
        for sname in (
            "bike_archetype.schema.json",
            "fault.schema.json",
            "zone.schema.json",
            "job.schema.json",
        ):
            _write_json(
                root / "data/schemas" / sname,
                {
                    "type": "object",
                    "required": ["id"],
                    "properties": {"id": {"type": "string", "minLength": 1}},
                },
            )
        _write_json(
            root / "data/schemas/save_state.schema.json",
            {
                "type": "object",
                "required": [
                    "schemaVersion",
                    "seed",
                    "cashUsd",
                    "bikes",
                    "inventory",
                    "activeToolRoll",
                    "completedGigs",
                ],
                "properties": {
                    "schemaVersion": {"type": "integer"},
                    "seed": {"type": "integer"},
                    "cashUsd": {"type": "number"},
                    "bikes": {"type": "array"},
                    "inventory": {"type": "array"},
                    "activeToolRoll": {"type": "array"},
                    "completedGigs": {"type": "array"},
                },
            },
        )

        _write_json(
            root / "data/catalogs/parts.json",
            {
                "schemaVersion": 1,
                "parts": [
                    {
                        "schemaVersion": 1,
                        "id": "frame_maxer_e50",
                        "category": "Frame",
                        "has3dMesh": True,
                        "meshAssetPath": "Models/Parts/Frames/frame_maxer_e50.glb",
                        "assetClass": "Major",
                        "requiredSockets": ["SOCKET_ENGINE_MOUNT"],
                    },
                    {
                        "schemaVersion": 1,
                        "id": "cyl_stock_50",
                        "category": "Cylinder",
                        "has3dMesh": True,
                        "meshAssetPath": "Models/Parts/Engine/cyl_stock_50.glb",
                        "assetClass": "Major",
                        "requiredSockets": ["SOCKET_HEAD_BASE"],
                    },
                    {
                        "schemaVersion": 1,
                        "id": "head_stock",
                        "category": "CylinderHead",
                        "has3dMesh": True,
                        "meshAssetPath": "Models/Parts/Engine/head_stock.glb",
                        "assetClass": "Major",
                        "requiredSockets": ["SOCKET_SPARK_PLUG"],
                    },
                    {
                        "schemaVersion": 1,
                        "id": "SparkPlugWrench",
                        "category": "Tool",
                        "has3dMesh": False,
                        "meshAssetPath": None,
                        "assetClass": "None",
                    },
                    {
                        "schemaVersion": 1,
                        "id": "SparePlug",
                        "category": "Spare",
                        "has3dMesh": False,
                        "meshAssetPath": None,
                        "assetClass": "None",
                    },
                ],
            },
        )
        slots = [
            {"slotId": "SLOT_FRAME", "parentSlotId": None, "defaultPartId": "frame_maxer_e50", "blockedBy": []},
            {"slotId": "SLOT_CYLINDER", "parentSlotId": "SLOT_FRAME", "defaultPartId": "cyl_stock_50", "blockedBy": ["SLOT_CYLINDER_HEAD"]},
            {"slotId": "SLOT_CYLINDER_HEAD", "parentSlotId": "SLOT_CYLINDER", "defaultPartId": "head_stock", "blockedBy": []},
        ]
        _write_json(
            root / "data/catalogs/archetypes.json",
            {
                "schemaVersion": 1,
                "archetypes": [
                    {"id": "arch_maxer_e50", "fasteners": [{"fastenerId": "BOLT_HEAD_NUT_1", "requiredToolId": "SparkPlugWrench"}], "mountSlots": slots},
                    {"id": "arch_bramble_pa50", "mountSlots": slots},
                    {"id": "arch_targa_a35", "mountSlots": slots},
                ],
            },
        )
        _write_json(
            root / "data/catalogs/faults.json",
            {
                "schemaVersion": 1,
                "faults": [
                    {
                        "id": fid,
                        "repairOptions": (
                            [{"requiredTools": ["SparkPlugWrench"], "consumedSpares": ["SparePlug"]}]
                            if fid == "fault_fouled_plug"
                            else []
                        ),
                    }
                    for fid in validate_data.REQUIRED_FAULT_IDS
                ],
            },
        )
        _write_json(
            root / "data/catalogs/zones.json",
            {
                "schemaVersion": 1,
                "zones": [{"id": zid} for zid in validate_data.REQUIRED_ZONE_IDS],
            },
        )
        _write_json(
            root / "data/catalogs/jobs.json",
            {
                "schemaVersion": 1,
                "jobs": [
                    {
                        "id": "job_mission_run",
                        "startZoneId": "Zone_WestOakland",
                        "targetZoneId": "Zone_SF_Mission",
                        "checkpoints": [{"zoneId": "Zone_BayCrossing"}],
                        "recommendedPartIds": ["frame_maxer_e50"],
                    }
                ],
            },
        )

    def test_valid_data_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._create_valid_data(root)
            rep = validate_data.run_validation(root, strict=True)
            self.assertEqual(rep.errors, [])

    def test_dag_cycle_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._create_valid_data(root)
            arch = json.loads((root / "data/catalogs/archetypes.json").read_text(encoding="utf-8"))
            arch["archetypes"][0]["mountSlots"][2]["blockedBy"] = ["SLOT_CYLINDER"]
            _write_json(root / "data/catalogs/archetypes.json", arch)
            rep = validate_data.run_validation(root, strict=True)
            self.assertTrue(any("cycle in removal preconditions DAG" in e for e in rep.errors))

    def test_broken_fault_spare_ref_fails(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._create_valid_data(root)
            faults = json.loads((root / "data/catalogs/faults.json").read_text(encoding="utf-8"))
            faults["faults"][0]["repairOptions"][0]["consumedSpares"] = ["MissingSpare"]
            _write_json(root / "data/catalogs/faults.json", faults)
            rep = validate_data.run_validation(root, strict=True)
            self.assertTrue(any("MissingSpare" in e for e in rep.errors))


class AssetValidatorTests(unittest.TestCase):
    def test_valid_glb_passes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            glb_path = Path(tmp) / "smoke_test_cube.glb"
            glb_path.write_bytes(
                _build_minimal_glb(num_tris=12, include_uv0=True, extra_node_names=["SOCKET_TEST_ANCHOR"])
            )
            errs, tris, nodes = validate_assets.inspect_glb_contract(
                glb_path,
                rel_label="smoke_test_cube.glb",
                max_tris=1500,
                required_nodes=["SOCKET_TEST_ANCHOR"],
            )
            self.assertEqual(errs, [])
            self.assertEqual(tris, 12)
            self.assertIn("SOCKET_TEST_ANCHOR", nodes)

    def test_missing_uv0_and_non_unit_scale_and_missing_socket_fail(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            glb_path = Path(tmp) / "bad.glb"
            glb_path.write_bytes(
                _build_minimal_glb(
                    num_tris=1600,
                    include_uv0=False,
                    node_scale=[10.0, 10.0, 10.0],
                    bbox_max=(3.0, 1.0, 1.0),
                )
            )
            errs, _, _ = validate_assets.inspect_glb_contract(
                glb_path,
                rel_label="bad.glb",
                max_tris=1500,
                required_nodes=["SOCKET_MISSING"],
            )
            joined = "\n".join(errs)
            self.assertIn("TEXCOORD_0", joined)
            self.assertIn("non-unit scale", joined)
            self.assertIn("exceeds budget 1500", joined)
            self.assertIn("exceeds 25.0m limit", joined)
            self.assertIn("SOCKET_MISSING", joined)


if __name__ == "__main__":
    unittest.main()

"""Headless Blender entrypoint generating all MopedGame .glb assets into unity/Assets/Art/Models."""

from __future__ import annotations

import argparse
import os
import sys
from typing import Dict, List

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from garage import garage_props  # noqa: E402
from parts import chassis, engine, exhaust, frames_forks, fuel, ignition_drivetrain  # noqa: E402
from smoke_test_cube import build_smoke_test_cube  # noqa: E402
import world  # noqa: E402


REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
DEFAULT_OUT_DIR = os.path.join(REPO_ROOT, "unity", "Assets", "Art", "Models")


def generate_all(out_dir: str = DEFAULT_OUT_DIR) -> List[Dict[str, object]]:
    results: List[Dict[str, object]] = []
    results.append(build_smoke_test_cube(out_dir))
    results.extend(frames_forks.generate_all(out_dir))
    results.extend(engine.generate_all(out_dir))
    results.extend(fuel.generate_all(out_dir))
    results.extend(exhaust.generate_all(out_dir))
    results.extend(ignition_drivetrain.generate_all(out_dir))
    results.extend(chassis.generate_all(out_dir))
    results.extend(garage_props.generate_all(out_dir))
    results.extend(world.generate_all(out_dir))
    return results


def _parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description="Generate all MopedGame .glb assets")
    parser.add_argument("--out", default=DEFAULT_OUT_DIR, help="Models output root directory")
    return parser.parse_args(argv)


if __name__ == "__main__":
    args = _parse_args()
    out_root = os.path.abspath(args.out)
    outputs = generate_all(out_root)
    print(f"[generate_all] generated {len(outputs)} .glb assets under {out_root}")
    tris_by_rel = {}
    for item in outputs:
        rel = os.path.relpath(str(item["filepath"]), out_root).replace("\\", "/")
        tris_by_rel[rel] = int(item["tris"])
        print(f"  - {rel}: {item['tris']} tris, {len(item['nodes'])} nodes")

    # Verify complete assembled moped budget (<= 28,000 tris) on representative full build.
    sample_bike_parts = [
        "Parts/Frames/frame_maxer_e50.glb",
        "Parts/Forks/fork_stock_e50.glb",
        "Parts/Engine/case_e50.glb",
        "Parts/Engine/cyl_kit_70.glb",
        "Parts/Engine/head_highcomp_70.glb",
        "Parts/Engine/piston_70.glb",
        "Parts/Engine/plug_b7hs.glb",
        "Parts/Fuel/intake_reed_15mm.glb",
        "Parts/Fuel/carb_phbg_19.glb",
        "Parts/Fuel/filter_pod_mesh.glb",
        "Parts/Fuel/tank_stock.glb",
        "Parts/Exhaust/pipe_topend_expansion.glb",
        "Parts/Ignition/stator_cdi_12v.glb",
        "Parts/Drivetrain/clutch_e50_stiff.glb",
        "Parts/Chassis/wheel_16_spoke.glb",
        "Parts/Chassis/wheel_16_spoke.glb",
        "Parts/Chassis/shocks_heavy_rear.glb",
        "Parts/Chassis/shocks_heavy_rear.glb",
        "Parts/Chassis/bars_bmx_riser.glb",
        "Parts/Chassis/seat_long_buddy.glb",
        "Parts/Chassis/light_boxy_led_12v.glb",
        "Parts/Chassis/rack_milk_crate.glb",
    ]
    bike_tris = sum(tris_by_rel[p] for p in sample_bike_parts)
    print(f"[generate_all] representative complete moped tri sum: {bike_tris} / 28000")
    if bike_tris > 28000:
        raise RuntimeError(f"Complete moped tri count {bike_tris} exceeds 28000 budget")

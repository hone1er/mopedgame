"""Smoke test asset generator producing Models/Test/smoke_test_cube.glb with SOCKET_TEST_ANCHOR."""

from __future__ import annotations

import argparse
import os
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from common_utils import MeshPartBuilder, add_socket, clear_scene, export_glb  # noqa: E402


REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
DEFAULT_OUT_DIR = os.path.join(REPO_ROOT, "unity", "Assets", "Art", "Models")


def build_smoke_test_cube(out_dir: str = DEFAULT_OUT_DIR) -> dict:
    """Build a 0.25m beveled-feel calibration block with SOCKET_TEST_ANCHOR on top."""
    clear_scene()
    mb = MeshPartBuilder("smoke_test_cube")
    mb.add_box(
        size=(0.25, 0.25, 0.25),
        location=(0.0, 0.0, 0.125),
        mat="mat_enamel_orange",
        smooth=False,
    )
    mb.add_cylinder(
        radius=0.06,
        depth=0.02,
        segments=12,
        location=(0.0, 0.0, 0.26),
        mat="mat_anodized_alu",
        smooth=True,
    )
    mb.build_object()

    add_socket("SOCKET_TEST_ANCHOR", location=(0.0, 0.0, 0.27))

    out_path = os.path.join(out_dir, "Test", "smoke_test_cube.glb")
    return export_glb(out_path, tri_budget=1500)


def _parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description="Generate smoke_test_cube.glb")
    parser.add_argument("--out", default=DEFAULT_OUT_DIR, help="Models output root directory")
    return parser.parse_args(argv)


if __name__ == "__main__":
    args = _parse_args()
    info = build_smoke_test_cube(args.out)
    print(f"[smoke_test_cube] exported {info['filepath']} ({info['tris']} tris)")

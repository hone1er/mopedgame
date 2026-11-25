"""Procedural generators for West Oakland Garage stations and props."""

from __future__ import annotations

import math
import os
from typing import Dict, List

from common_utils import MeshPartBuilder, add_socket, clear_scene, export_glb


GARAGE_TRI_BUDGET = 4000


def build_garage_workstand(out_dir: str) -> Dict[str, object]:
    """Build hydraulic/pedal moped garage workstand with SOCKET_BIKE_MOUNT."""
    clear_scene()
    mb = MeshPartBuilder("garage_workstand")

    for sx in (-0.34, 0.34):
        mb.add_box(size=(0.06, 0.90, 0.045), location=(sx, 0.0, 0.040), mat="mat_enamel_red")
        for sy in (-0.42, 0.42):
            mb.add_cylinder(radius=0.028, depth=0.020, segments=14, location=(sx, sy, 0.015), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_rubber")
            mb.add_box(size=(0.040, 0.045, 0.022), location=(sx, sy, 0.025), mat="mat_steel_dark")
    mb.add_box(size=(0.68, 0.10, 0.045), location=(0.0, 0.0, 0.042), mat="mat_enamel_red")

    mb.add_cylinder(radius=0.052, depth=0.38, segments=16, location=(0.0, 0.0, 0.24), mat="mat_steel_dark")
    mb.add_cylinder(radius=0.034, depth=0.32, segments=16, location=(0.0, 0.0, 0.50), mat="mat_chrome")
    # Hydraulic scissor brace arms & safety lock pin
    for sx in (-0.12, 0.12):
        mb.add_tube_between((sx, -0.18, 0.06), (sx * 0.4, -0.05, 0.65), radius=0.012, segments=8, mat="mat_steel_dark")
    mb.add_cylinder(radius=0.012, depth=0.22, segments=12, location=(0.14, 0.08, 0.14), rotation=(0.0, 1.35, 0.0), mat="mat_chrome")
    mb.add_box(size=(0.06, 0.04, 0.015), location=(0.24, 0.08, 0.16), mat="mat_rubber")

    mb.add_box(size=(0.24, 0.58, 0.045), location=(0.0, 0.0, 0.67), mat="mat_enamel_red")
    mb.add_box(size=(0.26, 0.44, 0.020), location=(0.0, 0.0, 0.705), mat="mat_rubber")
    for sy in (-0.16, 0.16):
        mb.add_box(size=(0.14, 0.04, 0.06), location=(0.0, sy, 0.715), mat="mat_steel_dark")

    # Magnetic side bolt dish with spare bolts
    mb.add_cylinder(radius=0.085, depth=0.022, segments=16, location=(0.19, 0.0, 0.56), mat="mat_anodized_alu")
    mb.add_torus(major_radius=0.085, minor_radius=0.008, major_segments=16, minor_segments=6, location=(0.19, 0.0, 0.568), mat="mat_anodized_red")

    mb.build_object()
    add_socket("SOCKET_BIKE_MOUNT", location=(0.0, 0.0, 0.72))

    return export_glb(
        os.path.join(out_dir, "Garage", "garage_workstand.glb"),
        tri_budget=GARAGE_TRI_BUDGET,
    )


def build_garage_workbench(out_dir: str) -> Dict[str, object]:
    """Build butcher-block carb/plug workbench with vise, drawers, lamp, and pegboard."""
    clear_scene()
    mb = MeshPartBuilder("garage_workbench")

    for sx in (-0.72, 0.72):
        for sy in (-0.34, 0.34):
            mb.add_box(size=(0.055, 0.055, 0.84), location=(sx, sy, 0.42), mat="mat_steel_dark")
            mb.add_cylinder(radius=0.030, depth=0.012, segments=12, location=(sx, sy, 0.006), mat="mat_rubber")
    for sy in (-0.34, 0.34):
        mb.add_box(size=(1.44, 0.04, 0.04), location=(0.0, sy, 0.22), mat="mat_steel_dark")
    mb.add_box(size=(1.46, 0.70, 0.030), location=(0.0, 0.0, 0.24), mat="mat_steel_dark")

    # Tool drawer bank underneath right half
    mb.add_box(size=(0.56, 0.62, 0.42), location=(0.38, 0.0, 0.62), mat="mat_enamel_red")
    for dz in (0.49, 0.62, 0.75):
        mb.add_box(size=(0.50, 0.02, 0.095), location=(0.38, -0.32, dz), mat="mat_enamel_red")
        mb.add_box(size=(0.24, 0.025, 0.014), location=(0.38, -0.34, dz), mat="mat_chrome")

    mb.add_box(size=(1.62, 0.82, 0.050), location=(0.0, 0.0, 0.855), mat="mat_wood_bench")

    # Bench vise on front-left corner
    mb.add_box(size=(0.14, 0.18, 0.11), location=(-0.62, -0.34, 0.935), mat="mat_enamel_red")
    mb.add_cylinder(radius=0.010, depth=0.20, segments=12, location=(-0.62, -0.45, 0.945), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")

    # Rear pegboard + tools + gooseneck shop lamp
    mb.add_box(size=(1.56, 0.030, 0.72), location=(0.0, 0.38, 1.24), mat="mat_steel_dark")
    for i in range(6):
        tx = -0.52 + i * 0.20
        mb.add_cylinder(radius=0.010, depth=0.24, segments=8, location=(tx, 0.35, 1.32), mat="mat_chrome")
        mb.add_box(size=(0.036, 0.012, 0.030), location=(tx, 0.35, 1.44), mat="mat_anodized_alu")
    mb.add_polyline_tube(
        [(0.64, 0.34, 0.88), (0.64, 0.30, 1.48), (0.48, 0.08, 1.56), (0.36, -0.02, 1.44)],
        [0.010, 0.010, 0.009, 0.012],
        segments=10,
        mat="mat_chrome",
    )
    mb.add_cone(radius1=0.075, radius2=0.022, depth=0.09, segments=14, location=(0.36, -0.02, 1.40), mat="mat_enamel_teal")

    mb.build_object()
    add_socket("SOCKET_WORK_SURFACE", location=(0.0, 0.0, 0.88))

    return export_glb(
        os.path.join(out_dir, "Garage", "garage_workbench.glb"),
        tri_budget=GARAGE_TRI_BUDGET,
    )


def build_garage_dyno_roller(out_dir: str) -> Dict[str, object]:
    """Build DIY steel floor roller dyno rig with front wheel chock and tach tower."""
    clear_scene()
    mb = MeshPartBuilder("garage_dyno_roller")

    mb.add_box(size=(0.76, 1.75, 0.055), location=(0.0, 0.0, 0.0275), mat="mat_steel_dark")
    mb.add_box(size=(0.64, 0.36, 0.055), location=(0.0, 1.02, 0.014), rotation=(0.08, 0.0, 0.0), mat="mat_steel_dark")

    # Twin knurled aluminum dyno rollers under rear wheel zone (+Y=0.59m)
    for ry in (0.48, 0.70):
        mb.add_cylinder(
            radius=0.095,
            depth=0.46,
            segments=20,
            location=(0.0, ry, 0.115),
            rotation=(0.0, math.pi * 0.5, 0.0),
            mat="mat_anodized_alu",
        )
        for rx in (-0.16, -0.06, 0.06, 0.16):
            mb.add_torus(major_radius=0.096, minor_radius=0.004, major_segments=18, minor_segments=6, location=(rx, ry, 0.115), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_steel_dark")
        for sx in (-0.26, 0.26):
            mb.add_box(size=(0.05, 0.09, 0.12), location=(sx, ry, 0.10), mat="mat_enamel_red")
    # Eddy-current load flywheel drum on +X
    mb.add_cylinder(radius=0.11, depth=0.06, segments=18, location=(0.32, 0.59, 0.115), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_cast_iron")

    mb.add_box(size=(0.24, 0.22, 0.28), location=(0.0, -0.72, 0.18), mat="mat_enamel_red")
    for sx in (-0.11, 0.11):
        mb.add_box(size=(0.03, 0.34, 0.22), location=(sx, -0.58, 0.14), mat="mat_steel_dark")

    mb.add_cylinder(radius=0.020, depth=0.88, segments=12, location=(-0.38, -0.25, 0.46), mat="mat_steel_dark")
    mb.add_box(size=(0.22, 0.12, 0.16), location=(-0.38, -0.25, 0.94), rotation=(-0.35, 0.0, 0.0), mat="mat_enamel_red")
    mb.add_box(size=(0.18, 0.02, 0.11), location=(-0.38, -0.30, 0.96), rotation=(-0.35, 0.0, 0.0), mat="mat_amber_plastic")

    mb.build_object()
    add_socket("SOCKET_BIKE_MOUNT", location=(0.0, 0.0, 0.28))

    return export_glb(
        os.path.join(out_dir, "Garage", "garage_dyno_roller.glb"),
        tri_budget=GARAGE_TRI_BUDGET,
    )


def build_garage_premix_can(out_dir: str) -> Dict[str, object]:
    """Build 5L steel/poly 2-stroke premix jerrycan with ribbed flex spout."""
    clear_scene()
    mb = MeshPartBuilder("garage_premix_can")

    mb.add_box(size=(0.17, 0.28, 0.32), location=(0.0, 0.0, 0.16), mat="mat_enamel_red")
    mb.add_box(size=(0.185, 0.20, 0.20), location=(0.0, 0.0, 0.16), mat="mat_enamel_red")
    for sx in (-0.092, 0.092):
        mb.add_box(size=(0.012, 0.18, 0.020), location=(sx, 0.0, 0.16), rotation=(0.65, 0.0, 0.0), mat="mat_steel_dark")
        mb.add_box(size=(0.012, 0.18, 0.020), location=(sx, 0.0, 0.16), rotation=(-0.65, 0.0, 0.0), mat="mat_steel_dark")

    mb.add_polyline_tube(
        [(0.0, 0.08, 0.32), (0.0, 0.06, 0.38), (0.0, -0.06, 0.38), (0.0, -0.07, 0.32)],
        [0.014, 0.015, 0.015, 0.014],
        segments=12,
        mat="mat_enamel_red",
    )

    mb.add_cylinder(radius=0.024, depth=0.032, segments=16, location=(0.0, -0.10, 0.335), mat="mat_steel_dark")
    spout_tip = (0.0, -0.22, 0.42)
    mb.add_tapered_tube_between(
        (0.0, -0.10, 0.35),
        spout_tip,
        r0=0.018,
        r1=0.010,
        segments=14,
        mat="mat_amber_plastic",
    )
    # Flex spout bellows rings
    for i in range(5):
        t = 0.2 + i * 0.14
        ry = -0.10 - 0.12 * t
        rz = 0.35 + 0.07 * t
        mb.add_torus(major_radius=0.017 - 0.005 * t, minor_radius=0.0025, major_segments=12, minor_segments=6, location=(0.0, ry, rz), rotation=(0.95, 0.0, 0.0), mat="mat_amber_plastic")

    mb.add_cylinder(radius=0.018, depth=0.038, segments=14, location=(0.0, 0.11, 0.34), mat="mat_amber_plastic")
    mb.add_box(size=(0.018, 0.010, 0.20), location=(0.0, -0.142, 0.16), mat="mat_amber_plastic")

    mb.build_object()
    add_socket("SOCKET_POUR_SPOUT", location=spout_tip)

    return export_glb(
        os.path.join(out_dir, "Garage", "garage_premix_can.glb"),
        tri_budget=GARAGE_TRI_BUDGET,
    )


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        build_garage_workstand(out_dir),
        build_garage_workbench(out_dir),
        build_garage_dyno_roller(out_dir),
        build_garage_premix_can(out_dir),
    ]

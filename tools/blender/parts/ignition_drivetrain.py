"""Procedural generators for ignition stators and E50/PA50/A35 drivetrain units."""

from __future__ import annotations

import math
import os
from typing import Dict, List

from common_utils import MeshPartBuilder, add_socket, clear_scene, export_glb


SMALL_TRI_BUDGET = 1500


def _build_stator(out_dir: str, is_cdi_12v: bool) -> Dict[str, object]:
    clear_scene()
    name = "stator_cdi_12v" if is_cdi_12v else "stator_points_6v"
    mb = MeshPartBuilder(name)

    # Aluminum backing plate oriented with normal along +X
    mb.add_cylinder(
        radius=0.050,
        depth=0.006,
        segments=20,
        location=(0.0, 0.0, 0.0),
        rotation=(0.0, math.pi * 0.5, 0.0),
        mat="mat_anodized_alu",
    )
    mb.add_cylinder(
        radius=0.011,
        depth=0.024,
        segments=12,
        location=(0.012, 0.0, 0.0),
        rotation=(0.0, math.pi * 0.5, 0.0),
        mat="mat_steel_dark",
    )
    # 3 mounting plate screws + wire harness tail
    for i in range(3):
        ang = (2.0 * math.pi * i) / 3.0 + 0.5
        mb.add_hex_bolt(
            radius=0.0035,
            height=0.008,
            location=(0.005, 0.044 * math.cos(ang), 0.044 * math.sin(ang)),
            rotation=(0.0, math.pi * 0.5, 0.0),
            mat="mat_chrome",
        )
    mb.add_polyline_tube(
        [(0.006, 0.036, 0.030), (0.002, 0.056, 0.042), (-0.006, 0.072, 0.048)],
        [0.004, 0.0035, 0.0035],
        segments=8,
        mat="mat_rubber",
    )

    if is_cdi_12v:
        for i in range(4):
            ang = i * math.pi * 0.5 + 0.35
            cy = 0.028 * math.cos(ang)
            cz = 0.028 * math.sin(ang)
            mb.add_box(size=(0.018, 0.018, 0.020), location=(0.011, cy, cz), rotation=(ang, 0.0, 0.0), mat="mat_copper")
            mb.add_box(size=(0.020, 0.012, 0.024), location=(0.011, cy * 1.15, cz * 1.15), rotation=(ang, 0.0, 0.0), mat="mat_steel_dark")
        mb.add_box(size=(0.016, 0.014, 0.018), location=(0.012, -0.036, 0.010), mat="mat_steel_dark")
        mb.add_box(size=(0.014, 0.020, 0.014), location=(0.010, 0.0, -0.034), mat="mat_anodized_red")
    else:
        for z_pos in (-0.024, 0.024):
            mb.add_box(size=(0.018, 0.024, 0.034), location=(0.011, 0.0, z_pos), mat="mat_copper")
            mb.add_box(size=(0.020, 0.032, 0.012), location=(0.011, 0.0, z_pos * 1.3), mat="mat_steel_dark")
        mb.add_cylinder(
            radius=0.0085,
            depth=0.022,
            segments=14,
            location=(0.012, 0.028, 0.006),
            rotation=(0.0, math.pi * 0.5, 0.0),
            mat="mat_chrome",
        )
        mb.add_box(size=(0.008, 0.022, 0.009), location=(0.012, -0.026, -0.006), mat="mat_brass")
        mb.add_box(size=(0.006, 0.010, 0.006), location=(0.013, -0.017, -0.003), mat="mat_amber_plastic")
        # Felt cam lubricator pad
        mb.add_box(size=(0.007, 0.008, 0.012), location=(0.012, -0.015, 0.014), mat="mat_enamel_cream")

    mb.build_object()
    add_socket("SOCKET_FLYWHEEL_KEY", location=(0.024, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Ignition", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def _build_e50_clutch(out_dir: str, is_stiff: bool) -> Dict[str, object]:
    clear_scene()
    name = "clutch_e50_stiff" if is_stiff else "clutch_e50_stock"
    mb = MeshPartBuilder(name)

    shoe_mat = "mat_anodized_blue" if is_stiff else "mat_cast_alu"
    pad_mat = "mat_anodized_red" if is_stiff else "mat_vinyl_tan"

    mb.add_cylinder(
        radius=0.056,
        depth=0.028,
        segments=20,
        location=(0.0, 0.0, 0.0),
        rotation=(0.0, math.pi * 0.5, 0.0),
        mat="mat_steel_dark",
    )
    mb.add_cylinder(
        radius=0.020,
        depth=0.036,
        segments=14,
        location=(-0.004, 0.0, 0.0),
        rotation=(0.0, math.pi * 0.5, 0.0),
        mat="mat_anodized_alu",
    )
    for i in range(3):
        ang = (2.0 * math.pi * i) / 3.0
        cy = 0.036 * math.cos(ang)
        cz = 0.036 * math.sin(ang)
        mb.add_box(size=(0.018, 0.022, 0.030), location=(-0.008, cy, cz), rotation=(ang, 0.0, 0.0), mat=shoe_mat)
        mb.add_box(size=(0.016, 0.006, 0.026), location=(-0.008, 0.047 * math.cos(ang), 0.047 * math.sin(ang)), rotation=(ang, 0.0, 0.0), mat=pad_mat)
        # Pivot pin & spring anchor
        mb.add_cylinder(
            radius=0.004,
            depth=0.024,
            segments=8,
            location=(-0.010, 0.026 * math.cos(ang - 0.3), 0.026 * math.sin(ang - 0.3)),
            rotation=(0.0, math.pi * 0.5, 0.0),
            mat="mat_chrome",
        )
        ang_next = ang + 1.05
        mb.add_tube_between(
            (-0.014, 0.024 * math.cos(ang + 0.35), 0.024 * math.sin(ang + 0.35)),
            (-0.014, 0.024 * math.cos(ang_next), 0.024 * math.sin(ang_next)),
            radius=0.0032,
            segments=6,
            mat="mat_anodized_blue" if is_stiff else "mat_chrome",
        )

    # Outer cooling ribs on bell
    for i in range(6):
        ang = (2.0 * math.pi * i) / 6.0
        mb.add_box(
            size=(0.026, 0.005, 0.008),
            location=(0.0, 0.058 * math.cos(ang), 0.058 * math.sin(ang)),
            rotation=(ang, 0.0, 0.0),
            mat="mat_anodized_alu" if is_stiff else "mat_cast_iron",
        )

    mb.add_cylinder(
        radius=0.012,
        depth=0.026,
        segments=12,
        location=(0.016, 0.0, 0.0),
        rotation=(0.0, math.pi * 0.5, 0.0),
        mat="mat_chrome",
    )

    mb.build_object()
    add_socket("SOCKET_DRIVE_OUTPUT", location=(0.026, 0.0, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Drivetrain", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def build_variator_pa50_stock(out_dir: str) -> Dict[str, object]:
    """Build Bramble PA50 dual-pulley variator + V-belt assembly."""
    clear_scene()
    mb = MeshPartBuilder("variator_pa50_stock")

    mb.add_cone(radius1=0.050, radius2=0.018, depth=0.014, segments=18, location=(-0.008, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_anodized_alu")
    mb.add_cone(radius1=0.018, radius2=0.050, depth=0.014, segments=18, location=(0.008, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_anodized_alu")
    for i in range(6):
        ang = (2.0 * math.pi * i) / 6.0
        mb.add_cylinder(
            radius=0.006,
            depth=0.010,
            segments=8,
            location=(-0.014, 0.028 * math.cos(ang), 0.028 * math.sin(ang)),
            rotation=(0.0, math.pi * 0.5, 0.0),
            mat="mat_brass",
        )

    mb.add_cone(radius1=0.058, radius2=0.022, depth=0.015, segments=18, location=(-0.008, 0.21, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_anodized_alu")
    mb.add_cone(radius1=0.022, radius2=0.058, depth=0.015, segments=18, location=(0.008, 0.21, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_anodized_alu")
    mb.add_cylinder(radius=0.020, depth=0.028, segments=12, location=(-0.024, 0.21, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_anodized_gold")

    belt_pts = [
        (0.0, -0.040, 0.0),
        (0.0, 0.0, 0.040),
        (0.0, 0.21, 0.048),
        (0.0, 0.258, 0.0),
        (0.0, 0.21, -0.048),
        (0.0, 0.0, -0.040),
        (0.0, -0.040, 0.0),
    ]
    mb.add_polyline_tube(belt_pts, [0.007] * len(belt_pts), segments=6, mat="mat_rubber")

    mb.build_object()
    add_socket("SOCKET_DRIVE_OUTPUT", location=(0.016, 0.21, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Drivetrain", "variator_pa50_stock.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def build_gearbox_a35_2spd(out_dir: str) -> Dict[str, object]:
    """Build Targa A35 2-speed automatic spur gear & sprag clutch cluster."""
    clear_scene()
    mb = MeshPartBuilder("gearbox_a35_2spd")

    # 1st & 2nd gear drums + toothed spur gear rims
    mb.add_cylinder(radius=0.048, depth=0.022, segments=18, location=(0.0, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_steel_dark")
    mb.add_cylinder(radius=0.042, depth=0.018, segments=18, location=(-0.024, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_anodized_alu")
    for i in range(14):
        ang = (2.0 * math.pi * i) / 14.0
        mb.add_box(size=(0.014, 0.006, 0.008), location=(0.0, 0.050 * math.cos(ang), 0.050 * math.sin(ang)), rotation=(ang, 0.0, 0.0), mat="mat_steel_dark")

    # Secondary layshaft + sprag output gear at +Y = 0.085m
    mb.add_cylinder(radius=0.036, depth=0.018, segments=16, location=(0.0, 0.085, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_cast_iron")
    mb.add_cylinder(radius=0.026, depth=0.018, segments=16, location=(-0.022, 0.085, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_brass")
    for i in range(12):
        ang = (2.0 * math.pi * i) / 12.0
        mb.add_box(size=(0.014, 0.005, 0.007), location=(0.0, 0.085 + 0.038 * math.cos(ang), 0.038 * math.sin(ang)), rotation=(ang, 0.0, 0.0), mat="mat_cast_iron")

    mb.add_cylinder(radius=0.012, depth=0.032, segments=12, location=(0.020, 0.085, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")

    mb.build_object()
    add_socket("SOCKET_DRIVE_OUTPUT", location=(0.036, 0.085, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Drivetrain", "gearbox_a35_2spd.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        _build_stator(out_dir, is_cdi_12v=False),
        _build_stator(out_dir, is_cdi_12v=True),
        _build_e50_clutch(out_dir, is_stiff=False),
        _build_e50_clutch(out_dir, is_stiff=True),
        build_variator_pa50_stock(out_dir),
        build_gearbox_a35_2spd(out_dir),
    ]

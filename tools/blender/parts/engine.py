"""Procedural generators for crankcases, cylinders, heads, pistons, and spark plugs."""

from __future__ import annotations

import math
import os
from typing import Dict, List

from common_utils import CYLINDER_FORWARD_TILT, MeshPartBuilder, add_bolt, add_socket, clear_scene, export_glb


MAJOR_TRI_BUDGET = 6500
SMALL_TRI_BUDGET = 1500


def _build_case(out_dir: str, variant: str) -> Dict[str, object]:
    clear_scene()
    mb = MeshPartBuilder(f"case_{variant}")

    mb.add_cylinder(
        radius=0.064,
        depth=0.088,
        segments=20,
        location=(0.0, 0.0, 0.0),
        rotation=(0.0, math.pi * 0.5, 0.0),
        mat="mat_cast_alu",
    )
    mb.add_box(size=(0.078, 0.13, 0.11), location=(0.0, 0.035, -0.005), mat="mat_cast_alu")
    # Center crankcase split seam rib + oil drain plug
    mb.add_box(size=(0.004, 0.14, 0.12), location=(0.0, 0.035, -0.005), mat="mat_anodized_alu")
    mb.add_hex_bolt(radius=0.006, height=0.008, location=(0.0, 0.020, -0.065), mat="mat_brass")

    # Barrel leans forward to near-horizontal (Puch E50 / Tomos A35 ~20 deg above horizontal, Honda PA50 ~10 deg).
    # Rx(+t) sends the barrel axis +Z to (0, -sin t, cos t), i.e. toward the front wheel.
    tilt = CYLINDER_FORWARD_TILT + (0.14 if variant == "pa50" else 0.0)
    cyl_rot = (tilt, 0.0, 0.0)
    cyl_pos = (0.0, -0.062, 0.030) if variant == "pa50" else (0.0, -0.058, 0.034)
    mb.add_box(
        size=(0.080, 0.080, 0.030),
        location=(0.0, cyl_pos[1] + 0.012 * math.sin(tilt), cyl_pos[2] - 0.012 * math.cos(tilt)),
        rotation=cyl_rot,
        mat="mat_cast_alu",
    )
    mb.add_cylinder(
        radius=0.054,
        depth=0.034,
        segments=18,
        location=(0.060, 0.0, 0.0),
        rotation=(0.0, math.pi * 0.5, 0.0),
        mat="mat_cast_alu",
    )
    # 6 perimeter case bolts on ignition side
    for i in range(6):
        ang = (2.0 * math.pi * i) / 6.0
        mb.add_hex_bolt(
            radius=0.004,
            height=0.008,
            location=(0.076, 0.048 * math.cos(ang), 0.048 * math.sin(ang)),
            rotation=(0.0, math.pi * 0.5, 0.0),
            mat="mat_chrome",
        )

    if variant == "e50":
        mb.add_cylinder(
            radius=0.068,
            depth=0.044,
            segments=20,
            location=(-0.065, 0.0, 0.0),
            rotation=(0.0, math.pi * 0.5, 0.0),
            mat="mat_anodized_alu",
        )
        mb.add_torus(major_radius=0.058, minor_radius=0.004, major_segments=18, minor_segments=6, location=(-0.086, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
        mb.add_box(size=(0.030, 0.09, 0.065), location=(-0.062, 0.045, 0.0), mat="mat_cast_alu")
    elif variant == "pa50":
        mb.add_cylinder(radius=0.066, depth=0.042, segments=18, location=(-0.068, -0.01, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_cast_iron")
        mb.add_box(size=(0.038, 0.24, 0.095), location=(-0.068, 0.10, -0.005), mat="mat_cast_iron")
        mb.add_cylinder(radius=0.058, depth=0.040, segments=18, location=(-0.068, 0.21, -0.005), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_cast_iron")
        for ry in (0.04, 0.10, 0.16):
            mb.add_box(size=(0.042, 0.010, 0.070), location=(-0.070, ry, -0.005), mat="mat_anodized_alu")
    else:  # a35
        mb.add_box(size=(0.052, 0.18, 0.125), location=(-0.066, 0.04, -0.005), mat="mat_cast_alu")
        for rz in (-0.032, -0.005, 0.022):
            mb.add_box(size=(0.056, 0.12, 0.010), location=(-0.068, 0.04, rz), mat="mat_anodized_alu")
        mb.add_hex_bolt(radius=0.007, height=0.008, location=(-0.066, 0.07, 0.060), mat="mat_enamel_red")

    bolt_locs = [
        ("BOLT_ENGINE_MOUNT_1", (0.0, 0.07, 0.03)),
        ("BOLT_ENGINE_MOUNT_2", (0.0, 0.07, -0.05)),
        ("BOLT_ENGINE_MOUNT_3", (0.0, -0.05, -0.06)),
    ]
    for b_name, pos in bolt_locs:
        mb.add_hex_bolt(radius=0.007, height=0.088, location=pos, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
        add_bolt(b_name, location=pos, rotation=(0.0, math.pi * 0.5, 0.0))

    mb.build_object()
    add_socket("SOCKET_CYLINDER_BASE", location=cyl_pos, rotation=cyl_rot)
    add_socket("SOCKET_IGNITION_STATOR", location=(0.075, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0))
    add_socket("SOCKET_CLUTCH_OUTPUT", location=(-0.085, 0.0, 0.0), rotation=(0.0, -math.pi * 0.5, 0.0))
    add_socket("SOCKET_CARB_INT_MOUNT", location=(0.0, 0.045, 0.065))

    return export_glb(
        os.path.join(out_dir, "Parts", "Engine", f"case_{variant}.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def _build_cylinder(out_dir: str, is_70cc: bool) -> Dict[str, object]:
    clear_scene()
    name = "cyl_kit_70" if is_70cc else "cyl_stock_50"
    mb = MeshPartBuilder(name)

    bore_r = 0.0235 if is_70cc else 0.0190
    sleeve_r = bore_r + 0.005
    fin_r = 0.058 if is_70cc else 0.045
    fin_xy = (0.110, 0.106) if is_70cc else (0.084, 0.080)
    fin_count = 9 if is_70cc else 7
    fin_pitch = 0.0082 if is_70cc else 0.0100
    fin_mat = "mat_anodized_alu" if is_70cc else "mat_cast_iron"

    # Base flange + paper/copper base gasket + honed cylinder liner
    mb.add_box(size=(0.082, 0.082, 0.002), location=(0.0, 0.0, 0.001), mat="mat_copper")
    mb.add_box(size=(0.080, 0.080, 0.010), location=(0.0, 0.0, 0.006), mat=fin_mat)
    mb.add_cylinder(radius=sleeve_r, depth=0.088, segments=20, location=(0.0, 0.0, 0.044), mat=fin_mat)
    mb.add_cylinder(radius=bore_r, depth=0.090, segments=20, location=(0.0, 0.0, 0.044), mat="mat_steel_dark")

    # Stacked rounded-square cooling fins + rubber anti-vibration damper strips
    for i in range(fin_count):
        fz = 0.049 + (i - 0.5 * (fin_count - 1)) * fin_pitch
        mb.add_cylinder(radius=fin_r, depth=0.0032, segments=20, location=(0.0, 0.0, fz), mat=fin_mat, smooth=False)
        mb.add_box(size=(fin_xy[0], fin_xy[1], 0.0030), location=(0.0, 0.0, fz), mat=fin_mat)
    for sx in (-1.0, 1.0):
        mb.add_box(size=(0.007, 0.010, 0.062), location=(sx * (fin_r - 0.006), 0.022, 0.048), mat="mat_rubber")
        mb.add_box(size=(0.018 if is_70cc else 0.012, 0.042, 0.062), location=(sx * (sleeve_r + 0.004), 0.0, 0.036), mat=fin_mat)

    # Exhaust port spigot + flange + copper ring + intake pad
    mb.add_cylinder(
        radius=0.017 if is_70cc else 0.014,
        depth=0.028,
        segments=14,
        location=(0.0, -0.048, 0.042),
        rotation=(math.pi * 0.5, 0.0, 0.0),
        mat=fin_mat,
    )
    mb.add_box(size=(0.052, 0.008, 0.030), location=(0.0, -0.058, 0.042), mat=fin_mat)
    mb.add_torus(major_radius=0.015, minor_radius=0.002, major_segments=14, minor_segments=6, location=(0.0, -0.062, 0.042), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_copper")
    mb.add_box(size=(0.050, 0.022, 0.034), location=(0.0, 0.048, 0.045), mat=fin_mat)

    studs = [
        ("BOLT_HEAD_STUD_1", (-0.026, -0.026, 0.090)),
        ("BOLT_HEAD_STUD_2", (0.026, -0.026, 0.090)),
        ("BOLT_HEAD_STUD_3", (-0.026, 0.026, 0.090)),
        ("BOLT_HEAD_STUD_4", (0.026, 0.026, 0.090)),
    ]
    for s_name, pos in studs:
        mb.add_cylinder(radius=0.007, depth=0.084, segments=8, location=(pos[0], pos[1], 0.046), mat=fin_mat)
        mb.add_cylinder(radius=0.0035, depth=0.055, segments=6, location=(pos[0], pos[1], 0.072), mat="mat_steel_dark")
        add_bolt(s_name, location=pos)

    mb.build_object()
    add_socket("SOCKET_HEAD_BASE", location=(0.0, 0.0, 0.088))
    # Port sockets cancel the crankcase's forward barrel tilt so pipes/intakes are authored in the bike frame.
    add_socket("SOCKET_EXHAUST_PORT", location=(0.0, -0.058, 0.042), rotation=(-CYLINDER_FORWARD_TILT, 0.0, 0.0))
    add_socket("SOCKET_INTAKE_PORT", location=(0.0, 0.058, 0.045), rotation=(-CYLINDER_FORWARD_TILT, 0.0, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Engine", f"{name}.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def _build_head(out_dir: str, is_highcomp_70: bool) -> Dict[str, object]:
    clear_scene()
    name = "head_highcomp_70" if is_highcomp_70 else "head_stock"
    mb = MeshPartBuilder(name)

    fin_r = 0.059 if is_highcomp_70 else 0.048
    fin_xy = (0.112, 0.112) if is_highcomp_70 else (0.090, 0.090)
    head_mat = "mat_anodized_alu" if is_highcomp_70 else "mat_cast_alu"
    fin_count = 6 if is_highcomp_70 else 4

    mb.add_box(size=(0.082, 0.082, 0.012), location=(0.0, 0.0, 0.006), mat=head_mat)
    for i in range(fin_count):
        fz = 0.016 + i * 0.0085
        mb.add_cylinder(radius=fin_r, depth=0.0034, segments=20, location=(0.0, 0.0, fz), mat=head_mat, smooth=False)
        mb.add_box(size=(fin_xy[0], fin_xy[1], 0.0032), location=(0.0, 0.0, fz), mat=head_mat)

    # Radial top cooling ribs
    for ang in (0.0, 0.7854, 1.5708, 2.3562):
        mb.add_box(size=(fin_xy[0] * 0.85, 0.004, 0.014), location=(0.0, 0.0, 0.044), rotation=(0.0, 0.0, ang), mat=head_mat)

    if is_highcomp_70:
        mb.add_cylinder(radius=0.032, depth=0.016, segments=18, location=(0.0, 0.0, 0.054), mat="mat_anodized_gold")

    mb.add_cylinder(radius=0.013, depth=0.028, segments=14, location=(0.0, -0.012, 0.046), rotation=(-0.26, 0.0, 0.0), mat=head_mat)
    mb.add_torus(major_radius=0.010, minor_radius=0.002, major_segments=12, minor_segments=6, location=(0.0, -0.015, 0.056), rotation=(-0.26, 0.0, 0.0), mat="mat_copper")

    nuts = [
        ("BOLT_HEAD_NUT_1", (-0.026, -0.026, 0.048)),
        ("BOLT_HEAD_NUT_2", (0.026, -0.026, 0.048)),
        ("BOLT_HEAD_NUT_3", (-0.026, 0.026, 0.048)),
        ("BOLT_HEAD_NUT_4", (0.026, 0.026, 0.048)),
    ]
    for n_name, pos in nuts:
        mb.add_cylinder(radius=0.0085, depth=0.003, segments=10, location=(pos[0], pos[1], pos[2] - 0.006), mat="mat_steel_dark")
        mb.add_hex_bolt(radius=0.0065, height=0.014, location=pos, mat="mat_chrome")
        add_bolt(n_name, location=pos)

    mb.build_object()
    add_socket("SOCKET_SPARK_PLUG", location=(0.0, -0.015, 0.055), rotation=(-0.26, 0.0, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Engine", f"{name}.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def _build_piston(out_dir: str, is_70cc: bool) -> Dict[str, object]:
    clear_scene()
    name = "piston_70" if is_70cc else "piston_50"
    mb = MeshPartBuilder(name)

    r = 0.0235 if is_70cc else 0.0190
    h = 0.050
    skirt_mat = "mat_anodized_gold" if is_70cc else "mat_anodized_alu"

    mb.add_cylinder(radius=r, depth=h, segments=20, location=(0.0, 0.0, 0.004), mat=skirt_mat)
    mb.add_cone(radius1=r, radius2=r * 0.72, depth=0.004, segments=20, location=(0.0, 0.0, 0.031), mat=skirt_mat)

    for rz in (0.021, 0.025):
        mb.add_torus(major_radius=r + 0.0005, minor_radius=0.0012, major_segments=20, minor_segments=6, location=(0.0, 0.0, rz), mat="mat_cast_iron")
    mb.add_cylinder(radius=0.006, depth=r * 2.02, segments=12, location=(0.0, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
    mb.add_box(size=(0.003, 0.016, 0.002), location=(0.0, -0.004, 0.034), mat="mat_cast_iron")

    mb.build_object()
    add_socket("SOCKET_WRIST_PIN", location=(0.0, 0.0, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Engine", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def _build_plug(out_dir: str, code: str, band_mat: str) -> Dict[str, object]:
    clear_scene()
    name = f"plug_{code}"
    mb = MeshPartBuilder(name)

    mb.add_cylinder(radius=0.007, depth=0.014, segments=12, location=(0.0, 0.0, -0.007), mat="mat_steel_dark")
    mb.add_cylinder(radius=0.010, depth=0.004, segments=12, location=(0.0, 0.0, 0.002), mat="mat_chrome")
    mb.add_hex_bolt(radius=0.0115, height=0.014, location=(0.0, 0.0, 0.011), mat="mat_chrome")
    mb.add_cylinder(radius=0.0065, depth=0.034, segments=12, location=(0.0, 0.0, 0.035), mat="mat_ceramic_white")
    mb.add_torus(major_radius=0.0068, minor_radius=0.0012, major_segments=12, minor_segments=6, location=(0.0, 0.0, 0.034), mat=band_mat)
    mb.add_torus(major_radius=0.0068, minor_radius=0.0012, major_segments=12, minor_segments=6, location=(0.0, 0.0, 0.041), mat="mat_ceramic_white")
    mb.add_cylinder(radius=0.0032, depth=0.010, segments=8, location=(0.0, 0.0, 0.057), mat="mat_brass")
    mb.add_cylinder(radius=0.0015, depth=0.005, segments=6, location=(0.0, 0.0, -0.016), mat="mat_brass")

    mb.build_object()
    add_socket("SOCKET_PLUG_BOOT", location=(0.0, 0.0, 0.062))

    return export_glb(
        os.path.join(out_dir, "Parts", "Engine", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        _build_case(out_dir, "e50"),
        _build_case(out_dir, "pa50"),
        _build_case(out_dir, "a35"),
        _build_cylinder(out_dir, is_70cc=False),
        _build_cylinder(out_dir, is_70cc=True),
        _build_head(out_dir, is_highcomp_70=False),
        _build_head(out_dir, is_highcomp_70=True),
        _build_piston(out_dir, is_70cc=False),
        _build_piston(out_dir, is_70cc=True),
        _build_plug(out_dir, "b6hs", "mat_enamel_red"),
        _build_plug(out_dir, "b7hs", "mat_anodized_blue"),
        _build_plug(out_dir, "b8hs", "mat_anodized_gold"),
    ]

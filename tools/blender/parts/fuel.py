"""Procedural generators for intakes, carburetors, air filters, and fuel tanks."""

from __future__ import annotations

import math
import os
from typing import Dict, List

from common_utils import MeshPartBuilder, add_bolt, add_socket, clear_scene, export_glb, superellipse_profile


SMALL_TRI_BUDGET = 1500


def _build_intake(out_dir: str, is_reed_15mm: bool) -> Dict[str, object]:
    clear_scene()
    name = "intake_reed_15mm" if is_reed_15mm else "intake_stock"
    mb = MeshPartBuilder(name)

    mat_body = "mat_anodized_blue" if is_reed_15mm else "mat_cast_alu"
    tube_r = 0.0115 if is_reed_15mm else 0.009

    # Cylinder flange plate + copper gasket shim
    mb.add_box(size=(0.054, 0.002, 0.036), location=(0.0, 0.001, 0.0), mat="mat_copper")
    mb.add_box(size=(0.052, 0.008, 0.034), location=(0.0, 0.006, 0.0), mat=mat_body)
    if is_reed_15mm:
        # Machined reed cage spacer block + petal housing ribs
        mb.add_box(size=(0.046, 0.024, 0.040), location=(0.0, 0.022, 0.0), mat="mat_anodized_alu")
        for rz in (-0.012, 0.0, 0.012):
            mb.add_box(size=(0.050, 0.016, 0.004), location=(0.0, 0.022, rz), mat=mat_body)
        start_y = 0.034
    else:
        start_y = 0.010

    # Smooth curved manifold elbow to carb spigot
    mb.add_polyline_tube(
        points=[
            (0.0, start_y, 0.0),
            (0.0, start_y + 0.012, 0.002),
            (0.0, start_y + 0.024, 0.006),
            (0.0, 0.062, 0.011),
            (0.0, 0.074, 0.014),
        ],
        radii=[tube_r, tube_r, tube_r + 0.0005, tube_r + 0.001, tube_r + 0.001],
        segments=16,
        mat=mat_body,
    )
    # Rubberized carb coupler + hose clamp ring
    mb.add_cylinder(
        radius=tube_r + 0.003,
        depth=0.016,
        segments=16,
        location=(0.0, 0.076, 0.014),
        rotation=(math.pi * 0.5, 0.0, 0.0),
        mat="mat_rubber",
    )
    mb.add_torus(
        major_radius=tube_r + 0.004,
        minor_radius=0.0018,
        major_segments=14,
        minor_segments=6,
        location=(0.0, 0.076, 0.014),
        rotation=(math.pi * 0.5, 0.0, 0.0),
        mat="mat_chrome",
    )

    for b_name, bx in (("BOLT_INTAKE_1", -0.019), ("BOLT_INTAKE_2", 0.019)):
        mb.add_cylinder(radius=0.006, depth=0.003, segments=10, location=(bx, 0.010, 0.0), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_anodized_alu")
        mb.add_hex_bolt(radius=0.0045, height=0.010, location=(bx, 0.013, 0.0), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_chrome")
        add_bolt(b_name, location=(bx, 0.013, 0.0), rotation=(math.pi * 0.5, 0.0, 0.0))

    mb.build_object()
    add_socket("SOCKET_CARB_MOUNT", location=(0.0, 0.082, 0.014))

    return export_glb(
        os.path.join(out_dir, "Parts", "Fuel", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def _build_carb(out_dir: str, variant: str) -> Dict[str, object]:
    clear_scene()
    name = f"carb_{variant}"
    mb = MeshPartBuilder(name)

    # Intake spigot clamp collar + pinch ring at origin
    mb.add_cylinder(
        radius=0.014,
        depth=0.020,
        segments=14,
        location=(0.0, 0.010, 0.0),
        rotation=(math.pi * 0.5, 0.0, 0.0),
        mat="mat_cast_alu",
    )
    mb.add_torus(
        major_radius=0.0155,
        minor_radius=0.002,
        major_segments=14,
        minor_segments=6,
        location=(0.0, 0.008, 0.0),
        rotation=(math.pi * 0.5, 0.0, 0.0),
        mat="mat_chrome",
    )

    if variant == "bing_12":
        # Bavaria/Bing 12mm round slide body + cylindrical screw-on bottom float bowl + brass tickler
        mb.add_cylinder(radius=0.015, depth=0.054, segments=16, location=(0.0, 0.032, 0.006), mat="mat_cast_alu")
        mb.add_cylinder(radius=0.016, depth=0.006, segments=16, location=(0.0, 0.032, 0.036), mat="mat_brass")
        mb.add_cylinder(radius=0.017, depth=0.028, segments=16, location=(0.0, 0.033, -0.024), mat="mat_cast_alu")
        mb.add_cylinder(radius=0.004, depth=0.014, segments=8, location=(0.018, 0.034, 0.016), mat="mat_brass")
        cable_top = (0.0, 0.032, 0.040)
        air_y = 0.054
        throat_r = 0.016
    elif variant == "sha_15":
        # Milano/Dellorto SHA-15 boxy body + side spring choke lever + wider 59mm rear filter collar
        mb.add_box(size=(0.034, 0.038, 0.044), location=(0.0, 0.034, -0.004), mat="mat_cast_alu")
        mb.add_cylinder(radius=0.016, depth=0.010, segments=14, location=(0.0, 0.034, 0.024), mat="mat_enamel_red")
        mb.add_box(size=(0.036, 0.036, 0.026), location=(0.0, 0.034, -0.030), mat="mat_steel_dark")
        mb.add_cylinder(radius=0.003, depth=0.020, segments=8, location=(-0.020, 0.028, 0.010), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_brass")
        mb.add_box(size=(0.006, 0.016, 0.018), location=(-0.026, 0.030, 0.006), mat="mat_chrome")
        cable_top = (0.0, 0.034, 0.030)
        air_y = 0.056
        throat_r = 0.028
    else:  # phbg_19
        # Tall PHBG-19 racing body + external spring idle/air screws + 4-bolt flat float bowl
        mb.add_cylinder(radius=0.017, depth=0.068, segments=16, location=(0.0, 0.038, 0.012), mat="mat_cast_alu")
        mb.add_cylinder(radius=0.018, depth=0.007, segments=16, location=(0.0, 0.038, 0.048), mat="mat_anodized_gold")
        mb.add_box(size=(0.040, 0.042, 0.032), location=(0.0, 0.038, -0.028), mat="mat_steel_dark")
        for sx in (-0.016, 0.016):
            for sy in (0.021, 0.055):
                mb.add_hex_bolt(radius=0.0028, height=0.006, location=(sx, sy, -0.045), mat="mat_chrome")
        # External spring-loaded idle & air screws
        for sy, sz in ((0.035, 0.005), (0.048, -0.004)):
            mb.add_cylinder(radius=0.004, depth=0.014, segments=8, location=(0.020, sy, sz), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_brass")
            mb.add_torus(major_radius=0.005, minor_radius=0.0014, major_segments=10, minor_segments=5, location=(0.018, sy, sz), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
        cable_top = (0.0, 0.038, 0.052)
        air_y = 0.064
        throat_r = 0.018

    # Brass throttle cable elbow & fuel banjo fitting
    mb.add_polyline_tube(
        [cable_top, (cable_top[0], cable_top[1], cable_top[2] + 0.014), (cable_top[0], cable_top[1] + 0.016, cable_top[2] + 0.022)],
        [0.0035, 0.0035, 0.003],
        segments=8,
        mat="mat_brass",
    )
    mb.add_cylinder(radius=0.007, depth=0.008, segments=10, location=(-0.018, 0.036, -0.010), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_brass")
    mb.add_cylinder(radius=0.003, depth=0.014, segments=8, location=(-0.022, 0.036, -0.003), mat="mat_brass")
    mb.add_hex_bolt(radius=0.005, height=0.006, location=(0.0, 0.035, -0.044), mat="mat_brass")

    # Venturi throat to rear air filter mount
    mb.add_cylinder(
        radius=throat_r,
        depth=0.018,
        segments=16,
        location=(0.0, air_y - 0.008, 0.0),
        rotation=(math.pi * 0.5, 0.0, 0.0),
        mat="mat_anodized_alu",
    )

    clamp_pos = (0.014, 0.008, 0.0)
    mb.add_hex_bolt(radius=0.004, height=0.012, location=clamp_pos, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
    add_bolt("BOLT_CARB_CLAMP", location=clamp_pos, rotation=(0.0, math.pi * 0.5, 0.0))

    mb.build_object()
    add_socket("SOCKET_AIR_FILTER", location=(0.0, air_y, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Fuel", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def _build_filter(out_dir: str, is_pod: bool) -> Dict[str, object]:
    clear_scene()
    name = "filter_pod_mesh" if is_pod else "filter_stock_box"
    mb = MeshPartBuilder(name)

    # Rubber carb boot + stainless clamp ring
    mb.add_cylinder(radius=0.018, depth=0.018, segments=14, location=(0.0, 0.009, 0.0), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_rubber")
    mb.add_torus(major_radius=0.019, minor_radius=0.002, major_segments=14, minor_segments=6, location=(0.0, 0.008, 0.0), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_chrome")

    if is_pod:
        # Tapered foam/mesh pod cone + chrome cap + pleated wire ribs
        mb.add_cone(radius1=0.032, radius2=0.024, depth=0.056, segments=16, location=(0.0, 0.046, 0.0), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_foam_filter")
        mb.add_cylinder(radius=0.024, depth=0.006, segments=16, location=(0.0, 0.076, 0.0), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_chrome")
        for ry in (0.032, 0.056):
            mb.add_torus(major_radius=0.028, minor_radius=0.0018, major_segments=14, minor_segments=6, location=(0.0, ry, 0.0), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_chrome")
        for i in range(6):
            ang = (2.0 * math.pi * i) / 6.0
            mb.add_tube_between(
                (0.032 * math.cos(ang), 0.020, 0.032 * math.sin(ang)),
                (0.024 * math.cos(ang), 0.072, 0.024 * math.sin(ang)),
                radius=0.0015,
                segments=5,
                mat="mat_chrome",
            )
        inlet_pos = (0.0, 0.080, 0.0)
    else:
        # OEM baffled black plastic airbox + louvers + snorkel
        mb.add_box(size=(0.072, 0.076, 0.088), location=(0.0, 0.056, -0.010), mat="mat_steel_dark")
        for lz in (-0.032, -0.016, 0.0, 0.016):
            mb.add_box(size=(0.078, 0.045, 0.005), location=(0.0, 0.056, lz), mat="mat_rubber")
        for lx in (-0.038, 0.038):
            mb.add_box(size=(0.006, 0.014, 0.032), location=(lx, 0.056, -0.010), mat="mat_chrome")
        mb.add_polyline_tube(
            [(0.0, 0.074, 0.030), (0.0, 0.078, 0.044), (0.0, 0.068, 0.054)],
            [0.012, 0.012, 0.013],
            segments=12,
            mat="mat_rubber",
        )
        inlet_pos = (0.0, 0.068, 0.056)

    mb.build_object()
    add_socket("SOCKET_FILTER_INLET", location=inlet_pos)

    return export_glb(
        os.path.join(out_dir, "Parts", "Fuel", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def build_tank_stock(out_dir: str) -> Dict[str, object]:
    """Build a slim rounded capsule tank (Magnum II / Tomos Targa top tank) with chrome cap and petcock.

    Shared by every archetype: the Targa frame rests it on the backbone, while the Maxer spine and the
    Bramble under-seat body enclose it so only the filler cap shows (keep it inside 0.36 x 0.12 x 0.10 m).
    """
    clear_scene()
    mb = MeshPartBuilder("tank_stock")

    tank_pts = [
        (0.0, -0.180, -0.004),
        (0.0, -0.165, 0.002),
        (0.0, -0.125, 0.006),
        (0.0, -0.060, 0.008),
        (0.0, 0.020, 0.006),
        (0.0, 0.100, 0.002),
        (0.0, 0.150, -0.004),
        (0.0, 0.178, -0.010),
    ]
    tank_wh = [
        (0.026, 0.020),
        (0.044, 0.034),
        (0.054, 0.043),
        (0.058, 0.046),
        (0.058, 0.046),
        (0.054, 0.042),
        (0.044, 0.034),
        (0.026, 0.020),
    ]
    mb.add_profile_sweep(tank_pts, superellipse_profile(18, 2.5), scales=tank_wh, mat="mat_enamel_yellow")
    # Two-tone side stripes (Magnum II style) on both flanks
    for side in (-1.0, 1.0):
        mb.add_box(size=(0.004, 0.230, 0.010), location=(side * 0.0575, -0.005, 0.002), mat="mat_enamel_black")
        mb.add_box(size=(0.004, 0.200, 0.006), location=(side * 0.0570, -0.010, -0.012), mat="mat_enamel_stripe_green")
    # Chrome flip-up gas cap with rubber seal, front-top
    mb.add_cylinder(radius=0.020, depth=0.010, segments=16, location=(0.0, -0.120, 0.050), mat="mat_rubber")
    mb.add_cylinder(radius=0.022, depth=0.012, segments=16, location=(0.0, -0.120, 0.060), mat="mat_chrome")
    mb.add_box(size=(0.010, 0.016, 0.006), location=(0.0, -0.100, 0.064), mat="mat_chrome")
    # Brass petcock bung + lever underneath on the right
    mb.add_cylinder(radius=0.008, depth=0.022, segments=10, location=(-0.026, 0.080, -0.044), mat="mat_brass")
    mb.add_box(size=(0.022, 0.006, 0.006), location=(-0.036, 0.080, -0.052), mat="mat_enamel_red")

    mb.build_object()
    add_socket("SOCKET_PETCOCK", location=(-0.026, 0.080, -0.056))

    return export_glb(
        os.path.join(out_dir, "Parts", "Fuel", "tank_stock.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        _build_intake(out_dir, is_reed_15mm=False),
        _build_intake(out_dir, is_reed_15mm=True),
        _build_carb(out_dir, "bing_12"),
        _build_carb(out_dir, "sha_15"),
        _build_carb(out_dir, "phbg_19"),
        _build_filter(out_dir, is_pod=False),
        _build_filter(out_dir, is_pod=True),
        build_tank_stock(out_dir),
    ]

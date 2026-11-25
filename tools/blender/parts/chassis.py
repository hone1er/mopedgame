"""Procedural generators for wheels, rear shocks, handlebars, seats, lights, and racks."""

from __future__ import annotations

import math
import os
from typing import Dict, List

from common_utils import MeshPartBuilder, add_bolt, add_socket, clear_scene, export_glb


MAJOR_TRI_BUDGET = 6500
SMALL_TRI_BUDGET = 1500


def _build_wheel(out_dir: str, is_mag: bool) -> Dict[str, object]:
    clear_scene()
    name = "wheel_16_mag" if is_mag else "wheel_16_spoke"
    mb = MeshPartBuilder(name)

    # 16" wheel with ~0.536m outer tire OD (0.268m radius)
    mb.add_torus(major_radius=0.232, minor_radius=0.036, major_segments=32, minor_segments=12, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_rubber")
    mb.add_torus(major_radius=0.198, minor_radius=0.015, major_segments=32, minor_segments=10, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_enamel_cream" if is_mag else "mat_chrome")

    mb.add_cylinder(radius=0.052, depth=0.095, segments=18, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_cast_alu")
    mb.add_cylinder(radius=0.062, depth=0.022, segments=18, location=(0.044, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_anodized_alu")
    mb.add_cylinder(radius=0.008, depth=0.145, segments=10, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_steel_dark")
    # Brake stay arm & sprocket mounting flange
    mb.add_box(size=(0.010, 0.065, 0.022), location=(0.056, 0.032, 0.0), mat="mat_steel_dark")
    mb.add_cylinder(radius=0.042, depth=0.008, segments=16, location=(-0.048, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_steel_dark")

    if is_mag:
        for i in range(5):
            ang = (2.0 * math.pi * i) / 5.0
            y_out = 0.188 * math.cos(ang)
            z_out = 0.188 * math.sin(ang)
            mb.add_tube_between((0.0, 0.0, 0.0), (0.0, y_out, z_out), radius=0.014, segments=8, mat="mat_enamel_cream")
            for d_ang in (-0.20, 0.20):
                y_sub = 0.188 * math.cos(ang + d_ang)
                z_sub = 0.188 * math.sin(ang + d_ang)
                mb.add_tube_between((0.0, 0.075 * math.cos(ang), 0.075 * math.sin(ang)), (0.0, y_sub, z_sub), radius=0.008, segments=6, mat="mat_enamel_cream")
    else:
        for i in range(24):
            ang_hub = (2.0 * math.pi * i) / 24.0
            side = 1.0 if i % 2 == 0 else -1.0
            ang_rim = ang_hub + side * 0.35
            p0 = (side * 0.032, 0.045 * math.cos(ang_hub), 0.045 * math.sin(ang_hub))
            p1 = (0.0, 0.188 * math.cos(ang_rim), 0.188 * math.sin(ang_rim))
            mb.add_tube_between(p0, p1, radius=0.0022, segments=6, mat="mat_chrome")

    bolt_l = (-0.072, 0.0, 0.0)
    bolt_r = (0.072, 0.0, 0.0)
    mb.add_hex_bolt(radius=0.010, height=0.010, location=bolt_l, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
    mb.add_hex_bolt(radius=0.010, height=0.010, location=bolt_r, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
    add_bolt("BOLT_AXLE_L", location=bolt_l)
    add_bolt("BOLT_AXLE_R", location=bolt_r)

    mb.build_object()
    add_socket("SOCKET_WHEEL_HUB", location=(0.0, 0.0, 0.0))
    add_socket("SOCKET_SPROCKET_MOUNT", location=(-0.052, 0.0, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Chassis", f"{name}.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def _build_shocks(out_dir: str, is_heavy: bool) -> Dict[str, object]:
    clear_scene()
    name = "shocks_heavy_rear" if is_heavy else "shocks_stock_rear"
    mb = MeshPartBuilder(name)

    spring_mat = "mat_anodized_gold" if is_heavy else "mat_chrome"
    turns = 6
    pts_per_turn = 8

    mb.add_cylinder(radius=0.014, depth=0.020, segments=12, location=(0.0, 0.0, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_anodized_alu")
    mb.add_cylinder(radius=0.014, depth=0.020, segments=12, location=(0.0, 0.0, -0.29), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_anodized_alu")
    mb.add_cylinder(radius=0.014, depth=0.14, segments=12, location=(0.0, 0.0, -0.085), mat="mat_steel_dark")
    mb.add_cylinder(radius=0.007, depth=0.16, segments=10, location=(0.0, 0.0, -0.20), mat="mat_chrome")
    mb.add_torus(major_radius=0.024, minor_radius=0.004, major_segments=14, minor_segments=6, location=(0.0, 0.0, -0.255), mat="mat_anodized_alu")

    if is_heavy:
        mb.add_cylinder(radius=0.016, depth=0.085, segments=12, location=(0.0, 0.032, -0.055), mat="mat_anodized_gold")
        mb.add_box(size=(0.020, 0.034, 0.022), location=(0.0, 0.016, -0.024), mat="mat_anodized_alu")

    total_pts = turns * pts_per_turn
    coil_pts = []
    coil_r = 0.025 if is_heavy else 0.022
    for i in range(total_pts + 1):
        t = i / total_pts
        ang = t * turns * 2.0 * math.pi
        z = -0.035 - t * 0.220
        coil_pts.append((coil_r * math.cos(ang), coil_r * math.sin(ang), z))
    mb.add_polyline_tube(coil_pts, [0.0045] * len(coil_pts), segments=6, mat=spring_mat)

    mb.build_object()
    add_socket("SOCKET_SHOCK_EYE_TOP", location=(0.0, 0.0, 0.0))
    add_socket("SOCKET_SHOCK_EYE_BOT", location=(0.0, 0.0, -0.29))

    return export_glb(
        os.path.join(out_dir, "Parts", "Chassis", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def _build_bars(out_dir: str, style: str) -> Dict[str, object]:
    clear_scene()
    name = f"bars_{style}"
    mb = MeshPartBuilder(name)

    mb.add_cylinder(radius=0.012, depth=0.08, segments=12, location=(0.0, 0.0, 0.025), mat="mat_anodized_alu")
    mb.add_box(size=(0.044, 0.034, 0.026), location=(0.0, 0.0, 0.065), mat="mat_anodized_alu")

    if style == "stock_upright":
        pts = [(-0.30, 0.10, 0.16), (-0.22, 0.07, 0.17), (-0.08, 0.01, 0.07), (0.0, 0.0, 0.065), (0.08, 0.01, 0.07), (0.22, 0.07, 0.17), (0.30, 0.10, 0.16)]
        grip_l = (-0.28, 0.092, 0.162)
        grip_r = (0.28, 0.092, 0.162)
        bar_mat = "mat_chrome"
    elif style == "cafe_clipon":
        # Low swept café M-bars
        pts = [(-0.27, 0.08, -0.025), (-0.20, 0.02, 0.015), (-0.12, -0.02, 0.095), (-0.06, -0.01, 0.065), (0.0, 0.0, 0.065), (0.06, -0.01, 0.065), (0.12, -0.02, 0.095), (0.20, 0.02, 0.015), (0.27, 0.08, -0.025)]
        grip_l = (-0.25, 0.062, -0.016)
        grip_r = (0.25, 0.062, -0.016)
        bar_mat = "mat_steel_dark"
    else:  # bmx_riser
        pts = [(-0.33, 0.05, 0.24), (-0.18, 0.02, 0.24), (-0.08, 0.0, 0.08), (0.0, 0.0, 0.065), (0.08, 0.0, 0.08), (0.18, 0.02, 0.24), (0.33, 0.05, 0.24)]
        mb.add_tube_between((-0.16, 0.018, 0.20), (0.16, 0.018, 0.20), radius=0.008, segments=10, mat="mat_anodized_gold")
        grip_l = (-0.30, 0.044, 0.24)
        grip_r = (0.30, 0.044, 0.24)
        bar_mat = "mat_chrome"

    mb.add_polyline_tube(pts, [0.011] * len(pts), segments=12, mat=bar_mat)

    for gx, gy, gz in (grip_l, grip_r):
        sign = 1.0 if gx > 0 else -1.0
        p_in = (gx - sign * 0.045, gy - 0.010, gz)
        p_out = (gx + sign * 0.045, gy + 0.010, gz)
        mb.add_tube_between(p_in, p_out, radius=0.0155, segments=12, mat="mat_rubber")
        mb.add_torus(major_radius=0.018, minor_radius=0.004, major_segments=12, minor_segments=6, location=p_in, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_rubber")
        mb.add_box(size=(0.075, 0.012, 0.008), location=(gx - sign * 0.05, gy - 0.028, gz - 0.008), mat="mat_anodized_alu")
        mb.add_box(size=(0.020, 0.022, 0.022), location=(gx - sign * 0.055, gy - 0.014, gz), mat="mat_steel_dark")

    mb.build_object()
    add_socket("SOCKET_GRIP_L", location=grip_l)
    add_socket("SOCKET_GRIP_R", location=grip_r)

    return export_glb(
        os.path.join(out_dir, "Parts", "Chassis", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def _build_seat(out_dir: str, is_buddy: bool) -> Dict[str, object]:
    clear_scene()
    name = "seat_long_buddy" if is_buddy else "seat_solo_spring"
    mb = MeshPartBuilder(name)

    mb.add_cylinder(radius=0.020, depth=0.045, segments=14, location=(0.0, 0.0, 0.020), mat="mat_steel_dark")
    mb.add_hex_bolt(radius=0.006, height=0.028, location=(0.0, 0.022, 0.020), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")

    if is_buddy:
        mb.add_box(size=(0.175, 0.42, 0.075), location=(0.0, 0.11, 0.075), mat="mat_vinyl_black")
        mb.add_box(size=(0.150, 0.36, 0.022), location=(0.0, 0.11, 0.116), mat="mat_vinyl_black")
        for ry in (-0.02, 0.06, 0.14, 0.22):
            mb.add_box(size=(0.145, 0.012, 0.008), location=(0.0, ry, 0.128), mat="mat_steel_dark")
        mb.add_polyline_tube(
            [(-0.088, 0.18, 0.08), (-0.095, 0.33, 0.08), (0.095, 0.33, 0.08), (0.088, 0.18, 0.08)],
            [0.007, 0.007, 0.007, 0.007],
            segments=10,
            mat="mat_chrome",
        )
        for sx in (-0.065, 0.065):
            mb.add_cylinder(radius=0.012, depth=0.045, segments=10, location=(sx, 0.26, 0.055), mat="mat_chrome")
    else:
        mb.add_box(size=(0.195, 0.16, 0.045), location=(0.0, 0.03, 0.082), mat="mat_vinyl_tan")
        mb.add_cone(radius1=0.095, radius2=0.042, depth=0.11, segments=16, location=(0.0, -0.075, 0.082), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_vinyl_tan")
        for ry in (-0.01, 0.04, 0.09):
            mb.add_box(size=(0.165, 0.010, 0.008), location=(0.0, ry, 0.106), mat="mat_vinyl_black")
        for sx in (-0.055, 0.055):
            mb.add_cylinder(radius=0.012, depth=0.042, segments=12, location=(sx, 0.075, 0.045), mat="mat_chrome")
            for sz in (0.032, 0.044, 0.056):
                mb.add_torus(major_radius=0.014, minor_radius=0.003, major_segments=12, minor_segments=6, location=(sx, 0.075, sz), mat="mat_chrome")

    mb.build_object()
    add_socket("SOCKET_SEAT_CLAMP", location=(0.0, 0.0, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Chassis", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def _build_light(out_dir: str, is_boxy_led: bool) -> Dict[str, object]:
    clear_scene()
    name = "light_boxy_led_12v" if is_boxy_led else "light_round_6v"
    mb = MeshPartBuilder(name)

    if is_boxy_led:
        mb.add_box(size=(0.145, 0.082, 0.095), location=(0.0, -0.042, 0.0), mat="mat_cast_iron")
        mb.add_box(size=(0.132, 0.014, 0.080), location=(0.0, -0.082, 0.0), mat="mat_steel_dark")
        mb.add_box(size=(0.120, 0.008, 0.068), location=(0.0, -0.086, 0.0), mat="mat_glass_lens")
        # 4 LED projector optics inside housing
        for lx in (-0.032, 0.032):
            for lz in (-0.016, 0.016):
                mb.add_cylinder(radius=0.013, depth=0.012, segments=12, location=(lx, -0.088, lz), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_chrome")
        # Protective steel wire rock-guard grille
        for gz in (-0.022, 0.0, 0.022):
            mb.add_box(size=(0.132, 0.004, 0.004), location=(0.0, -0.095, gz), mat="mat_steel_dark")
        for gx in (-0.038, 0.0, 0.038):
            mb.add_box(size=(0.004, 0.004, 0.072), location=(gx, -0.095, 0.0), mat="mat_steel_dark")
        for sx in (-0.078, 0.078):
            mb.add_box(size=(0.014, 0.026, 0.032), location=(sx, -0.055, 0.0), mat="mat_amber_plastic")
            mb.add_hex_bolt(radius=0.005, height=0.012, location=(sx * 0.96, -0.012, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
    else:
        mb.add_sphere(radius=0.062, u_segments=16, v_segments=10, location=(0.0, -0.038, 0.0), scale=(1.0, 1.15, 1.0), mat="mat_enamel_cream")
        mb.add_torus(major_radius=0.060, minor_radius=0.008, major_segments=18, minor_segments=6, location=(0.0, -0.085, 0.0), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_chrome")
        mb.add_cylinder(radius=0.056, depth=0.012, segments=16, location=(0.0, -0.088, 0.0), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_glass_lens")
        # Classic top-mounted round VDO-style moped speedometer
        mb.add_torus(major_radius=0.024, minor_radius=0.004, major_segments=14, minor_segments=6, location=(0.0, -0.035, 0.060), mat="mat_chrome")
        mb.add_cylinder(radius=0.022, depth=0.008, segments=14, location=(0.0, -0.035, 0.058), mat="mat_ceramic_white")
        for sx in (-0.064, 0.064):
            mb.add_hex_bolt(radius=0.005, height=0.010, location=(sx, -0.015, 0.0), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")

    mb.build_object()
    add_socket("SOCKET_LIGHT_BRACKET", location=(0.0, 0.0, 0.0))

    return export_glb(
        os.path.join(out_dir, "Parts", "Chassis", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def _build_rack(out_dir: str, is_milk_crate: bool) -> Dict[str, object]:
    clear_scene()
    name = "rack_milk_crate" if is_milk_crate else "rack_rear_wire"
    mb = MeshPartBuilder(name)

    for sx in (-0.095, 0.095):
        mb.add_tube_between((sx, -0.04, 0.0), (sx, 0.24, 0.0), radius=0.007, segments=10, mat="mat_chrome")
        mb.add_tube_between((sx, 0.16, 0.0), (sx, 0.06, -0.22), radius=0.006, segments=8, mat="mat_chrome")
    for ry in (-0.02, 0.08, 0.18, 0.24):
        mb.add_tube_between((-0.095, ry, 0.0), (0.095, ry, 0.0), radius=0.006, segments=8, mat="mat_chrome")
    mb.add_box(size=(0.052, 0.014, 0.024), location=(0.0, 0.245, -0.012), mat="mat_amber_plastic")

    if is_milk_crate:
        cx, cy, cz = 0.0, 0.11, 0.015
        hw, hd, h = 0.145, 0.145, 0.22
        for sx in (-hw, hw):
            for sy in (-hd, hd):
                mb.add_box(size=(0.022, 0.022, h), location=(cx + sx, cy + sy, cz + h * 0.5), mat="mat_crate_blue")
        for rz in (cz + 0.012, cz + h - 0.012, cz + h * 0.5):
            mb.add_box(size=(hw * 2.0 + 0.02, 0.016, 0.022), location=(cx, cy - hd, rz), mat="mat_crate_blue")
            mb.add_box(size=(hw * 2.0 + 0.02, 0.016, 0.022), location=(cx, cy + hd, rz), mat="mat_crate_blue")
            mb.add_box(size=(0.016, hd * 2.0 + 0.02, 0.022), location=(cx - hw, cy, rz), mat="mat_crate_blue")
            mb.add_box(size=(0.016, hd * 2.0 + 0.02, 0.022), location=(cx + hw, cy, rz), mat="mat_crate_blue")
        for ox in (-0.08, 0.0, 0.08):
            mb.add_box(size=(0.014, hd * 2.0, 0.010), location=(cx + ox, cy, cz + 0.005), mat="mat_crate_blue")
            mb.add_box(size=(0.010, 0.014, h * 0.8), location=(cx + ox, cy + hd, cz + h * 0.45), mat="mat_crate_blue")
        # 4 corner zip-tie anchor lugs securing crate to rack
        for sx in (-0.095, 0.095):
            for sy in (-0.01, 0.22):
                mb.add_box(size=(0.016, 0.016, 0.024), location=(sx, sy, 0.008), mat="mat_steel_dark")
        cargo_anchor = (0.0, 0.11, 0.04)
    else:
        mb.add_polyline_tube([(-0.07, 0.02, 0.015), (-0.07, 0.21, 0.015), (0.07, 0.21, 0.015), (0.07, 0.02, 0.015)], [0.0045] * 4, segments=8, mat="mat_steel_dark")
        for sx in (-0.06, 0.06):
            mb.add_torus(major_radius=0.012, minor_radius=0.003, major_segments=10, minor_segments=6, location=(sx, 0.01, 0.010), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
        cargo_anchor = (0.0, 0.11, 0.02)

    b1 = (-0.085, -0.02, -0.010)
    b2 = (0.085, -0.02, -0.010)
    mb.add_hex_bolt(radius=0.005, height=0.010, location=b1, mat="mat_chrome")
    mb.add_hex_bolt(radius=0.005, height=0.010, location=b2, mat="mat_chrome")
    add_bolt("BOLT_RACK_1", location=b1)
    add_bolt("BOLT_RACK_2", location=b2)

    mb.build_object()
    add_socket("SOCKET_CARGO_ANCHOR", location=cargo_anchor)

    return export_glb(
        os.path.join(out_dir, "Parts", "Chassis", f"{name}.glb"),
        tri_budget=SMALL_TRI_BUDGET,
    )


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        _build_wheel(out_dir, is_mag=False),
        _build_wheel(out_dir, is_mag=True),
        _build_shocks(out_dir, is_heavy=False),
        _build_shocks(out_dir, is_heavy=True),
        _build_bars(out_dir, "stock_upright"),
        _build_bars(out_dir, "cafe_clipon"),
        _build_bars(out_dir, "bmx_riser"),
        _build_seat(out_dir, is_buddy=False),
        _build_seat(out_dir, is_buddy=True),
        _build_light(out_dir, is_boxy_led=False),
        _build_light(out_dir, is_boxy_led=True),
        _build_rack(out_dir, is_milk_crate=False),
        _build_rack(out_dir, is_milk_crate=True),
    ]

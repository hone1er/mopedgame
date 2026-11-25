"""Procedural generators for moped frames and front forks."""

from __future__ import annotations

import math
import os
from typing import Dict, List

from common_utils import MeshPartBuilder, add_socket, clear_scene, export_glb


MAJOR_TRI_BUDGET = 6500


def _add_common_frame_rear_triangle(
    mb: MeshPartBuilder,
    paint_mat: str,
    swingarm_mat: str,
    seat_post_top: tuple,
) -> None:
    """Add symmetric rear swingarm tubes, dropouts, pedal cranks, seat post, and center stand."""
    for side in (-1.0, 1.0):
        x_pivot = side * 0.055
        x_axle = side * 0.075
        mb.add_tube_between((x_pivot, 0.14, 0.265), (x_axle, 0.58, 0.268), radius=0.015, segments=12, mat=swingarm_mat)
        mb.add_box(size=(0.010, 0.055, 0.048), location=(x_axle, 0.58, 0.268), mat=swingarm_mat)
        mb.add_tube_between((side * 0.045, 0.34, 0.56), (side * 0.085, 0.44, 0.57), radius=0.011, segments=10, mat=paint_mat)
        mb.add_tube_between((side * 0.045, 0.25, 0.56), (x_pivot, 0.14, 0.265), radius=0.014, segments=10, mat=paint_mat)

    # Swingarm cross-pivot tube + pedal bottom-bracket shell
    mb.add_cylinder(radius=0.022, depth=0.12, segments=14, location=(0.0, 0.14, 0.265), rotation=(0.0, math.pi * 0.5, 0.0), mat=swingarm_mat)
    mb.add_cylinder(radius=0.024, depth=0.11, segments=14, location=(0.0, 0.06, 0.24), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_steel_dark")

    # Iconic moped pedal cranks & block pedals
    mb.add_cylinder(radius=0.008, depth=0.18, segments=10, location=(0.0, 0.06, 0.24), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
    # Left crank down, right crank up
    for side, dy, dz in ((-1.0, 0.06, -0.11), (1.0, -0.06, 0.11)):
        cx = side * 0.085
        mb.add_tube_between((cx, 0.06, 0.24), (cx, 0.06 + dy, 0.24 + dz), radius=0.007, segments=8, mat="mat_chrome")
        mb.add_box(size=(0.072, 0.054, 0.022), location=(cx + side * 0.045, 0.06 + dy, 0.24 + dz), mat="mat_rubber")

    # Left-side pressed/ABS side cover & chain guard strip with quarter-turn knob
    mb.add_box(size=(0.010, 0.40, 0.065), location=(-0.088, 0.34, 0.268), mat="mat_charcoal_abs")
    mb.add_cylinder(radius=0.009, depth=0.010, segments=10, location=(-0.094, 0.22, 0.275), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")

    # Seat post tube & clamp collar
    mb.add_tube_between((0.0, 0.20, 0.48), seat_post_top, radius=0.018, segments=14, mat=paint_mat)
    mb.add_cylinder(radius=0.021, depth=0.024, segments=14, location=seat_post_top, rotation=(0.25, 0.0, 0.0), mat="mat_chrome")

    # Folded center stand bracket & legs under bottom bracket
    for side in (-1.0, 1.0):
        mb.add_tube_between((side * 0.045, 0.06, 0.18), (side * 0.075, 0.24, 0.14), radius=0.008, segments=8, mat="mat_steel_dark")
    mb.add_tube_between((-0.075, 0.24, 0.14), (0.075, 0.24, 0.14), radius=0.008, segments=8, mat="mat_steel_dark")


def _add_frame_sockets(tank_pos: tuple) -> None:
    add_socket("SOCKET_FORK_STEERER", location=(0.0, -0.38, 0.695), rotation=(-0.436332, 0.0, 0.0))
    add_socket("SOCKET_ENGINE_MOUNT", location=(0.0, -0.02, 0.30))
    add_socket("SOCKET_REAR_AXLE", location=(0.0, 0.58, 0.268))
    add_socket("SOCKET_SHOCK_TOP_L", location=(-0.085, 0.44, 0.57))
    add_socket("SOCKET_SHOCK_TOP_R", location=(0.085, 0.44, 0.57))
    add_socket("SOCKET_SEAT_POST", location=(0.0, 0.26, 0.68))
    add_socket("SOCKET_FUEL_TANK", location=tank_pos)
    add_socket("SOCKET_CARGO_RACK", location=(0.0, 0.49, 0.64))
    add_socket("SOCKET_CENTER_STAND", location=(0.0, 0.06, 0.18))


def build_frame_maxer_e50(out_dir: str) -> Dict[str, object]:
    """Build Puch Maxi E50 homage pressed step-through frame."""
    clear_scene()
    mb = MeshPartBuilder("frame_maxer_e50")
    paint = "mat_enamel_orange"

    mb.add_cylinder(radius=0.024, depth=0.16, segments=16, location=(0.0, -0.38, 0.695), rotation=(-0.436332, 0.0, 0.0), mat=paint)
    mb.add_torus(major_radius=0.026, minor_radius=0.004, major_segments=14, minor_segments=6, location=(0.0, -0.41, 0.765), rotation=(-0.436332, 0.0, 0.0), mat="mat_chrome")
    mb.add_torus(major_radius=0.026, minor_radius=0.004, major_segments=14, minor_segments=6, location=(0.0, -0.35, 0.625), rotation=(-0.436332, 0.0, 0.0), mat="mat_chrome")

    mb.add_polyline_tube(
        points=[
            (0.0, -0.35, 0.675),
            (0.0, -0.25, 0.56),
            (0.0, -0.10, 0.42),
            (0.0, 0.06, 0.37),
            (0.0, 0.22, 0.52),
            (0.0, 0.42, 0.57),
            (0.0, 0.56, 0.53),
        ],
        radii=[0.032, 0.036, 0.034, 0.035, 0.036, 0.032, 0.022],
        segments=16,
        mat=paint,
    )
    mb.add_box(size=(0.082, 0.19, 0.10), location=(0.0, -0.02, 0.34), rotation=(0.20, 0.0, 0.0), mat=paint)
    for side in (-1.0, 1.0):
        mb.add_box(size=(0.012, 0.22, 0.13), location=(side * 0.052, 0.12, 0.42), rotation=(0.15, 0.0, 0.0), mat="mat_anodized_alu")

    fender_pts = []
    fender_rad = []
    for i in range(11):
        ang = math.radians(-12.0 + i * 11.5)
        y = 0.58 - 0.305 * math.sin(ang)
        z = 0.268 + 0.305 * math.cos(ang)
        fender_pts.append((0.0, y, z))
        fender_rad.append(0.042)
    mb.add_polyline_tube(fender_pts, fender_rad, segments=10, mat=paint)
    mb.add_box(size=(0.045, 0.025, 0.045), location=(0.0, 0.85, 0.40), mat="mat_amber_plastic")

    _add_common_frame_rear_triangle(mb, paint, paint, (0.0, 0.26, 0.68))
    mb.build_object()
    _add_frame_sockets(tank_pos=(0.0, -0.14, 0.61))

    return export_glb(
        os.path.join(out_dir, "Parts", "Frames", "frame_maxer_e50.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def build_frame_bramble_pa50(out_dir: str) -> Dict[str, object]:
    """Build Honda Hobbit PA50 homage perimeter-cradle frame."""
    clear_scene()
    mb = MeshPartBuilder("frame_bramble_pa50")
    paint = "mat_enamel_teal"

    mb.add_cylinder(radius=0.025, depth=0.17, segments=16, location=(0.0, -0.38, 0.695), rotation=(-0.436332, 0.0, 0.0), mat=paint)
    mb.add_torus(major_radius=0.027, minor_radius=0.004, major_segments=14, minor_segments=6, location=(0.0, -0.41, 0.765), rotation=(-0.436332, 0.0, 0.0), mat="mat_chrome")

    mb.add_polyline_tube(
        points=[
            (0.0, -0.36, 0.705),
            (0.0, -0.15, 0.65),
            (0.0, 0.16, 0.62),
            (0.0, 0.45, 0.58),
            (0.0, 0.57, 0.54),
        ],
        radii=[0.025, 0.026, 0.026, 0.022, 0.018],
        segments=14,
        mat=paint,
    )
    for side in (-1.0, 1.0):
        mb.add_polyline_tube(
            points=[
                (side * 0.015, -0.34, 0.625),
                (side * 0.055, -0.18, 0.42),
                (side * 0.055, 0.02, 0.28),
                (side * 0.048, 0.20, 0.48),
            ],
            radii=[0.015, 0.015, 0.015, 0.015],
            segments=10,
            mat=paint,
        )
    mb.add_box(size=(0.09, 0.15, 0.07), location=(0.0, 0.02, 0.34), mat="mat_steel_dark")

    fender_pts = []
    fender_rad = []
    for i in range(9):
        ang = math.radians(-12.0 + i * 11.5)
        y = 0.58 - 0.300 * math.sin(ang)
        z = 0.268 + 0.300 * math.cos(ang)
        fender_pts.append((0.0, y, z))
        fender_rad.append(0.038)
    mb.add_polyline_tube(fender_pts, fender_rad, segments=10, mat="mat_steel_dark")
    mb.add_box(size=(0.045, 0.025, 0.045), location=(0.0, 0.84, 0.40), mat="mat_amber_plastic")

    _add_common_frame_rear_triangle(mb, paint, "mat_steel_dark", (0.0, 0.26, 0.68))
    mb.build_object()
    _add_frame_sockets(tank_pos=(0.0, -0.15, 0.66))

    return export_glb(
        os.path.join(out_dir, "Parts", "Frames", "frame_bramble_pa50.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def build_frame_targa_a35(out_dir: str) -> Dict[str, object]:
    """Build Tomos A35 homage twin-spar stamped spine frame."""
    clear_scene()
    mb = MeshPartBuilder("frame_targa_a35")
    paint = "mat_enamel_yellow"

    mb.add_cylinder(radius=0.025, depth=0.17, segments=16, location=(0.0, -0.38, 0.695), rotation=(-0.436332, 0.0, 0.0), mat=paint)
    mb.add_torus(major_radius=0.027, minor_radius=0.004, major_segments=14, minor_segments=6, location=(0.0, -0.41, 0.765), rotation=(-0.436332, 0.0, 0.0), mat="mat_chrome")

    for side in (-1.0, 1.0):
        mb.add_polyline_tube(
            points=[
                (side * 0.024, -0.36, 0.685),
                (side * 0.034, -0.14, 0.58),
                (side * 0.038, 0.14, 0.50),
                (side * 0.034, 0.42, 0.57),
            ],
            radii=[0.018, 0.021, 0.022, 0.018],
            segments=10,
            mat=paint,
        )
    mb.add_box(size=(0.088, 0.24, 0.14), location=(0.0, 0.05, 0.44), rotation=(0.18, 0.0, 0.0), mat="mat_steel_dark")
    mb.add_box(size=(0.068, 0.26, 0.06), location=(0.0, -0.16, 0.60), rotation=(-0.28, 0.0, 0.0), mat=paint)

    fender_pts = []
    fender_rad = []
    for i in range(11):
        ang = math.radians(-10.0 + i * 11.5)
        y = 0.58 - 0.305 * math.sin(ang)
        z = 0.268 + 0.305 * math.cos(ang)
        fender_pts.append((0.0, y, z))
        fender_rad.append(0.044)
    mb.add_polyline_tube(fender_pts, fender_rad, segments=10, mat=paint)
    mb.add_box(size=(0.045, 0.025, 0.045), location=(0.0, 0.85, 0.40), mat="mat_amber_plastic")

    _add_common_frame_rear_triangle(mb, paint, "mat_steel_dark", (0.0, 0.26, 0.68))
    mb.build_object()
    _add_frame_sockets(tank_pos=(0.0, -0.16, 0.65))

    return export_glb(
        os.path.join(out_dir, "Parts", "Frames", "frame_targa_a35.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def build_fork_stock_e50(out_dir: str) -> Dict[str, object]:
    """Build telescopic front fork with gaiters, triple clamps, and front fender."""
    clear_scene()
    mb = MeshPartBuilder("fork_stock_e50")

    mb.add_cylinder(radius=0.014, depth=0.22, segments=14, location=(0.0, 0.0, 0.0), mat="mat_steel_dark")
    mb.add_box(size=(0.16, 0.055, 0.018), location=(0.0, 0.0, 0.085), mat="mat_anodized_alu")
    mb.add_box(size=(0.16, 0.055, 0.022), location=(0.0, 0.0, -0.055), mat="mat_anodized_alu")
    mb.add_hex_bolt(radius=0.014, height=0.012, location=(0.0, 0.0, 0.100), mat="mat_chrome")

    for side in (-1.0, 1.0):
        x = side * 0.068
        mb.add_cylinder(radius=0.013, depth=0.26, segments=14, location=(x, 0.0, -0.03), mat="mat_chrome")
        for ring in range(5):
            mb.add_torus(
                major_radius=0.014,
                minor_radius=0.0042,
                major_segments=10,
                minor_segments=6,
                location=(x, 0.0, -0.09 - ring * 0.016),
                mat="mat_rubber",
            )
        mb.add_cylinder(radius=0.018, depth=0.28, segments=14, location=(x, 0.0, -0.335), mat="mat_enamel_orange")
        mb.add_box(size=(0.012, 0.035, 0.045), location=(x, 0.0, -0.47), mat="mat_anodized_alu")
        mb.add_box(size=(0.006, 0.075, 0.022), location=(side * 0.072, -0.04, 0.04), mat="mat_chrome")
        mb.add_hex_bolt(radius=0.006, height=0.010, location=(x, 0.0, 0.096), mat="mat_chrome")

    fender_pts = []
    fender_rad = []
    for i in range(11):
        ang = math.radians(-55.0 + i * 11.0)
        y = -0.295 * math.sin(ang)
        z = -0.47 + 0.295 * math.cos(ang)
        fender_pts.append((0.0, y, z))
        fender_rad.append(0.036)
    mb.add_polyline_tube(fender_pts, fender_rad, segments=10, mat="mat_chrome")
    for side in (-1.0, 1.0):
        mb.add_tube_between((side * 0.065, 0.0, -0.44), (side * 0.036, 0.21, -0.26), radius=0.004, segments=6, mat="mat_chrome")

    mb.build_object()
    add_socket("SOCKET_FRONT_AXLE", location=(0.0, 0.0, -0.47))
    add_socket("SOCKET_HANDLEBAR_CLAMP", location=(0.0, 0.0, 0.10))
    add_socket("SOCKET_HEADLIGHT_MOUNT", location=(0.0, -0.075, 0.04))
    add_socket("SOCKET_FENDER_FRONT", location=(0.0, 0.0, -0.14))

    return export_glb(
        os.path.join(out_dir, "Parts", "Forks", "fork_stock_e50.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        build_frame_maxer_e50(out_dir),
        build_frame_bramble_pa50(out_dir),
        build_frame_targa_a35(out_dir),
        build_fork_stock_e50(out_dir),
    ]

"""Procedural generators for moped frames and front forks.

Silhouettes follow the Pedal Code Army stable (docs/REFERENCES_VINTAGE_MOPEDS.md):
  - frame_maxer_e50   -> Puch Maxi S: fat pressed-steel "swoop" spine whose under-seat section is the fuel tank,
                         painted pressed rear fender, swingarm + twin shocks.
  - frame_bramble_pa50 -> Honda Hobbit / Derbi Variant step-through: big round spine dropping to a low step,
                         white front cover panel, enclosed under-seat body housing the tank.
  - frame_targa_a35   -> Puch Magnum II / Tomos Targa: black tube backbone with a top tank on it, twin seat rails,
                         paint-matched side covers and a chrome rear fender.

Blender axes: X lateral (+X = rider's left), -Y forward, +Z up. Hard points (steerer, axles, engine, seat post,
shock tops) are shared by all frames so wheelbase (~1.16 m) and saddle height stay inside PROJECT_GUIDE §5.1.
"""

from __future__ import annotations

import math
import os
from typing import Dict, List, Sequence, Tuple

from mathutils import Vector

from common_utils import (
    SHOCK_HALF_SPACING,
    MeshPartBuilder,
    add_socket,
    arc_points,
    clear_scene,
    export_glb,
    fender_profile,
    superellipse_profile,
)


MAJOR_TRI_BUDGET = 6500

STEERER = (0.0, -0.38, 0.695)
RAKE = -0.436332  # 25 deg, top of the head tube leans rearward
REAR_AXLE = (0.0, 0.58, 0.268)
ENGINE_MOUNT = (0.0, -0.02, 0.30)
SEAT_POST = (0.0, 0.26, 0.68)
CARGO_RACK = (0.0, 0.565, 0.64)  # rack (spans -0.04..+0.24 local) starts just behind the buddy seat tail
CENTER_STAND = (0.0, 0.06, 0.18)
SHOCK_TOP_Y = 0.44
SHOCK_TOP_Z = 0.57
SWINGARM_PIVOT = (0.0, 0.14, 0.265)
BOTTOM_BRACKET = (0.0, 0.06, 0.24)

SPINE_PROFILE = superellipse_profile(16, 2.8)
TUBE_PROFILE = superellipse_profile(14, 2.0)


def _steer_axis() -> Vector:
    return Vector((0.0, -math.sin(RAKE), math.cos(RAKE)))


def _head_tube_point(offset: float) -> Tuple[float, float, float]:
    p = Vector(STEERER) + _steer_axis() * offset
    return (p.x, p.y, p.z)


def _add_head_tube(mb: MeshPartBuilder, paint: str, radius: float = 0.025) -> None:
    mb.add_cylinder(radius=radius, depth=0.17, segments=16, location=STEERER, rotation=(RAKE, 0.0, 0.0), mat=paint)
    for off in (0.088, -0.088):
        mb.add_torus(major_radius=radius + 0.002, minor_radius=0.004, major_segments=14, minor_segments=6,
                     location=_head_tube_point(off), rotation=(RAKE, 0.0, 0.0), mat="mat_chrome")


def _add_rear_fender(mb: MeshPartBuilder, mat: str, deg_from: float, deg_to: float, radius: float = 0.305,
                     half_width: float = 0.052, crown: float = 0.022, count: int = 14) -> Tuple[float, float, float]:
    pts = arc_points((REAR_AXLE[1], REAR_AXLE[2]), radius, deg_from, deg_to, count)
    mb.add_profile_sweep(pts, fender_profile(half_width, crown, 0.003, 8), mat=mat)
    return pts[-1]


def _add_tail_light(mb: MeshPartBuilder, at: Tuple[float, float, float]) -> None:
    x, y, z = at
    mb.add_box(size=(0.060, 0.030, 0.040), location=(x, y + 0.010, z + 0.030), mat="mat_steel_dark")
    mb.add_box(size=(0.054, 0.008, 0.034), location=(x, y + 0.028, z + 0.030), mat="mat_lens_red")
    # Rear plate bracket
    mb.add_box(size=(0.090, 0.004, 0.060), location=(x, y + 0.030, z - 0.030), rotation=(0.35, 0.0, 0.0), mat="mat_steel_dark")


def _add_swingarm_and_pedals(mb: MeshPartBuilder, swingarm_mat: str) -> None:
    """Swingarm to the rear axle, pedal crank set, chain guard and folded center stand."""
    for side in (-1.0, 1.0):
        x_pivot = side * 0.055
        x_axle = side * 0.075
        mb.add_tube_between((x_pivot, SWINGARM_PIVOT[1], SWINGARM_PIVOT[2]), (x_axle, REAR_AXLE[1], REAR_AXLE[2]),
                            radius=0.015, segments=12, mat=swingarm_mat)
        mb.add_box(size=(0.010, 0.055, 0.048), location=(x_axle, REAR_AXLE[1], REAR_AXLE[2]), mat=swingarm_mat)
        # Lower shock eye lug on the swingarm end
        mb.add_box(size=(0.012, 0.030, 0.040), location=(side * SHOCK_HALF_SPACING, REAR_AXLE[1] - 0.02, REAR_AXLE[2] + 0.02), mat=swingarm_mat)
    mb.add_cylinder(radius=0.022, depth=0.12, segments=14, location=SWINGARM_PIVOT, rotation=(0.0, math.pi * 0.5, 0.0), mat=swingarm_mat)

    bx, by, bz = BOTTOM_BRACKET
    mb.add_cylinder(radius=0.024, depth=0.11, segments=14, location=BOTTOM_BRACKET, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_steel_dark")
    mb.add_cylinder(radius=0.008, depth=0.18, segments=10, location=BOTTOM_BRACKET, rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")
    # Cranks 180 deg apart, rubber block pedals with amber reflectors
    for side, dy, dz in ((-1.0, 0.06, -0.11), (1.0, -0.06, 0.11)):
        cx = side * 0.085
        mb.add_tube_between((cx, by, bz), (cx, by + dy, bz + dz), radius=0.007, segments=8, mat="mat_chrome")
        px = cx + side * 0.045
        mb.add_box(size=(0.080, 0.050, 0.020), location=(px, by + dy, bz + dz), mat="mat_rubber")
        mb.add_box(size=(0.050, 0.052, 0.008), location=(px, by + dy, bz + dz), mat="mat_amber_plastic")
        mb.add_tube_between((px - 0.042, by + dy, bz + dz), (px + 0.042, by + dy, bz + dz), radius=0.004, segments=6, mat="mat_chrome")

    # Right-side (-X) chain guard
    mb.add_box(size=(0.010, 0.42, 0.055), location=(-0.090, 0.33, 0.268), rotation=(0.0, 0.0, 0.0), mat="mat_steel_dark")
    mb.add_box(size=(0.030, 0.42, 0.006), location=(-0.078, 0.33, 0.297), mat="mat_steel_dark")

    # Center stand folded up flat under the swingarm (clear of the under-engine exhaust bellies)
    for side in (-1.0, 1.0):
        mb.add_tube_between((side * 0.050, 0.110, 0.236), (side * 0.060, 0.320, 0.216), radius=0.009, segments=8, mat="mat_steel_dark")
    mb.add_tube_between((-0.060, 0.320, 0.216), (0.060, 0.320, 0.216), radius=0.009, segments=8, mat="mat_steel_dark")
    mb.add_box(size=(0.012, 0.030, 0.024), location=(0.066, 0.330, 0.214), mat="mat_steel_dark")


def _add_shock_mounts(mb: MeshPartBuilder, paint: str, from_z: float, from_half_x: float) -> None:
    for side in (-1.0, 1.0):
        x = side * SHOCK_HALF_SPACING
        mb.add_box(size=(abs(x) - from_half_x + 0.012, 0.040, 0.010),
                   location=(side * (from_half_x + abs(x)) * 0.5, SHOCK_TOP_Y, SHOCK_TOP_Z + 0.022), mat=paint)
        mb.add_tube_between((x, SHOCK_TOP_Y, SHOCK_TOP_Z + 0.026), (x, SHOCK_TOP_Y, from_z), radius=0.008, segments=8, mat=paint)
        mb.add_box(size=(0.012, 0.030, 0.040), location=(x, SHOCK_TOP_Y, SHOCK_TOP_Z + 0.005), mat=paint)


def _add_frame_sockets(tank_pos: Tuple[float, float, float], tank_rot: Tuple[float, float, float] = (0.0, 0.0, 0.0)) -> None:
    add_socket("SOCKET_FORK_STEERER", location=STEERER, rotation=(RAKE, 0.0, 0.0))
    add_socket("SOCKET_ENGINE_MOUNT", location=ENGINE_MOUNT)
    add_socket("SOCKET_REAR_AXLE", location=REAR_AXLE)
    add_socket("SOCKET_SHOCK_TOP_L", location=(SHOCK_HALF_SPACING, SHOCK_TOP_Y, SHOCK_TOP_Z))
    add_socket("SOCKET_SHOCK_TOP_R", location=(-SHOCK_HALF_SPACING, SHOCK_TOP_Y, SHOCK_TOP_Z))
    add_socket("SOCKET_SEAT_POST", location=SEAT_POST)
    add_socket("SOCKET_FUEL_TANK", location=tank_pos, rotation=tank_rot)
    add_socket("SOCKET_CARGO_RACK", location=CARGO_RACK)
    add_socket("SOCKET_CENTER_STAND", location=CENTER_STAND)


def _spine(mb: MeshPartBuilder, pts: Sequence[Tuple[float, float, float]], wh: Sequence[Tuple[float, float]], mat: str) -> None:
    mb.add_profile_sweep(pts, SPINE_PROFILE, scales=wh, mat=mat)


def build_frame_maxer_e50(out_dir: str) -> Dict[str, object]:
    """Puch Maxi S homage: pressed-steel swoop spine with integrated under-seat tank section."""
    clear_scene()
    mb = MeshPartBuilder("frame_maxer_e50")
    paint = "mat_enamel_red"

    _add_head_tube(mb, paint, radius=0.027)

    # The spine: narrow and tall at the head tube, dipping to a knee-height step over the engine,
    # swelling into the tank under the saddle, then tapering into the rear fender.
    spine_pts = [
        (0.0, -0.372, 0.735),
        (0.0, -0.338, 0.650),
        (0.0, -0.292, 0.570),
        (0.0, -0.225, 0.515),
        (0.0, -0.130, 0.495),
        (0.0, -0.030, 0.505),
        (0.0, 0.060, 0.545),
        (0.0, 0.130, 0.592),
        (0.0, 0.205, 0.616),
        (0.0, 0.290, 0.622),
        (0.0, 0.380, 0.614),
        (0.0, 0.460, 0.594),
        (0.0, 0.530, 0.566),
        (0.0, 0.590, 0.546),
    ]
    spine_wh = [
        (0.026, 0.044),
        (0.032, 0.052),
        (0.038, 0.054),
        (0.042, 0.050),
        (0.044, 0.046),
        (0.046, 0.048),
        (0.054, 0.058),
        (0.066, 0.068),
        (0.075, 0.073),
        (0.077, 0.075),
        (0.075, 0.073),
        (0.062, 0.058),
        (0.046, 0.036),
        (0.034, 0.026),
    ]
    _spine(mb, spine_pts, spine_wh, paint)
    # Chrome trim band where the swoop meets the tank
    mb.add_torus(major_radius=0.058, minor_radius=0.004, major_segments=18, minor_segments=6,
                 location=(0.0, 0.100, 0.572), rotation=(math.pi * 0.5 - 0.55, 0.0, 0.0), mat="mat_chrome")
    # Tank seam lips along both sides of the tank section
    for side in (-1.0, 1.0):
        mb.add_box(size=(0.006, 0.30, 0.010), location=(side * 0.074, 0.29, 0.612), mat=paint)

    # Engine hanger plates down to the crankcase mount
    for side in (-1.0, 1.0):
        mb.add_box(size=(0.008, 0.050, 0.110), location=(side * 0.046, -0.010, 0.410), rotation=(-0.12, 0.0, 0.0), mat=paint)
    # Rear hoop: spine down to the swingarm pivot (both sides)
    for side in (-1.0, 1.0):
        mb.add_tube_between((side * 0.040, 0.190, 0.560), (side * 0.055, SWINGARM_PIVOT[1], SWINGARM_PIVOT[2] + 0.02),
                            radius=0.016, segments=10, mat=paint)

    _add_shock_mounts(mb, paint, from_z=0.600, from_half_x=0.060)
    tail = _add_rear_fender(mb, paint, deg_from=-38.0, deg_to=100.0)
    _add_tail_light(mb, tail)
    _add_swingarm_and_pedals(mb, paint)

    mb.build_object()
    # Tank (incl. filler cap) is fully enclosed by the swollen spine section, like the Maxi's frame tank.
    _add_frame_sockets(tank_pos=(0.0, 0.320, 0.604))

    return export_glb(os.path.join(out_dir, "Parts", "Frames", "frame_maxer_e50.glb"), tri_budget=MAJOR_TRI_BUDGET)


def build_frame_bramble_pa50(out_dir: str) -> Dict[str, object]:
    """Honda Hobbit / Derbi Variant homage: step-through tube spine, white cowl, enclosed under-seat body."""
    clear_scene()
    mb = MeshPartBuilder("frame_bramble_pa50")
    paint = "mat_enamel_slate_blue"

    _add_head_tube(mb, paint, radius=0.028)

    spine_pts = [
        (0.0, -0.372, 0.725),
        (0.0, -0.340, 0.640),
        (0.0, -0.300, 0.560),
        (0.0, -0.235, 0.500),
        (0.0, -0.140, 0.478),
        (0.0, -0.030, 0.478),
        (0.0, 0.060, 0.500),
        (0.0, 0.115, 0.560),
        (0.0, 0.150, 0.630),
    ]
    spine_wh = [(0.034, 0.036)] * 2 + [(0.038, 0.040)] * 5 + [(0.040, 0.042)] * 2
    mb.add_profile_sweep(spine_pts, TUBE_PROFILE, scales=spine_wh, mat=paint)

    # White front cowl over the down section (Hobbit/Camino style legshield cover)
    cowl_pts = spine_pts[:5]
    cowl_wh = [(0.040, 0.044), (0.044, 0.048), (0.046, 0.050), (0.046, 0.050), (0.044, 0.048)]
    mb.add_profile_sweep(cowl_pts, SPINE_PROFILE, scales=cowl_wh, mat="mat_enamel_white")

    # Rear frame: twin tubes from the top of the riser back over the wheel
    for side in (-1.0, 1.0):
        x = side * 0.046
        mb.add_polyline_tube(
            [(x * 0.6, 0.140, 0.615), (x, 0.240, 0.648), (x, 0.420, 0.642), (x, 0.560, 0.612), (x * 0.8, 0.660, 0.585)],
            [0.016] * 5, segments=10, mat=paint,
        )
    # Enclosed under-seat body (tank lives inside), with a vented white side badge panel
    body_pts = [(0.0, 0.080, 0.560), (0.0, 0.120, 0.565), (0.0, 0.250, 0.568), (0.0, 0.380, 0.572), (0.0, 0.470, 0.585), (0.0, 0.505, 0.592)]
    body_wh = [(0.046, 0.090), (0.078, 0.104), (0.084, 0.106), (0.084, 0.102), (0.074, 0.084), (0.048, 0.058)]
    mb.add_profile_sweep(body_pts, SPINE_PROFILE, scales=body_wh, mat=paint)
    for side in (-1.0, 1.0):
        mb.add_box(size=(0.004, 0.200, 0.090), location=(side * 0.085, 0.290, 0.545), mat="mat_enamel_white")
        mb.add_cylinder(radius=0.010, depth=0.006, segments=10, location=(side * 0.087, 0.400, 0.620),
                        rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome")

    # Engine hanger + rear hoop to swingarm pivot
    for side in (-1.0, 1.0):
        mb.add_box(size=(0.008, 0.050, 0.110), location=(side * 0.044, -0.020, 0.400), mat=paint)
        mb.add_tube_between((side * 0.040, 0.105, 0.520), (side * 0.055, SWINGARM_PIVOT[1], SWINGARM_PIVOT[2] + 0.02),
                            radius=0.016, segments=10, mat=paint)

    _add_shock_mounts(mb, paint, from_z=0.640, from_half_x=0.046)
    tail = _add_rear_fender(mb, paint, deg_from=-30.0, deg_to=110.0, half_width=0.048)
    _add_tail_light(mb, tail)
    _add_swingarm_and_pedals(mb, "mat_steel_dark")

    mb.build_object()
    _add_frame_sockets(tank_pos=(0.0, 0.295, 0.590))

    return export_glb(os.path.join(out_dir, "Parts", "Frames", "frame_bramble_pa50.glb"), tri_budget=MAJOR_TRI_BUDGET)


def build_frame_targa_a35(out_dir: str) -> Dict[str, object]:
    """Puch Magnum II / Tomos Targa homage: black tube backbone, top tank, seat rails, side covers."""
    clear_scene()
    mb = MeshPartBuilder("frame_targa_a35")
    frame = "mat_enamel_black"
    accent = "mat_enamel_yellow"

    _add_head_tube(mb, frame, radius=0.026)

    # Single large backbone curving down behind the engine to the swingarm pivot
    backbone = [
        (0.0, -0.366, 0.700),
        (0.0, -0.280, 0.620),
        (0.0, -0.160, 0.565),
        (0.0, -0.030, 0.530),
        (0.0, 0.080, 0.490),
        (0.0, 0.140, 0.420),
        (0.0, 0.150, 0.330),
    ]
    mb.add_profile_sweep(backbone, TUBE_PROFILE, scales=[(0.029, 0.031)] * len(backbone), mat=frame)
    # Gusset under the head tube
    mb.add_box(size=(0.010, 0.10, 0.07), location=(0.0, -0.32, 0.625), rotation=(0.75, 0.0, 0.0), mat=frame)
    # Tank mounting rubbers
    for ty in (-0.21, 0.02):
        mb.add_box(size=(0.040, 0.020, 0.012), location=(0.0, ty, 0.596 if ty < 0 else 0.556), mat="mat_rubber")

    # Twin seat rails and struts
    for side in (-1.0, 1.0):
        x = side * 0.062
        mb.add_polyline_tube(
            [(x * 0.4, 0.040, 0.520), (x, 0.150, 0.612), (x, 0.330, 0.632), (x, 0.520, 0.628), (x * 0.85, 0.640, 0.612)],
            [0.012] * 5, segments=10, mat=frame,
        )
        mb.add_tube_between((side * 0.050, SWINGARM_PIVOT[1] + 0.01, SWINGARM_PIVOT[2] + 0.03), (x, 0.300, 0.628),
                            radius=0.011, segments=8, mat=frame)
    mb.add_tube_between((-0.062, 0.520, 0.628), (0.062, 0.520, 0.628), radius=0.010, segments=8, mat=frame)
    mb.add_tube_between((-0.062, 0.250, 0.630), (0.062, 0.250, 0.630), radius=0.010, segments=8, mat=frame)
    # Short seat post stub onto the rail crossbar
    mb.add_cylinder(radius=0.014, depth=0.055, segments=12, location=(0.0, SEAT_POST[1], 0.652), mat=frame)

    # Paint-matched side covers under the saddle with quarter-turn knobs (PCA 3D-printed cover nod)
    cover_profile = superellipse_profile(12, 3.2)
    for side in (-1.0, 1.0):
        x = side * 0.074
        cover_pts = [(x, 0.090, 0.555), (x, 0.140, 0.562), (x, 0.240, 0.566), (x, 0.315, 0.560), (x, 0.345, 0.552)]
        cover_wh = [(0.010, 0.030), (0.014, 0.050), (0.014, 0.054), (0.014, 0.050), (0.010, 0.032)]
        mb.add_profile_sweep(cover_pts, cover_profile, scales=cover_wh, mat=accent)
        mb.add_cylinder(radius=0.008, depth=0.010, segments=10, location=(side * 0.090, 0.300, 0.590),
                        rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_charcoal_abs")
        mb.add_box(size=(0.004, 0.120, 0.012), location=(side * 0.089, 0.225, 0.540), mat="mat_enamel_stripe_green")

    _add_shock_mounts(mb, frame, from_z=0.632, from_half_x=0.062)
    tail = _add_rear_fender(mb, "mat_chrome", deg_from=-26.0, deg_to=104.0, half_width=0.046, crown=0.018)
    for side in (-1.0, 1.0):
        mb.add_tube_between((side * 0.046, 0.640, 0.610), (side * 0.048, 0.770, 0.470), radius=0.006, segments=6, mat="mat_chrome")
    _add_tail_light(mb, tail)
    _add_swingarm_and_pedals(mb, frame)

    mb.build_object()
    # Top tank rests on the backbone just behind the head tube, nose slightly down like the Magnum II
    _add_frame_sockets(tank_pos=(0.0, -0.090, 0.648), tank_rot=(0.05, 0.0, 0.0))

    return export_glb(os.path.join(out_dir, "Parts", "Frames", "frame_targa_a35.glb"), tri_budget=MAJOR_TRI_BUDGET)


def build_fork_stock_e50(out_dir: str) -> Dict[str, object]:
    """Telescopic fork: chrome stanchions in black rubber gaiters, alloy sliders, chrome pressed fender."""
    clear_scene()
    mb = MeshPartBuilder("fork_stock_e50")
    # Local frame: +Z up the steering axis, -Y forward; axle at z = -0.47.

    mb.add_cylinder(radius=0.014, depth=0.22, segments=14, location=(0.0, 0.0, 0.0), mat="mat_steel_dark")
    # Upper & lower triple clamps
    mb.add_box(size=(0.17, 0.060, 0.018), location=(0.0, -0.012, 0.094), mat="mat_cast_alu")
    mb.add_box(size=(0.17, 0.060, 0.026), location=(0.0, -0.012, -0.095), mat="mat_cast_alu")
    mb.add_hex_bolt(radius=0.014, height=0.012, location=(0.0, 0.0, 0.108), mat="mat_chrome")

    for side in (-1.0, 1.0):
        x = side * 0.070
        # Stanchion (chrome), gaiter bellows (rubber), slider (alloy)
        mb.add_cylinder(radius=0.0135, depth=0.25, segments=14, location=(x, -0.012, 0.0), mat="mat_chrome")
        gaiter = []
        radii = []
        for i in range(13):
            gaiter.append((x, -0.012, -0.11 - i * 0.011))
            radii.append(0.020 if i % 2 == 0 else 0.016)
        mb.add_polyline_tube(gaiter, radii, segments=12, mat="mat_rubber")
        mb.add_cylinder(radius=0.019, depth=0.26, segments=14, location=(x, -0.012, -0.375), mat="mat_cast_alu")
        mb.add_torus(major_radius=0.019, minor_radius=0.003, major_segments=14, minor_segments=6, location=(x, -0.012, -0.246), mat="mat_chrome")
        # Axle lug
        mb.add_box(size=(0.016, 0.040, 0.050), location=(x, 0.0, -0.47), mat="mat_cast_alu")
        mb.add_hex_bolt(radius=0.006, height=0.010, location=(x, -0.012, 0.104), mat="mat_chrome")
        # Headlight ear on the upper leg
        mb.add_box(size=(0.006, 0.070, 0.020), location=(side * 0.082, -0.050, 0.035), mat="mat_chrome")

    # Pressed chrome front fender hugging the tyre (wheel radius 0.268)
    fender_pts = arc_points((0.0, -0.47), 0.300, -78.0, 58.0, 14)
    mb.add_profile_sweep(fender_pts, fender_profile(0.046, 0.018, 0.003, 8), mat="mat_chrome")
    for side in (-1.0, 1.0):
        mb.add_tube_between((side * 0.060, 0.0, -0.45), (side * 0.046, 0.150, -0.235), radius=0.004, segments=6, mat="mat_chrome")
        mb.add_tube_between((side * 0.060, -0.012, -0.30), (side * 0.046, -0.030, -0.17), radius=0.006, segments=6, mat="mat_chrome")

    mb.build_object()
    add_socket("SOCKET_FRONT_AXLE", location=(0.0, 0.0, -0.47))
    add_socket("SOCKET_HANDLEBAR_CLAMP", location=(0.0, 0.0, 0.10))
    add_socket("SOCKET_HEADLIGHT_MOUNT", location=(0.0, -0.090, 0.04))
    add_socket("SOCKET_FENDER_FRONT", location=(0.0, 0.0, -0.17))

    return export_glb(os.path.join(out_dir, "Parts", "Forks", "fork_stock_e50.glb"), tri_budget=MAJOR_TRI_BUDGET)


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        build_frame_maxer_e50(out_dir),
        build_frame_bramble_pa50(out_dir),
        build_frame_targa_a35(out_dir),
        build_fork_stock_e50(out_dir),
    ]

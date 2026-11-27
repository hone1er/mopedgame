"""Procedural generators for stock baffled, circuit loop, and top-end expansion pipes.

Pipes are authored in the bike frame (-Y forward, +Z up, +X = rider's left) with the flange at the origin:
cylinder SOCKET_EXHAUST_PORT counter-rotates the crankcase barrel tilt (common_utils.CYLINDER_FORWARD_TILT),
so the port faces down-forward along PORT_NORMAL. On the E50/A35 cases the flange sits about 0.29 m above
the ground and 0.14 m ahead of the engine mount, so every route below keeps:
  - local z >= -0.19 (ground clearance >= ~0.09 m),
  - |x| <= 0.06 while passing the pedal crank sweep (bike y -0.09..0.22 -> local y 0.05..0.36),
  - mufflers at |x| ~= 0.11 beside the rear wheel, outboard of the axle nuts.
Like the Puch Maxi / Magnum II, the pipe runs low on the left (+X) side.
"""

from __future__ import annotations

import math
import os
from typing import Dict, List, Sequence, Tuple

from mathutils import Vector

from common_utils import CYLINDER_FORWARD_TILT, MeshPartBuilder, add_socket, clear_scene, export_glb


MAJOR_TRI_BUDGET = 6500

PORT_NORMAL = Vector((0.0, -math.cos(CYLINDER_FORWARD_TILT), -math.sin(CYLINDER_FORWARD_TILT)))
_FLANGE_ROT = (CYLINDER_FORWARD_TILT, 0.0, 0.0)          # box thin axis (local Y) along the port normal
_STUD_ROT = (CYLINDER_FORWARD_TILT + math.pi * 0.5, 0.0, 0.0)  # cylinder axis (local Z) along the port normal


def _along(dist: float, lateral: float = 0.0) -> Tuple[float, float, float]:
    p = PORT_NORMAL * dist
    return (lateral, p.y, p.z)


def _add_flange(mb: MeshPartBuilder, mat: str) -> None:
    mb.add_box(size=(0.052, 0.008, 0.028), location=_along(0.005), rotation=_FLANGE_ROT, mat=mat)
    mb.add_box(size=(0.054, 0.002, 0.030), location=_along(0.0), rotation=_FLANGE_ROT, mat="mat_copper")
    for sx in (-0.019, 0.019):
        mb.add_hex_bolt(radius=0.0045, height=0.008, location=_along(0.010, sx), rotation=_STUD_ROT, mat="mat_chrome")


def _header(lateral_end: float) -> List[Tuple[float, float, float]]:
    """Shared down-pipe: leaves the port along its normal and bends back under the crankcase."""
    a = _along(0.012)
    b = _along(0.034)
    return [
        (0.0, 0.0, 0.0),
        a,
        b,
        (0.004, b[1] - 0.004, -0.080),
        (0.010, 0.006, -0.136),
        (lateral_end, 0.070, -0.164),
    ]


def _add_seam_rings(mb: MeshPartBuilder, pts: Sequence[Tuple[float, float, float]], radii: Sequence[float], mat: str) -> None:
    for i in range(1, len(pts) - 1):
        p0, p1 = Vector(pts[i - 1]), Vector(pts[i + 1])
        d = (p1 - p0).normalized()
        rot = d.to_track_quat("Z", "Y").to_euler()
        mb.add_torus(major_radius=radii[i] + 0.001, minor_radius=0.0022, major_segments=16, minor_segments=6,
                     location=pts[i], rotation=(rot.x, rot.y, rot.z), mat=mat)


def _add_canister(mb: MeshPartBuilder, p0: Tuple[float, float, float], p1: Tuple[float, float, float], radius: float,
                  body_mat: str, cap_mat: str, tip_radius: float) -> None:
    mb.add_tube_between(p0, p1, radius=radius, segments=18, mat=body_mat)
    d = (Vector(p1) - Vector(p0)).normalized()
    for p in (p0, p1):
        rot = d.to_track_quat("Z", "Y").to_euler()
        mb.add_torus(major_radius=radius, minor_radius=0.003, major_segments=16, minor_segments=6,
                     location=p, rotation=(rot.x, rot.y, rot.z), mat=cap_mat)
    tip_end = Vector(p1) + d * 0.045
    mb.add_tube_between(p1, (tip_end.x, tip_end.y, tip_end.z), radius=tip_radius, segments=12, mat=cap_mat)


def build_pipe_stock_baffled(out_dir: str) -> Dict[str, object]:
    """Stock pea-shooter: slim black down-pipe into a long cigar muffler beside the rear wheel."""
    clear_scene()
    mb = MeshPartBuilder("pipe_stock_baffled")
    _add_flange(mb, "mat_steel_dark")

    pts = _header(0.030) + [
        (0.045, 0.200, -0.168),
        (0.056, 0.345, -0.162),
        (0.105, 0.420, -0.150),
        (0.115, 0.470, -0.143),
    ]
    rads = [0.013, 0.013, 0.013, 0.013, 0.013, 0.014, 0.014, 0.015, 0.018, 0.022]
    mb.add_polyline_tube(pts, rads, segments=16, mat="mat_exhaust_steel")

    muffler_start = (0.115, 0.470, -0.143)
    muffler_end = (0.115, 0.900, -0.082)
    mb.add_polyline_tube([muffler_start, (0.115, 0.520, -0.136), (0.115, 0.860, -0.088), muffler_end],
                         [0.022, 0.029, 0.029, 0.024], segments=18, mat="mat_enamel_black")
    for cy, cz in ((0.560, -0.130), (0.780, -0.099)):
        mb.add_torus(major_radius=0.030, minor_radius=0.003, major_segments=16, minor_segments=6,
                     location=(0.115, cy, cz), rotation=(math.pi * 0.5 - 0.14, 0.0, 0.0), mat="mat_steel_dark")
    mb.add_tube_between(muffler_end, (0.115, 0.945, -0.076), radius=0.009, segments=12, mat="mat_steel_dark")
    bracket = (0.105, 0.640, -0.090)
    mb.add_box(size=(0.012, 0.030, 0.048), location=bracket, mat="mat_steel_dark")

    mb.build_object()
    add_socket("SOCKET_EXHAUST_FLANGE", location=(0.0, 0.0, 0.0))
    add_socket("SOCKET_EXHAUST_BRACKET", location=bracket)

    return export_glb(os.path.join(out_dir, "Parts", "Exhaust", "pipe_stock_baffled.glb"), tri_budget=MAJOR_TRI_BUDGET)


def build_pipe_circuit_e50_proma(out_dir: str) -> Dict[str, object]:
    """Proma-style circuit pipe: compact expansion belly tucked under the engine, stinger to a side can."""
    clear_scene()
    mb = MeshPartBuilder("pipe_circuit_e50_proma")
    _add_flange(mb, "mat_exhaust_steel")

    header = _header(0.020)
    body = [
        (0.024, 0.120, -0.158),
        (0.026, 0.180, -0.155),
        (0.032, 0.240, -0.152),
        (0.044, 0.300, -0.148),
        (0.078, 0.365, -0.140),
        (0.108, 0.400, -0.132),
    ]
    pts = header + body
    rads = [0.014, 0.014, 0.015, 0.017, 0.022, 0.030, 0.038, 0.040, 0.036, 0.024, 0.015, 0.012]
    mb.add_polyline_tube(pts, rads, segments=16, mat="mat_exhaust_steel")
    _add_seam_rings(mb, pts[4:10], rads[4:10], "mat_steel_dark")

    _add_canister(mb, (0.112, 0.440, -0.128), (0.112, 0.640, -0.104), 0.025, "mat_anodized_alu", "mat_chrome", 0.009)
    mb.add_tube_between((0.108, 0.400, -0.132), (0.112, 0.440, -0.128), radius=0.012, segments=12, mat="mat_exhaust_steel")
    bracket = (0.100, 0.540, -0.100)
    mb.add_box(size=(0.012, 0.026, 0.040), location=bracket, mat="mat_steel_dark")

    mb.build_object()
    add_socket("SOCKET_EXHAUST_FLANGE", location=(0.0, 0.0, 0.0))
    add_socket("SOCKET_EXHAUST_BRACKET", location=bracket)

    return export_glb(os.path.join(out_dir, "Parts", "Exhaust", "pipe_circuit_e50_proma.glb"), tri_budget=MAJOR_TRI_BUDGET)


def build_pipe_topend_expansion(out_dir: str) -> Dict[str, object]:
    """Homoet-style top-end chamber: long welded cones under the engine, race silencer by the rear wheel."""
    clear_scene()
    mb = MeshPartBuilder("pipe_topend_expansion")
    _add_flange(mb, "mat_exhaust_steel")

    header = _header(0.012)
    body = [
        (0.010, 0.130, -0.150),
        (0.010, 0.200, -0.146),
        (0.014, 0.270, -0.146),
        (0.026, 0.340, -0.148),
        (0.052, 0.400, -0.146),
        (0.095, 0.450, -0.138),
        (0.110, 0.490, -0.130),
    ]
    pts = header + body
    rads = [0.015, 0.015, 0.016, 0.019, 0.026, 0.036, 0.048, 0.054, 0.054, 0.044, 0.026, 0.014, 0.012]
    mb.add_polyline_tube(pts, rads, segments=18, mat="mat_exhaust_steel")
    _add_seam_rings(mb, pts[3:11], rads[3:11], "mat_steel_dark")

    _add_canister(mb, (0.112, 0.520, -0.126), (0.112, 0.760, -0.094), 0.028, "mat_enamel_black", "mat_anodized_alu", 0.010)
    mb.add_tube_between((0.110, 0.490, -0.130), (0.112, 0.520, -0.126), radius=0.012, segments=12, mat="mat_exhaust_steel")
    bracket = (0.100, 0.640, -0.100)
    mb.add_box(size=(0.014, 0.028, 0.040), location=bracket, mat="mat_anodized_alu")

    mb.build_object()
    add_socket("SOCKET_EXHAUST_FLANGE", location=(0.0, 0.0, 0.0))
    add_socket("SOCKET_EXHAUST_BRACKET", location=bracket)

    return export_glb(os.path.join(out_dir, "Parts", "Exhaust", "pipe_topend_expansion.glb"), tri_budget=MAJOR_TRI_BUDGET)


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        build_pipe_stock_baffled(out_dir),
        build_pipe_circuit_e50_proma(out_dir),
        build_pipe_topend_expansion(out_dir),
    ]

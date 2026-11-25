"""Procedural generators for stock baffled, circuit loop, and top-end expansion pipes."""

from __future__ import annotations

import os
from typing import Dict, List

from common_utils import MeshPartBuilder, add_socket, clear_scene, export_glb


MAJOR_TRI_BUDGET = 6500


def _add_flange(mb: MeshPartBuilder, mat: str) -> None:
    mb.add_box(size=(0.052, 0.008, 0.028), location=(0.0, 0.004, 0.0), mat=mat)
    mb.add_box(size=(0.054, 0.002, 0.030), location=(0.0, 0.0, 0.0), mat="mat_copper")
    for sx in (-0.019, 0.019):
        mb.add_hex_bolt(radius=0.0045, height=0.008, location=(sx, 0.008, 0.0), rotation=(1.5708, 0.0, 0.0), mat="mat_chrome")


def build_pipe_stock_baffled(out_dir: str) -> Dict[str, object]:
    """Build slender chrome pea-shooter baffled stock exhaust."""
    clear_scene()
    mb = MeshPartBuilder("pipe_stock_baffled")
    _add_flange(mb, "mat_chrome")

    pts = [
        (0.0, 0.006, 0.0),
        (0.0, -0.042, -0.024),
        (0.014, -0.068, -0.080),
        (0.042, -0.056, -0.148),
        (0.072, 0.018, -0.196),
        (0.086, 0.140, -0.212),
        (0.090, 0.280, -0.215),
        (0.092, 0.450, -0.212),
        (0.092, 0.640, -0.210),
    ]
    rads = [0.013, 0.013, 0.013, 0.014, 0.015, 0.020, 0.028, 0.028, 0.028]
    mb.add_polyline_tube(pts, rads, segments=18, mat="mat_chrome")

    # Exhaust seam clamp collars
    for cy in (0.26, 0.58):
        mb.add_torus(major_radius=0.029, minor_radius=0.003, major_segments=16, minor_segments=6, location=(0.091, cy, -0.212), rotation=(1.5708, 0.0, 0.0), mat="mat_steel_dark")

    mb.add_cylinder(radius=0.009, depth=0.035, segments=12, location=(0.092, 0.655, -0.210), rotation=(1.5708, 0.0, 0.0), mat="mat_steel_dark")
    bracket = (0.082, 0.45, -0.175)
    mb.add_box(size=(0.014, 0.028, 0.042), location=bracket, mat="mat_chrome")

    mb.build_object()
    add_socket("SOCKET_EXHAUST_FLANGE", location=(0.0, 0.0, 0.0))
    add_socket("SOCKET_EXHAUST_BRACKET", location=bracket)

    return export_glb(
        os.path.join(out_dir, "Parts", "Exhaust", "pipe_stock_baffled.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def build_pipe_circuit_e50_proma(out_dir: str) -> Dict[str, object]:
    """Build looping under-frame Proma-style circuit torque pipe."""
    clear_scene()
    mb = MeshPartBuilder("pipe_circuit_e50_proma")
    _add_flange(mb, "mat_exhaust_steel")

    pts = [
        (0.0, 0.006, 0.0),
        (0.0, -0.065, -0.045),
        (0.042, -0.075, -0.165),
        (0.055, 0.080, -0.220),
        (0.015, 0.250, -0.230),
        (-0.045, 0.220, -0.210),
        (-0.065, 0.090, -0.190),
        (0.035, 0.130, -0.175),
        (0.092, 0.340, -0.165),
        (0.098, 0.460, -0.145),
    ]
    rads = [0.014, 0.016, 0.022, 0.034, 0.042, 0.040, 0.028, 0.018, 0.014, 0.012]
    mb.add_polyline_tube(pts, rads, segments=16, mat="mat_exhaust_steel")

    # Welded expansion cone seams
    for p, r in zip(pts[2:7], rads[2:7]):
        mb.add_torus(major_radius=r + 0.001, minor_radius=0.0022, major_segments=16, minor_segments=6, location=p, rotation=(1.5708, 0.0, 0.0), mat="mat_steel_dark")

    mb.add_cylinder(
        radius=0.026,
        depth=0.19,
        segments=16,
        location=(0.100, 0.555, -0.130),
        rotation=(1.42, 0.0, 0.0),
        mat="mat_anodized_alu",
    )
    mb.add_cylinder(
        radius=0.009,
        depth=0.030,
        segments=12,
        location=(0.100, 0.655, -0.115),
        rotation=(1.42, 0.0, 0.0),
        mat="mat_chrome",
    )
    bracket = (0.088, 0.44, -0.140)
    mb.add_box(size=(0.014, 0.026, 0.034), location=bracket, mat="mat_steel_dark")

    mb.build_object()
    add_socket("SOCKET_EXHAUST_FLANGE", location=(0.0, 0.0, 0.0))
    add_socket("SOCKET_EXHAUST_BRACKET", location=bracket)

    return export_glb(
        os.path.join(out_dir, "Parts", "Exhaust", "pipe_circuit_e50_proma.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def build_pipe_topend_expansion(out_dir: str) -> Dict[str, object]:
    """Build aggressive hydroformed high-RPM expansion chamber."""
    clear_scene()
    mb = MeshPartBuilder("pipe_topend_expansion")
    _add_flange(mb, "mat_exhaust_steel")

    pts = [
        (0.0, 0.006, 0.0),
        (0.0, -0.070, -0.035),
        (0.048, -0.085, -0.145),
        (0.088, 0.020, -0.205),
        (0.102, 0.140, -0.214),
        (0.104, 0.240, -0.214),
        (0.105, 0.340, -0.202),
        (0.102, 0.440, -0.180),
        (0.098, 0.520, -0.140),
    ]
    rads = [0.015, 0.018, 0.026, 0.042, 0.056, 0.056, 0.050, 0.030, 0.013]
    mb.add_polyline_tube(pts, rads, segments=18, mat="mat_exhaust_steel")

    for p, r in zip(pts[2:8], rads[2:8]):
        mb.add_torus(major_radius=r + 0.001, minor_radius=0.0024, major_segments=16, minor_segments=6, location=p, rotation=(1.5708, 0.0, 0.0), mat="mat_steel_dark")

    mb.add_cylinder(
        radius=0.028,
        depth=0.22,
        segments=16,
        location=(0.098, 0.625, -0.105),
        rotation=(1.28, 0.0, 0.0),
        mat="mat_anodized_gold",
    )
    mb.add_cylinder(
        radius=0.010,
        depth=0.032,
        segments=12,
        location=(0.098, 0.740, -0.070),
        rotation=(1.28, 0.0, 0.0),
        mat="mat_anodized_red",
    )
    bracket = (0.090, 0.46, -0.130)
    mb.add_box(size=(0.016, 0.028, 0.036), location=bracket, mat="mat_anodized_alu")

    mb.build_object()
    add_socket("SOCKET_EXHAUST_FLANGE", location=(0.0, 0.0, 0.0))
    add_socket("SOCKET_EXHAUST_BRACKET", location=bracket)

    return export_glb(
        os.path.join(out_dir, "Parts", "Exhaust", "pipe_topend_expansion.glb"),
        tri_budget=MAJOR_TRI_BUDGET,
    )


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        build_pipe_stock_baffled(out_dir),
        build_pipe_circuit_e50_proma(out_dir),
        build_pipe_topend_expansion(out_dir),
    ]

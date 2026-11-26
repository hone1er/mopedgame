"""Zone_OaklandHills modular kit: eucalyptus groves, winding hill road, overlook turnout, guardrail."""

from __future__ import annotations

import math
from typing import Dict, List

from common_utils import MeshPartBuilder, add_socket, clear_scene
from world.world_common import (
    add_ellipsoid,
    add_extrusion,
    add_snap_sockets,
    arc_frames,
    export_world_glb,
    straight_frames,
    sweep_profile,
)


ZONE = "OaklandHills"


def build_oh_eucalyptus_cluster(out_dir: str) -> Dict[str, object]:
    """Stylized cluster of tall, leaning blue-gum eucalyptus with droopy canopy clumps."""
    clear_scene()
    mb = MeshPartBuilder("oh_eucalyptus_cluster")

    # (base x, base y, height, lean x, lean y, trunk radius)
    trees = [
        (0.0, 0.0, 17.0, 0.9, -0.4, 0.42),
        (3.4, 1.8, 14.5, 1.6, 0.6, 0.34),
        (-2.9, 2.4, 15.5, -1.3, 0.9, 0.36),
        (1.2, -3.1, 12.5, 0.4, -1.5, 0.30),
    ]
    mb.add_cylinder(radius=5.6, depth=0.08, segments=16, location=(0.3, 0.6, 0.04), mat="mat_dry_grass", smooth=False)
    for tx, ty, th, lx, ly, tr in trees:
        pts = []
        radii = []
        for k in range(6):
            t = k / 5.0
            bend = t * t
            pts.append((tx + lx * bend, ty + ly * bend, th * 0.78 * t))
            radii.append(tr * (1.0 - 0.72 * t))
        mb.add_polyline_tube(pts, radii, segments=8, mat="mat_bark_eucalyptus", cap_start=False)
        mb.add_cone(radius1=tr * 1.5, radius2=tr * 0.95, depth=0.7, segments=8, location=(tx, ty, 0.35), mat="mat_bark_eucalyptus")
        # Peeling bark ribbons
        for k in range(3):
            ang = k * 2.1 + tx
            mb.add_box(
                size=(0.05, 0.22, 1.6),
                location=(tx + math.cos(ang) * tr * 0.95, ty + math.sin(ang) * tr * 0.95, 1.6 + k * 1.3),
                rotation=(0.0, 0.0, ang),
                mat="mat_wood_weathered",
            )
        top = pts[-1]
        # Branches and droopy canopy clumps at branch tips
        for b in range(4):
            ang = b * (math.pi * 0.5) + 0.4 * tx
            reach = 2.0 + 0.35 * b
            z0 = th * (0.5 + 0.08 * b)
            base = (tx + lx * 0.4, ty + ly * 0.4, z0)
            tip = (base[0] + math.cos(ang) * reach, base[1] + math.sin(ang) * reach, z0 + 1.6)
            mb.add_tapered_tube_between(base, tip, r0=tr * 0.38, r1=tr * 0.14, segments=6, mat="mat_bark_eucalyptus", cap_ends=False)
            add_ellipsoid(mb, 
                radius=1.0,
                u_segments=8,
                v_segments=5,
                location=(tip[0], tip[1], tip[2] - 0.4),
                scale=(1.6, 1.6, 2.4),
                mat="mat_foliage_eucalyptus" if b % 2 == 0 else "mat_foliage_sage",
                smooth=False,
            )
        add_ellipsoid(mb, 
            radius=1.0,
            u_segments=8,
            v_segments=6,
            location=(top[0], top[1], top[2] + 1.2),
            scale=(2.2, 2.2, 3.4),
            mat="mat_foliage_eucalyptus",
            smooth=False,
        )
    # Fallen bark strips and seed-pod scatter on the ground
    for k in range(6):
        ang = k * 1.05
        mb.add_box(size=(0.08, 1.4, 0.04), location=(math.cos(ang) * 3.2, math.sin(ang) * 3.2 + 0.4, 0.1), rotation=(0.0, 0.0, ang + 0.6), mat="mat_wood_weathered")

    mb.build_object()
    add_socket("SOCKET_GROUND_ANCHOR", location=(0.0, 0.0, 0.0))
    return export_world_glb(out_dir, ZONE, "oh_eucalyptus_cluster")


def build_oh_hill_road_20m(out_dir: str) -> Dict[str, object]:
    """20 m curved (R = 45 m) and sloped (10 % grade) two-lane hillside road with banked shoulders."""
    clear_scene()
    surf = MeshPartBuilder("oh_hill_road_20m_surface")
    solid = MeshPartBuilder("oh_hill_road_20m_details")
    frames = arc_frames(20.0, 45.0, 24, grade=0.10)

    # Left: uphill cut bank; right: downhill fill bank (left-to-right, up-facing winding)
    profile = [
        (-7.2, 1.6), (-5.6, 0.9), (-4.6, 0.12), (-4.3, 0.0), (-3.7, 0.0), (-3.58, 0.0),
        (-1.8, 0.0), (-0.16, 0.0), (-0.06, 0.0), (0.06, 0.0), (0.16, 0.0), (1.8, 0.0),
        (3.58, 0.0), (3.7, 0.0), (4.3, 0.0), (4.6, -0.12), (5.6, -0.9), (7.0, -2.2),
    ]
    mats = [
        "mat_dry_grass", "mat_dry_grass", "mat_concrete_dark", "mat_asphalt", "mat_road_paint_white",
        "mat_asphalt", "mat_asphalt", "mat_road_paint_yellow", "mat_asphalt", "mat_road_paint_yellow",
        "mat_asphalt", "mat_asphalt", "mat_road_paint_white", "mat_asphalt", "mat_concrete_dark",
        "mat_dry_grass", "mat_dry_grass",
    ]

    def cell_mat(iu, iv, c):
        # Weathered tar-seal patches and a scrubbed apex line on the inside lane
        if mats[iu] == "mat_asphalt" and iv in (5, 6, 15, 16) and iu in (5, 6):
            return "mat_asphalt_patch"
        if mats[iu] == "mat_asphalt" and iv in (10, 11, 19) and iu in (10, 11):
            return "mat_asphalt_patch"
        return None

    sweep_profile(surf, frames, profile, mats, cell_mat=cell_mat)
    surf.build_object(recalc_normals=False)

    # Reflective delineator posts along the downhill edge + cut-bank rocks + drain inlet
    for i in (2, 8, 14, 20):
        origin, lat, up = frames[i]
        p = origin + lat * 4.45
        solid.add_box(size=(0.1, 0.06, 1.0), location=(p.x, p.y, p.z + 0.4), mat="mat_ceramic_white")
        solid.add_box(size=(0.08, 0.07, 0.12), location=(p.x, p.y, p.z + 0.8), mat="mat_amber_plastic")
    for i, (u, s) in zip((4, 11, 18), ((-6.2, 0.9), (-6.6, 0.8), (-5.9, 1.1))):
        origin, lat, up = frames[i]
        p = origin + lat * u
        add_ellipsoid(solid, radius=s, u_segments=6, v_segments=4, location=(p.x, p.y, p.z + 1.1), scale=(1.2, 1.0, 0.6), mat="mat_concrete_dark", smooth=False)
    origin, lat, up = frames[12]
    p = origin + lat * -4.45
    solid.add_box(size=(0.6, 0.9, 0.1), location=(p.x, p.y, p.z + 0.08), mat="mat_cast_iron")
    for i in (6, 18):
        origin, lat, up = frames[i]
        p = origin + lat * 0.0
        solid.add_box(size=(0.1, 0.1, 0.02), location=(p.x, p.y, p.z + 0.01), mat="mat_amber_plastic")
    solid.build_object()

    add_snap_sockets(frames)
    return export_world_glb(out_dir, ZONE, "oh_hill_road_20m")


def build_oh_overlook_sign(out_dir: str) -> Dict[str, object]:
    """Scenic ridge overlook marker: routed wood sign, stone base, viewer, bench and turnout curb."""
    clear_scene()
    surf = MeshPartBuilder("oh_overlook_sign_turnout")
    mb = MeshPartBuilder("oh_overlook_sign")

    # Curved turnout curb (R = 9 m arc) swept as an up-facing profile, plus the turnout pad
    frames = arc_frames(11.0, -9.0, 14)
    curb_profile = [(-0.25, -0.2), (-0.25, 0.18), (0.0, 0.22), (0.25, 0.18), (0.25, -0.2)]
    sweep_profile(surf, frames, curb_profile, ["mat_concrete", "mat_concrete", "mat_concrete", "mat_concrete"])
    surf.build_object(recalc_normals=False)
    mb.add_box(size=(6.0, 11.0, 0.08), location=(-3.3, 0.0, 0.0), mat="mat_asphalt")

    # Stone base, posts, routed sign panel with ridge/bay icon
    mb.add_box(size=(2.8, 1.0, 0.7), location=(2.4, 0.0, 0.35), rotation=(0.0, 0.0, math.pi * 0.5), mat="mat_concrete_dark")
    for k in range(6):
        ang = k * 1.1
        add_ellipsoid(mb, radius=0.22, u_segments=6, v_segments=4, location=(2.4 + math.cos(ang) * 0.45, -1.2 + k * 0.48, 0.72), mat="mat_concrete", smooth=False)
    for sy in (-1.1, 1.1):
        mb.add_box(size=(0.16, 0.16, 2.3), location=(2.4, sy, 1.75), mat="mat_wood_weathered")
        mb.add_cone(radius1=0.13, radius2=0.0, depth=0.12, segments=4, location=(2.4, sy, 2.96), rotation=(0.0, 0.0, math.pi * 0.25), mat="mat_wood_weathered")
    mb.add_box(size=(0.1, 2.6, 1.0), location=(2.32, 0.0, 2.15), mat="mat_sign_brown")
    mb.add_box(size=(0.06, 2.4, 0.06), location=(2.26, 0.0, 2.58), mat="mat_enamel_cream")
    mb.add_box(size=(0.06, 2.4, 0.06), location=(2.26, 0.0, 1.72), mat="mat_enamel_cream")
    for k, (py, ph) in enumerate(((-0.75, 0.35), (-0.4, 0.5), (-0.05, 0.3))):
        mb.add_cone(radius1=0.22, radius2=0.0, depth=ph, segments=3, location=(2.25, py, 1.95 + ph * 0.5), rotation=(0.0, math.pi * 0.5, math.pi * 0.5), mat="mat_enamel_cream")
    for k in range(3):
        mb.add_box(size=(0.05, 0.9 - k * 0.2, 0.05), location=(2.26, 0.6, 2.32 - k * 0.15), mat="mat_enamel_cream")

    # Coin-op binocular viewer on pedestal
    vx, vy = 0.8, 3.0
    mb.add_cylinder(radius=0.25, depth=0.1, segments=12, location=(vx, vy, 0.05), mat="mat_steel_dark")
    mb.add_cylinder(radius=0.08, depth=1.1, segments=10, location=(vx, vy, 0.6), mat="mat_enamel_teal")
    mb.add_box(size=(0.3, 0.5, 0.35), location=(vx, vy, 1.3), mat="mat_enamel_teal")
    for sy in (-0.1, 0.1):
        mb.add_cylinder(radius=0.07, depth=0.3, segments=10, location=(vx + 0.25, vy + sy, 1.35), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_steel_dark")

    # Bench and short wood post fence along the drop side
    for sy in (-3.6, -2.4):
        mb.add_box(size=(0.4, 0.08, 0.45), location=(0.4, sy, 0.22), mat="mat_steel_dark")
    mb.add_box(size=(0.45, 1.6, 0.06), location=(0.4, -3.0, 0.46), mat="mat_wood_bench")
    mb.add_box(size=(0.06, 1.6, 0.35), location=(0.62, -3.0, 0.72), rotation=(0.0, 0.2, 0.0), mat="mat_wood_bench")
    for k in range(6):
        fy = -5.0 + k * 2.0
        mb.add_cylinder(radius=0.09, depth=1.0, segments=8, location=(3.6, fy, 0.5), mat="mat_wood_weathered")
    mb.add_box(size=(0.08, 10.0, 0.1), location=(3.6, 0.0, 0.85), mat="mat_wood_weathered")
    mb.add_box(size=(0.08, 10.0, 0.1), location=(3.6, 0.0, 0.5), mat="mat_wood_weathered")

    mb.build_object()
    add_socket("SOCKET_VIEWPOINT", location=(vx, vy, 1.4))
    add_socket("SOCKET_TURNOUT_ENTRY", location=(-3.3, -5.5, 0.04))
    return export_world_glb(out_dir, ZONE, "oh_overlook_sign")


def build_oh_guardrail_10m(out_dir: str) -> Dict[str, object]:
    """10 m galvanized steel W-beam guardrail run on I-beam posts with wood blockouts."""
    clear_scene()
    mb = MeshPartBuilder("oh_guardrail_10m")
    rail_z = 0.70
    length = 10.0

    # W-beam cross-section (two corrugation peaks toward the road at -X), extruded along Y
    w_line = [(-0.02, -0.155), (-0.083, -0.095), (-0.083, -0.065), (0.0, -0.015),
              (0.0, 0.015), (-0.083, 0.065), (-0.083, 0.095), (-0.02, 0.155)]
    thickness = 0.012
    section = w_line + [(x + thickness, z) for x, z in reversed(w_line)]
    add_extrusion(mb, section, -0.5 * length, 0.5 * length, "mat_galvanized", offset=(-0.2, 0.0, rail_z + 0.04))

    posts_y = [-4.0, -2.0, 0.0, 2.0, 4.0]
    for py in posts_y:
        # I-beam post: two flanges + web
        for fx in (-0.05, 0.05):
            mb.add_box(size=(0.012, 0.15, 1.1), location=(fx, py, 0.45), mat="mat_galvanized")
        mb.add_box(size=(0.1, 0.01, 1.1), location=(0.0, py, 0.45), mat="mat_galvanized")
        mb.add_box(size=(0.15, 0.15, 0.36), location=(-0.12, py, rail_z + 0.04), mat="mat_wood_weathered")
        for bz in (rail_z - 0.03, rail_z + 0.11):
            mb.add_hex_bolt(radius=0.018, height=0.03, location=(-0.29, py, bz), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_galvanized")
        mb.add_box(size=(0.03, 0.06, 0.08), location=(-0.21, py, rail_z + 0.25), mat="mat_amber_plastic")
    # Splice plates & bolts at the run joints
    for sy in (-4.9, 4.9):
        mb.add_box(size=(0.02, 0.3, 0.3), location=(-0.27, sy, rail_z + 0.04), mat="mat_galvanized")
        for bz in (rail_z - 0.04, rail_z + 0.12):
            for by in (-0.08, 0.08):
                mb.add_hex_bolt(radius=0.012, height=0.025, location=(-0.29, sy + by, bz), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_galvanized")
    # Gravel shoulder strip under the run
    mb.add_box(size=(0.8, length, 0.05), location=(-0.05, 0.0, 0.0), mat="mat_concrete_dark")

    mb.build_object()
    frames = straight_frames(length, 2)
    add_snap_sockets(frames)
    return export_world_glb(out_dir, ZONE, "oh_guardrail_10m")


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        build_oh_eucalyptus_cluster(out_dir),
        build_oh_hill_road_20m(out_dir),
        build_oh_overlook_sign(out_dir),
        build_oh_guardrail_10m(out_dir),
    ]

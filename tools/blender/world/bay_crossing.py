"""Zone_BayCrossing modular kit: stylized suspension tower, causeway service deck, ferry dock."""

from __future__ import annotations

import math
from typing import Dict, List

from common_utils import MeshPartBuilder, add_socket, clear_scene
from world.world_common import (
    add_ellipsoid,
    add_snap_sockets,
    export_world_glb,
    straight_frames,
    street_profile,
    sweep_profile,
)


ZONE = "BayCrossing"


def build_bc_bridge_tower(out_dir: str) -> Dict[str, object]:
    """Stylized 28 m steel suspension tower: tapered twin legs, portal struts, X-bracing, saddles."""
    clear_scene()
    mb = MeshPartBuilder("bc_bridge_tower")
    leg_x = 6.0
    top_z = 27.0

    # Concrete pier with cutwater ends and fender band
    mb.add_box(size=(16.0, 5.0, 3.0), location=(0.0, 0.0, 1.5), mat="mat_concrete")
    for sx in (-8.0, 8.0):
        mb.add_cylinder(radius=2.5, depth=3.0, segments=12, location=(sx, 0.0, 1.5), mat="mat_concrete", smooth=False)
    mb.add_box(size=(16.2, 5.2, 0.4), location=(0.0, 0.0, 0.6), mat="mat_concrete_dark")

    # Tapered square legs (4-sided cones rotated 45 deg) with stepped setbacks
    for sx in (-leg_x, leg_x):
        mb.add_box(size=(2.4, 3.2, 0.6), location=(sx, 0.0, 3.3), mat="mat_steel_dark")
        mb.add_cone(radius1=1.55, radius2=1.05, depth=top_z - 3.6, segments=4, location=(sx, 0.0, 3.6 + 0.5 * (top_z - 3.6)), rotation=(0.0, 0.0, math.pi * 0.25), mat="mat_bridge_grey", smooth=False)
        for z in (9.0, 15.0, 21.0):
            frac = (z - 3.6) / (top_z - 3.6)
            r = 1.55 + (1.05 - 1.55) * frac
            mb.add_box(size=(r * 1.414 + 0.12, r * 1.414 + 0.12, 0.25), location=(sx, 0.0, z), mat="mat_steel_dark")
        # Vertical stiffener ribs on the outer face
        mb.add_box(size=(0.12, 0.25, top_z - 4.0), location=(sx + math.copysign(0.95, sx), 0.0, 3.6 + 0.5 * (top_z - 4.0)), rotation=(0.0, math.copysign(0.02, sx), 0.0), mat="mat_steel_dark")

    # Portal struts with decorative stepped crowns
    for z, depth in ((8.0, 1.6), (16.0, 1.3), (23.5, 1.1)):
        mb.add_box(size=(2.0 * leg_x - 1.4, 1.6, depth), location=(0.0, 0.0, z), mat="mat_bridge_grey")
        mb.add_box(size=(2.0 * leg_x - 1.2, 1.7, 0.18), location=(0.0, 0.0, z + 0.5 * depth + 0.09), mat="mat_steel_dark")
        for k in range(5):
            mb.add_box(size=(0.6, 1.72, depth * 0.6), location=(-4.0 + k * 2.0, 0.0, z), mat="mat_steel_dark")
    # X-bracing panels between struts (front and back faces)
    for z0, z1 in ((8.8, 15.3), (16.7, 22.9)):
        for fy in (-0.7, 0.7):
            mb.add_tube_between((-leg_x + 0.9, fy, z0), (leg_x - 0.9, fy, z1), radius=0.16, segments=6, mat="mat_bridge_grey", smooth=False)
            mb.add_tube_between((leg_x - 0.9, fy, z0), (-leg_x + 0.9, fy, z1), radius=0.16, segments=6, mat="mat_bridge_grey", smooth=False)
            mb.add_box(size=(0.9, 0.12, 0.9), location=(0.0, fy, 0.5 * (z0 + z1)), rotation=(0.0, math.pi * 0.25, 0.0), mat="mat_steel_dark")

    # Top caps, cable saddles, main cable stubs and aviation beacons
    for sx in (-leg_x, leg_x):
        mb.add_box(size=(1.9, 2.4, 0.5), location=(sx, 0.0, top_z + 0.25), mat="mat_steel_dark")
        mb.add_cylinder(radius=0.75, depth=1.0, segments=12, location=(sx, 0.0, top_z + 0.6), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_bridge_grey")
        for dy in (-1.0, 1.0):
            mb.add_tube_between((sx, 0.0, top_z + 1.2), (sx, dy * 9.0, top_z - 6.0), radius=0.32, segments=8, mat="mat_steel_dark")
            for k in range(1, 4):
                t = k / 4.0
                mb.add_cylinder(radius=0.4, depth=0.25, segments=8, location=(sx, dy * 9.0 * t, top_z + 1.2 - 7.2 * t), rotation=(math.atan2(9.0, 7.2) * -dy, 0.0, 0.0), mat="mat_bridge_grey")
                mb.add_tube_between((sx, dy * 9.0 * t, top_z + 1.2 - 7.2 * t), (sx, dy * 9.0 * t, 8.6), radius=0.05, segments=4, mat="mat_steel_dark", smooth=False)
        mb.add_cylinder(radius=0.08, depth=0.8, segments=6, location=(sx, 0.0, top_z + 1.5), mat="mat_steel_dark")
        add_ellipsoid(mb, radius=0.18, u_segments=8, v_segments=4, location=(sx, 0.0, top_z + 1.95), mat="mat_enamel_red")

    # Maintenance ladder cage on one leg
    for k in range(10):
        mb.add_box(size=(0.6, 0.05, 0.05), location=(-leg_x, -1.15 - 0.02 * k, 4.5 + k * 2.0), mat="mat_galvanized")

    mb.build_object()
    add_socket("SOCKET_DECK_LEVEL", location=(0.0, 0.0, 8.8))
    add_socket("SOCKET_CABLE_SADDLE_L", location=(-leg_x, 0.0, top_z + 1.2))
    add_socket("SOCKET_CABLE_SADDLE_R", location=(leg_x, 0.0, top_z + 1.2))
    return export_world_glb(out_dir, ZONE, "bc_bridge_tower")


def build_bc_causeway_deck_20m(out_dir: str) -> Dict[str, object]:
    """20 m bridge deck: green bike lane + service lane, curbs, rails, girders and lamp posts."""
    clear_scene()
    surf = MeshPartBuilder("bc_causeway_deck_20m_surface")
    mb = MeshPartBuilder("bc_causeway_deck_20m_structure")
    frames = straight_frames(20.0, 10)

    paint = [
        (-3.4, -1.6, "mat_road_paint_green"),
        (-1.6, -1.45, "mat_road_paint_white"),
        (-0.08, 0.08, "mat_road_paint_yellow"),
        (3.3, 3.42, "mat_road_paint_white"),
    ]
    profile, mats = street_profile(3.6, 0.9, curb_h=0.2, paint=paint, skirt=1.1, walk="mat_concrete", curb="mat_concrete")

    def cell_mat(iu, iv, c):
        # Dashed service-lane centerline (2 m dash / 2 m gap)
        if mats[iu] == "mat_road_paint_yellow" and iv % 2 == 1:
            return "mat_asphalt"
        return None

    sweep_profile(surf, frames, profile, mats, cell_mat=cell_mat)
    surf.build_object(recalc_normals=False)

    edge = 4.5
    # Steel plate girders, cross diaphragms and bottom flange
    for gx in (-2.6, 2.6):
        mb.add_box(size=(0.3, 20.0, 1.8), location=(gx, 0.0, -2.0), mat="mat_bridge_grey")
        mb.add_box(size=(0.9, 20.0, 0.12), location=(gx, 0.0, -2.9), mat="mat_bridge_grey")
        mb.add_box(size=(0.9, 20.0, 0.12), location=(gx, 0.0, -1.1), mat="mat_bridge_grey")
    for k in range(5):
        dy = -8.0 + k * 4.0
        mb.add_box(size=(5.0, 0.2, 1.2), location=(0.0, dy, -2.0), mat="mat_steel_dark")
    mb.add_box(size=(9.0, 20.0, 0.3), location=(0.0, 0.0, -0.85), mat="mat_concrete_dark")

    # Rails: posts every 2.5 m, top + mid rails, toe plate
    for side in (-1.0, 1.0):
        x = side * (edge - 0.12)
        for k in range(8):
            mb.add_box(size=(0.08, 0.08, 1.15), location=(x, -8.75 + k * 2.5, 0.2 + 0.575), mat="mat_galvanized")
        mb.add_cylinder(radius=0.045, depth=20.0, segments=8, location=(x, 0.0, 1.35), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_galvanized")
        mb.add_cylinder(radius=0.03, depth=20.0, segments=6, location=(x, 0.0, 0.85), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_galvanized")
        mb.add_box(size=(0.03, 20.0, 0.15), location=(x, 0.0, 0.28), mat="mat_galvanized")
        # Lamp post with cantilever luminaire at mid-span
        mb.add_cylinder(radius=0.09, depth=5.2, segments=8, location=(side * (edge + 0.1), 0.0, 2.6), mat="mat_bridge_grey")
        mb.add_tube_between((side * (edge + 0.1), 0.0, 5.1), (side * (edge - 1.2), 0.0, 5.4), radius=0.05, segments=6, mat="mat_bridge_grey")
        mb.add_box(size=(0.6, 0.3, 0.12), location=(side * (edge - 1.3), 0.0, 5.35), mat="mat_steel_dark")
        mb.add_box(size=(0.5, 0.24, 0.03), location=(side * (edge - 1.3), 0.0, 5.28), mat="mat_glass_lens")
        mb.add_box(size=(0.5, 0.5, 0.25), location=(side * (edge + 0.1), 0.0, -0.6), mat="mat_bridge_grey")
    # Expansion joint plates
    for y in (-9.95, 9.95):
        mb.add_box(size=(7.2, 0.1, 0.02), location=(0.0, y, 0.005), mat="mat_steel_dark")

    mb.build_object()
    add_snap_sockets(frames)
    return export_world_glb(out_dir, ZONE, "bc_causeway_deck_20m")


def build_bc_ferry_terminal_dock(out_dir: str) -> Dict[str, object]:
    """Bay ferry pier segment: plank deck on pilings, sawtooth awning, bollards, gangway, sign pylon."""
    clear_scene()
    mb = MeshPartBuilder("bc_ferry_terminal_dock")
    dw, dl = 10.0, 14.0

    # Plank deck (top at z = 0), stringers, pilings into the water
    mb.add_box(size=(dw, dl, 0.25), location=(0.0, 0.0, -0.2), mat="mat_wood_weathered")
    for k in range(14):
        mb.add_box(size=(dw - 0.1, 0.94, 0.06), location=(0.0, -0.5 * dl + 0.5 + k * 1.0, -0.04), mat="mat_wood_bench")
    for sx in (-4.4, -1.5, 1.5, 4.4):
        mb.add_box(size=(0.3, dl, 0.4), location=(sx, 0.0, -0.5), mat="mat_wood_weathered")
    for px in (-4.6, 0.0, 4.6):
        for py in (-6.4, -2.2, 2.2, 6.4):
            mb.add_cylinder(radius=0.22, depth=3.6, segments=10, location=(px, py, -1.9), mat="mat_wood_weathered")
    for py in (-6.4, -2.2, 2.2, 6.4):
        mb.add_cylinder(radius=0.25, depth=0.5, segments=10, location=(4.85, py, -0.4), mat="mat_rubber")
    # Edge timber + rub rail on the berth side (+X)
    mb.add_box(size=(0.3, dl, 0.25), location=(4.9, 0.0, 0.1), mat="mat_wood_weathered")
    mb.add_box(size=(0.2, dl, 0.2), location=(5.1, 0.0, -0.3), mat="mat_rubber")

    # Awning: steel posts + pitched corrugated canopy panels + gutter
    for px in (-3.8, 0.4):
        for py in (-5.0, -1.7, 1.7, 5.0):
            mb.add_cylinder(radius=0.09, depth=3.4, segments=8, location=(px, py, 1.7), mat="mat_enamel_teal")
    for py in (-5.0, -1.7, 1.7, 5.0):
        mb.add_box(size=(5.6, 0.12, 0.25), location=(-1.7, py, 3.45), mat="mat_enamel_teal")
    for k in range(3):
        cy = -3.35 + k * 3.35
        mb.add_box(size=(2.9, 3.4, 0.08), location=(-3.0, cy, 3.85), rotation=(0.0, -0.28, 0.0), mat="mat_corrugated_steel")
        mb.add_box(size=(2.9, 3.4, 0.08), location=(-0.4, cy, 3.85), rotation=(0.0, 0.28, 0.0), mat="mat_corrugated_steel")
    mb.add_box(size=(0.2, 10.2, 0.15), location=(1.0, 0.0, 3.45), mat="mat_galvanized")

    # Benches under the awning
    for by in (-3.3, 0.0, 3.3):
        mb.add_box(size=(0.5, 2.0, 0.06), location=(-3.0, by, 0.45), mat="mat_wood_bench")
        mb.add_box(size=(0.06, 2.0, 0.4), location=(-3.25, by, 0.7), mat="mat_wood_bench")
        for ly in (-0.85, 0.85):
            mb.add_box(size=(0.45, 0.06, 0.42), location=(-3.0, by + ly, 0.21), mat="mat_steel_dark")

    # Bollards and cleats along the berth edge
    for py in (-5.5, -1.8, 1.8, 5.5):
        mb.add_cylinder(radius=0.16, depth=0.55, segments=12, location=(4.4, py, 0.275), mat="mat_cast_iron")
        mb.add_cylinder(radius=0.22, depth=0.1, segments=12, location=(4.4, py, 0.58), mat="mat_cast_iron")
        mb.add_box(size=(0.12, 0.5, 0.1), location=(4.5, py + 0.9, 0.12), mat="mat_cast_iron")

    # Gangway ramp, handrails, life ring post, wayfinding pylon
    mb.add_box(size=(1.4, 4.0, 0.12), location=(6.1, 3.2, -0.4), rotation=(0.0, 0.0, math.pi * 0.5), mat="mat_galvanized")
    for gy in (2.55, 3.85):
        mb.add_tube_between((4.9, gy, 0.9), (7.4, gy, 0.5), radius=0.035, segments=6, mat="mat_galvanized")
    mb.add_box(size=(0.1, 0.1, 1.6), location=(3.6, -6.6, 0.8), mat="mat_enamel_red")
    mb.add_torus(major_radius=0.32, minor_radius=0.07, major_segments=14, minor_segments=6, location=(3.6, -6.7, 1.2), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_enamel_orange")
    mb.add_box(size=(0.3, 1.0, 2.6), location=(1.6, -6.2, 1.3), mat="mat_enamel_teal")
    mb.add_box(size=(0.32, 0.8, 0.5), location=(1.6, -6.2, 2.2), mat="mat_enamel_cream")
    mb.add_box(size=(0.32, 0.8, 0.12), location=(1.6, -6.2, 1.6), mat="mat_enamel_yellow")

    mb.build_object()
    add_socket("SOCKET_BERTH", location=(5.2, 0.0, 0.0))
    add_socket("SOCKET_GANGWAY", location=(7.4, 3.2, -0.4))
    return export_world_glb(out_dir, ZONE, "bc_ferry_terminal_dock")


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        build_bc_bridge_tower(out_dir),
        build_bc_causeway_deck_20m(out_dir),
        build_bc_ferry_terminal_dock(out_dir),
    ]

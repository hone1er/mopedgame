"""Zone_WestOakland modular kit: brick warehouses, BART viaduct, moped shop, taco truck, flats road."""

from __future__ import annotations

import math
from typing import Dict, List

from common_utils import MeshPartBuilder, add_socket, clear_scene
from world.world_common import (
    add_snap_sockets,
    export_world_glb,
    straight_frames,
    street_profile,
    sweep_profile,
)


ZONE = "WestOakland"


def build_wo_warehouse_brick(out_dir: str) -> Dict[str, object]:
    """Industrial brick warehouse block with sawtooth roof and front loading bay (front = -Y)."""
    clear_scene()
    mb = MeshPartBuilder("wo_warehouse_brick")
    w, d, h = 24.0, 16.0, 8.0
    front = -0.5 * d

    mb.add_box(size=(w + 0.3, d + 0.3, 0.7), location=(0.0, 0.0, 0.35), mat="mat_concrete")
    mb.add_box(size=(w, d, h), location=(0.0, 0.0, 0.5 * h), mat="mat_brick_red")
    mb.add_box(size=(w + 0.4, d + 0.4, 0.45), location=(0.0, 0.0, h + 0.1), mat="mat_brick_dark")
    mb.add_box(size=(w + 0.2, d + 0.2, 0.18), location=(0.0, 0.0, 3.9), mat="mat_brick_dark")

    # Pilasters on all four faces
    for i in range(7):
        x = -12.0 + i * 4.0
        for y in (front - 0.12, -front + 0.12):
            mb.add_box(size=(0.55, 0.28, h), location=(x, y, 0.5 * h), mat="mat_brick_dark")
    for j in range(1, 4):
        y = front + j * 4.0
        for x in (-0.5 * w - 0.12, 0.5 * w + 0.12):
            mb.add_box(size=(0.28, 0.55, h), location=(x, y, 0.5 * h), mat="mat_brick_dark")

    # Upper clerestory windows (front + back) with concrete sills and steel mullions
    for i in range(6):
        x = -10.0 + i * 4.0
        for side in (-1.0, 1.0):
            y = side * (0.5 * d + 0.03)
            mb.add_box(size=(2.4, 0.12, 1.8), location=(x, y, 5.9), mat="mat_window_glass")
            mb.add_box(size=(2.7, 0.30, 0.16), location=(x, side * (0.5 * d + 0.1), 4.92), mat="mat_concrete")
            mb.add_box(size=(2.6, 0.22, 0.22), location=(x, side * (0.5 * d + 0.06), 6.9), mat="mat_brick_dark")
            mb.add_box(size=(0.06, 0.16, 1.8), location=(x, y, 5.9), mat="mat_steel_dark")
            mb.add_box(size=(2.4, 0.16, 0.06), location=(x, y, 5.9), mat="mat_steel_dark")
    # Ground-floor side windows (left/right faces)
    for j in range(3):
        y = front + 4.0 + j * 4.0
        for side in (-1.0, 1.0):
            x = side * (0.5 * w + 0.03)
            mb.add_box(size=(0.12, 2.0, 1.6), location=(x, y, 2.4), mat="mat_window_glass")
            mb.add_box(size=(0.30, 2.3, 0.16), location=(side * (0.5 * w + 0.1), y, 1.52), mat="mat_concrete")

    # Sawtooth north-light roof: 4 teeth, pitched slabs + vertical glazing strips
    pitch = math.radians(24.0)
    tooth = d / 4.0
    slab_len = tooth / math.cos(pitch)
    rise = tooth * math.tan(pitch)
    for k in range(4):
        y0 = front + k * tooth
        mb.add_box(
            size=(w - 0.2, slab_len, 0.18),
            location=(0.0, y0 + 0.5 * tooth, h + 0.3 + 0.5 * rise),
            rotation=(pitch, 0.0, 0.0),
            mat="mat_corrugated_steel",
        )
        mb.add_box(size=(w - 0.6, 0.10, rise - 0.15), location=(0.0, y0 + tooth - 0.05, h + 0.3 + 0.5 * rise), mat="mat_window_glass")
        mb.add_box(size=(w - 0.2, 0.2, 0.2), location=(0.0, y0 + tooth - 0.05, h + 0.3 + rise), mat="mat_steel_dark")
    for side in (-1.0, 1.0):
        for k in range(4):
            y0 = front + k * tooth
            mb.add_box(size=(0.25, tooth, rise * 0.5), location=(side * (0.5 * w - 0.1), y0 + 0.75 * tooth, h + 0.3 + rise * 0.5), mat="mat_brick_dark")

    # Loading bay (front right): dock platform, roll-up door with ribs, bumpers, canopy, stairs
    bay_x = 6.0
    mb.add_box(size=(7.0, 2.4, 1.2), location=(bay_x, front - 1.2, 0.6), mat="mat_concrete")
    mb.add_box(size=(7.1, 0.12, 0.12), location=(bay_x, front - 2.4, 1.18), mat="mat_enamel_yellow")
    mb.add_box(size=(4.6, 0.16, 4.4), location=(bay_x, front - 0.06, 3.4), mat="mat_corrugated_steel")
    for r in range(10):
        mb.add_box(size=(4.6, 0.06, 0.06), location=(bay_x, front - 0.16, 1.4 + r * 0.42), mat="mat_steel_dark")
    mb.add_box(size=(5.0, 0.3, 0.3), location=(bay_x, front - 0.15, 5.75), mat="mat_steel_dark")
    for sx in (-2.5, 2.5):
        mb.add_box(size=(0.25, 0.3, 4.6), location=(bay_x + sx, front - 0.15, 3.4), mat="mat_enamel_yellow")
        mb.add_box(size=(0.35, 0.25, 0.5), location=(bay_x + sx * 0.6, front - 2.5, 0.85), mat="mat_rubber")
    mb.add_box(size=(7.6, 2.8, 0.14), location=(bay_x, front - 1.3, 6.3), rotation=(-0.12, 0.0, 0.0), mat="mat_corrugated_steel")
    for sx in (-3.6, 3.6):
        mb.add_tube_between((bay_x + sx, front - 0.1, 6.3), (bay_x + sx, front - 2.6, 6.0), radius=0.05, segments=6, mat="mat_steel_dark")
    for st in range(4):
        mb.add_box(size=(1.2, 0.32, 0.3 * (st + 1)), location=(bay_x - 4.1, front - 2.2 + st * 0.32, 0.15 * (st + 1)), mat="mat_concrete_dark")
    for sx in (-4.6, 4.6):
        mb.add_cylinder(radius=0.14, depth=1.1, segments=10, location=(bay_x + sx, front - 3.0, 0.55), mat="mat_enamel_yellow")

    # Personnel door + gooseneck lamps + downspouts
    mb.add_box(size=(1.1, 0.14, 2.3), location=(-6.0, front - 0.05, 1.85), mat="mat_enamel_teal")
    mb.add_box(size=(1.5, 0.5, 0.12), location=(-6.0, front - 0.3, 3.15), mat="mat_steel_dark")
    for lx in (-6.0, -1.0):
        mb.add_polyline_tube(
            [(lx, front - 0.05, 4.4), (lx, front - 0.5, 4.6), (lx, front - 0.7, 4.4)],
            [0.04, 0.04, 0.04],
            segments=6,
            mat="mat_steel_dark",
        )
        mb.add_cone(radius1=0.22, radius2=0.06, depth=0.2, segments=10, location=(lx, front - 0.75, 4.3), mat="mat_enamel_teal")
    for x in (-11.8, 11.8):
        mb.add_cylinder(radius=0.08, depth=h, segments=8, location=(x, front - 0.2, 0.5 * h), mat="mat_galvanized")

    # Rooftop HVAC unit
    mb.add_box(size=(2.2, 1.6, 1.2), location=(-7.0, 2.0, h + 1.9), mat="mat_galvanized")
    mb.add_cylinder(radius=0.5, depth=0.25, segments=14, location=(-7.0, 2.0, h + 2.6), mat="mat_steel_dark")

    mb.build_object()
    add_socket("SOCKET_LOADING_BAY", location=(bay_x, front - 2.4, 1.2))
    add_socket("SOCKET_ENTRANCE", location=(-6.0, front - 0.4, 0.7))
    return export_world_glb(out_dir, ZONE, "wo_warehouse_brick")


def build_wo_road_pothole_20m(out_dir: str) -> Dict[str, object]:
    """20 m flats road slab (2 lanes + sidewalks) with depressed potholes and patched asphalt."""
    clear_scene()
    surf = MeshPartBuilder("wo_road_pothole_20m_surface")
    solid = MeshPartBuilder("wo_road_pothole_20m_details")
    half_road = 5.0
    paint = [
        (-4.62, -4.5, "mat_road_paint_white"),
        (4.5, 4.62, "mat_road_paint_white"),
        (-0.16, -0.06, "mat_road_paint_yellow"),
        (0.06, 0.16, "mat_road_paint_yellow"),
    ]
    breaks = [-4.0 + 0.5 * i for i in range(17)]
    profile, mats = street_profile(half_road, 2.4, paint=paint, extra_breaks=breaks)
    frames = straight_frames(20.0, 32)

    # (x, y, radius, depth) potholes and (x0, x1, y0, y1) patched rectangles
    potholes = [(-2.4, -5.6, 0.75, 0.09), (2.1, 3.4, 0.6, 0.07), (-1.2, 7.1, 0.5, 0.06), (3.3, -1.8, 0.45, 0.05)]
    patches = [(-3.9, -0.6, -1.0, 1.6), (0.7, 3.6, -8.5, -6.2), (-4.4, -2.6, 4.6, 5.9)]

    def displace(p, u, h) -> float:
        if h != 0.0 or abs(u) >= half_road - 1e-4:
            return 0.0
        dz = 0.0
        for px, py, r, depth in potholes:
            dist2 = ((p.x - px) ** 2 + (p.y - py) ** 2) / (r * r)
            if dist2 < 1.0:
                dz = min(dz, -depth * (1.0 - dist2))
        return dz

    def cell_mat(iu, iv, c):
        if abs(c.x) >= half_road - 1e-4 or abs(c.z) > 0.12:
            return None
        for px, py, r, _ in potholes:
            if (c.x - px) ** 2 + (c.y - py) ** 2 < (r * 1.25) ** 2:
                return "mat_asphalt_patch"
        for x0, x1, y0, y1 in patches:
            if x0 <= c.x <= x1 and y0 <= c.y <= y1:
                return "mat_asphalt_patch"
        return None

    sweep_profile(surf, frames, profile, mats, displace=displace, cell_mat=cell_mat)
    surf.build_object(recalc_normals=False)

    # Manhole covers, storm drain grates, crack-seal tar strips (slightly proud of surface)
    for mx, my in ((1.6, -2.5), (-3.0, 8.4)):
        solid.add_cylinder(radius=0.38, depth=0.03, segments=16, location=(mx, my, 0.005), mat="mat_cast_iron", smooth=False)
        solid.add_torus(major_radius=0.30, minor_radius=0.012, major_segments=16, minor_segments=4, location=(mx, my, 0.02), mat="mat_steel_dark")
    for gy in (-7.0, 6.0):
        solid.add_box(size=(0.5, 0.9, 0.04), location=(-4.72, gy, 0.0), mat="mat_cast_iron")
        for k in range(5):
            solid.add_box(size=(0.42, 0.05, 0.03), location=(-4.72, gy - 0.32 + k * 0.16, 0.02), mat="mat_steel_dark")
    for sx, sy, ln, ang in ((-0.9, -3.2, 3.4, 0.5), (2.8, 0.8, 2.6, -0.7), (-3.2, 2.8, 2.2, 1.1), (1.2, 8.2, 2.8, 0.25)):
        solid.add_box(size=(0.07, ln, 0.012), location=(sx, sy, 0.004), rotation=(0.0, 0.0, ang), mat="mat_asphalt_patch")
    # Sidewalk expansion joints and a utility pole stub on the right sidewalk
    for k in range(-4, 5):
        for side in (-1.0, 1.0):
            solid.add_box(size=(2.4, 0.05, 0.01), location=(side * (half_road + 1.2), k * 2.25, 0.152), mat="mat_concrete_dark")
    solid.add_cylinder(radius=0.16, depth=1.0, segments=10, location=(6.8, 4.0, 0.65), mat="mat_wood_weathered")
    solid.build_object()

    add_snap_sockets(frames)
    return export_world_glb(out_dir, ZONE, "wo_road_pothole_20m")


def build_wo_bart_overpass_pillar(out_dir: str) -> Dict[str, object]:
    """Concrete elevated-rail viaduct column with hammerhead cap and a 20 m twin-track deck span."""
    clear_scene()
    mb = MeshPartBuilder("wo_bart_overpass_pillar")
    deck_z = 9.0

    # Footing, column shaft with chamfered corners, flared capital, hammerhead cap beam
    mb.add_box(size=(3.6, 4.2, 0.6), location=(0.0, 0.0, 0.3), mat="mat_concrete_dark")
    mb.add_box(size=(1.6, 2.4, 6.4), location=(0.0, 0.0, 3.8), mat="mat_concrete")
    for sx in (-0.8, 0.8):
        for sy in (-1.2, 1.2):
            mb.add_box(size=(0.22, 0.22, 6.4), location=(sx, sy, 3.8), rotation=(0.0, 0.0, math.pi * 0.25), mat="mat_concrete")
    for k, (wx, wy) in enumerate(((2.0, 2.8), (3.0, 3.2), (4.4, 3.4))):
        mb.add_box(size=(wx, wy, 0.4), location=(0.0, 0.0, 7.2 + k * 0.4), mat="mat_concrete")
    mb.add_box(size=(8.6, 2.6, 0.9), location=(0.0, 0.0, 8.3), mat="mat_concrete")
    for bx in (-3.0, -1.0, 1.0, 3.0):
        mb.add_box(size=(0.6, 0.6, 0.12), location=(bx, 0.0, 8.81), mat="mat_rubber")
    # Column joint lines + utility conduit + downspout
    for z in (2.0, 4.0, 6.0):
        mb.add_box(size=(1.64, 2.44, 0.04), location=(0.0, 0.0, z), mat="mat_concrete_dark")
    mb.add_cylinder(radius=0.09, depth=8.2, segments=8, location=(0.95, 0.9, 4.4), mat="mat_galvanized")
    mb.add_cylinder(radius=0.07, depth=8.2, segments=8, location=(-0.95, -0.9, 4.4), mat="mat_steel_dark")

    # Deck span: box girder, soffit, fascia, parapets, walkway, rails and third-rail covers
    mb.add_box(size=(6.0, 20.0, 1.4), location=(0.0, 0.0, deck_z), mat="mat_concrete")
    mb.add_box(size=(8.8, 20.0, 0.35), location=(0.0, 0.0, deck_z + 0.85), mat="mat_concrete")
    for side in (-1.0, 1.0):
        mb.add_box(size=(0.25, 20.0, 1.1), location=(side * 4.275, 0.0, deck_z + 1.55), mat="mat_concrete")
        mb.add_box(size=(0.35, 20.0, 0.12), location=(side * 4.275, 0.0, deck_z + 2.16), mat="mat_concrete_dark")
        mb.add_box(size=(0.6, 20.0, 0.5), location=(side * 3.35, 0.0, deck_z + 1.27), mat="mat_concrete_dark")
        for k in range(5):
            mb.add_box(size=(0.05, 0.05, 0.8), location=(side * 4.05, -8.0 + k * 4.0, deck_z + 2.5), mat="mat_galvanized")
        mb.add_box(size=(0.06, 20.0, 0.06), location=(side * 4.05, 0.0, deck_z + 2.9), mat="mat_galvanized")
    for track_x in (-1.55, 1.55):
        mb.add_box(size=(2.4, 20.0, 0.18), location=(track_x, 0.0, deck_z + 1.11), mat="mat_concrete_dark")
        for rx in (-0.84, 0.84):
            mb.add_box(size=(0.08, 20.0, 0.16), location=(track_x + rx, 0.0, deck_z + 1.28), mat="mat_steel_dark")
            mb.add_box(size=(0.05, 20.0, 0.03), location=(track_x + rx, 0.0, deck_z + 1.375), mat="mat_chrome")
        mb.add_box(size=(0.22, 20.0, 0.14), location=(track_x + math.copysign(1.2, track_x), 0.0, deck_z + 1.27), mat="mat_ceramic_white")
    # Expansion joints at span ends
    for y in (-9.95, 9.95):
        mb.add_box(size=(8.82, 0.1, 0.05), location=(0.0, y, deck_z + 1.04), mat="mat_steel_dark")

    mb.build_object()
    add_socket("SOCKET_DECK_START", location=(0.0, -10.0, deck_z + 1.2))
    add_socket("SOCKET_DECK_END", location=(0.0, 10.0, deck_z + 1.2))
    return export_world_glb(out_dir, ZONE, "wo_bart_overpass_pillar")


def build_wo_garage_facade(out_dir: str) -> Dict[str, object]:
    """West Oakland moped shop facade: roll-up door, striped sign awning, sign board (front = -Y)."""
    clear_scene()
    mb = MeshPartBuilder("wo_garage_facade")
    w, h, t = 10.0, 6.6, 0.4
    fy = -0.5 * t

    mb.add_box(size=(w, t, 3.8), location=(0.0, 0.0, 1.9), mat="mat_brick_red")
    mb.add_box(size=(w, t, h - 3.8), location=(0.0, 0.0, 3.8 + 0.5 * (h - 3.8)), mat="mat_enamel_cream")
    mb.add_box(size=(w + 0.3, t + 0.3, 0.3), location=(0.0, 0.0, h + 0.1), mat="mat_enamel_teal")
    mb.add_box(size=(w + 0.1, t + 0.1, 0.35), location=(0.0, 0.0, 0.175), mat="mat_concrete")
    for sx in (-5.0, 5.0):
        mb.add_box(size=(0.45, t + 0.2, h), location=(sx, 0.0, 0.5 * h), mat="mat_brick_dark")

    # Roll-up door (4.2 x 3.6 m) in steel frame with slats and handle
    door_x = -1.2
    mb.add_box(size=(4.2, 0.08, 3.6), location=(door_x, fy - 0.03, 1.8), mat="mat_corrugated_steel")
    for k in range(15):
        mb.add_box(size=(4.2, 0.05, 0.05), location=(door_x, fy - 0.09, 0.2 + k * 0.235), mat="mat_steel_dark")
    for sx in (-2.2, 2.2):
        mb.add_box(size=(0.2, 0.2, 3.9), location=(door_x + sx, fy - 0.08, 1.95), mat="mat_steel_dark")
    mb.add_box(size=(4.6, 0.45, 0.45), location=(door_x, fy - 0.2, 3.9), mat="mat_steel_dark")
    mb.add_box(size=(0.5, 0.06, 0.06), location=(door_x, fy - 0.14, 0.55), mat="mat_chrome")

    # Pedestrian door + shop window with mullions
    mb.add_box(size=(1.0, 0.08, 2.2), location=(2.3, fy - 0.03, 1.1), mat="mat_enamel_orange")
    mb.add_box(size=(0.5, 0.09, 0.7), location=(2.3, fy - 0.04, 1.6), mat="mat_window_glass")
    mb.add_cylinder(radius=0.035, depth=0.12, segments=8, location=(2.68, fy - 0.1, 1.05), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_chrome")
    mb.add_box(size=(1.6, 0.08, 1.6), location=(3.85, fy - 0.03, 1.7), mat="mat_window_glass")
    mb.add_box(size=(1.8, 0.25, 0.12), location=(3.85, fy - 0.12, 0.86), mat="mat_concrete")
    mb.add_box(size=(0.05, 0.1, 1.6), location=(3.85, fy - 0.08, 1.7), mat="mat_steel_dark")
    mb.add_box(size=(1.6, 0.1, 0.05), location=(3.85, fy - 0.08, 1.7), mat="mat_steel_dark")

    # Upper floor windows
    for wx in (-3.4, 0.0, 3.4):
        mb.add_box(size=(1.4, 0.08, 1.3), location=(wx, fy - 0.03, 5.1), mat="mat_window_glass")
        mb.add_box(size=(1.6, 0.2, 0.12), location=(wx, fy - 0.1, 4.39), mat="mat_enamel_teal")
        mb.add_box(size=(1.6, 0.14, 0.12), location=(wx, fy - 0.07, 5.81), mat="mat_enamel_teal")

    # Striped sign awning on brackets
    pitch = 0.42
    for k in range(10):
        ax = -4.05 + k * 0.9
        mat = "mat_enamel_orange" if k % 2 == 0 else "mat_enamel_cream"
        mb.add_box(size=(0.9, 1.5, 0.06), location=(ax, fy - 0.68, 4.0), rotation=(-pitch, 0.0, 0.0), mat=mat)
    mb.add_box(size=(9.0, 0.06, 0.28), location=(0.0, fy - 1.36, 3.6), mat="mat_enamel_orange")
    for bx in (-4.4, 0.0, 4.4):
        mb.add_tube_between((bx, fy, 3.6), (bx, fy - 1.32, 3.72), radius=0.03, segments=6, mat="mat_steel_dark")

    # Projecting sign board with stylized moped icon
    sign_y = fy - 0.12
    mb.add_box(size=(4.6, 0.14, 0.9), location=(0.0, sign_y, 6.05), mat="mat_enamel_teal")
    mb.add_box(size=(4.3, 0.06, 0.66), location=(0.0, sign_y - 0.09, 6.05), mat="mat_enamel_cream")
    for wx in (-0.55, 0.55):
        mb.add_torus(major_radius=0.17, minor_radius=0.035, major_segments=14, minor_segments=4, location=(wx - 1.3, sign_y - 0.14, 5.92), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_enamel_orange")
    mb.add_box(size=(0.8, 0.05, 0.16), location=(-1.3, sign_y - 0.14, 6.04), mat="mat_enamel_orange")
    mb.add_box(size=(0.1, 0.05, 0.36), location=(-0.85, sign_y - 0.14, 6.14), rotation=(0.0, -0.3, 0.0), mat="mat_enamel_orange")
    for k in range(5):
        mb.add_box(size=(0.36, 0.05, 0.36), location=(-0.1 + k * 0.48, sign_y - 0.14, 6.05), mat="mat_enamel_orange")
    # Gooseneck sign lamps
    for lx in (-1.6, 1.6):
        mb.add_polyline_tube([(lx, fy, 6.9), (lx, fy - 0.45, 7.0), (lx, fy - 0.6, 6.85)], [0.025, 0.025, 0.025], segments=6, mat="mat_steel_dark")
        mb.add_cone(radius1=0.16, radius2=0.04, depth=0.14, segments=10, location=(lx, fy - 0.62, 6.78), mat="mat_enamel_teal")

    mb.build_object()
    add_socket("SOCKET_GARAGE_DOOR", location=(door_x, fy - 0.6, 0.0))
    add_socket("SOCKET_SIGN", location=(0.0, sign_y - 0.15, 6.05))
    return export_world_glb(out_dir, ZONE, "wo_garage_facade")


def build_wo_taco_truck(out_dir: str) -> Dict[str, object]:
    """Stylized East Bay step-van taco truck with curbside (+X) serving window (front = -Y)."""
    clear_scene()
    mb = MeshPartBuilder("wo_taco_truck")
    wheel_r = 0.45

    # Chassis rails, bumpers, wheels
    for sx in (-0.5, 0.5):
        mb.add_box(size=(0.14, 6.6, 0.22), location=(sx, 0.1, 0.62), mat="mat_steel_dark")
    mb.add_box(size=(2.3, 0.18, 0.28), location=(0.0, -3.28, 0.62), mat="mat_chrome")
    mb.add_box(size=(2.2, 0.2, 0.22), location=(0.0, 3.44, 0.6), mat="mat_steel_dark")
    for wy in (-2.2, 2.1):
        for sx in (-1.0, 1.0):
            mb.add_cylinder(radius=wheel_r, depth=0.3, segments=16, location=(sx, wy, wheel_r), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_rubber")
            mb.add_cylinder(radius=0.24, depth=0.32, segments=10, location=(sx, wy, wheel_r), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_chrome", smooth=False)
            mb.add_box(size=(0.36, 1.2, 0.08), location=(sx * 1.02, wy, 1.0), mat="mat_steel_dark")

    # Cab: hood, windshield, side windows, headlights, grille, mirrors
    mb.add_box(size=(2.2, 1.0, 0.9), location=(0.0, -2.75, 1.25), mat="mat_enamel_cream")
    mb.add_box(size=(2.25, 1.3, 1.5), location=(0.0, -1.7, 1.75), mat="mat_enamel_cream")
    mb.add_box(size=(2.0, 0.1, 0.8), location=(0.0, -2.33, 2.05), rotation=(0.18, 0.0, 0.0), mat="mat_window_glass")
    for sx in (-1.13, 1.13):
        mb.add_box(size=(0.06, 0.9, 0.7), location=(sx, -1.75, 2.05), mat="mat_window_glass")
        mb.add_cylinder(radius=0.12, depth=0.06, segments=12, location=(sx * 0.8, -3.26, 1.35), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_glass_lens")
        mb.add_tube_between((sx, -2.2, 2.0), (sx * 1.2, -2.3, 2.05), radius=0.02, segments=6, mat="mat_chrome")
        mb.add_box(size=(0.08, 0.2, 0.3), location=(sx * 1.24, -2.3, 2.05), mat="mat_vinyl_black")
    mb.add_box(size=(1.1, 0.06, 0.45), location=(0.0, -3.26, 1.3), mat="mat_chrome")
    for k in range(4):
        mb.add_box(size=(1.0, 0.07, 0.04), location=(0.0, -3.29, 1.13 + k * 0.11), mat="mat_steel_dark")

    # Kitchen box body with stripe, serving window, awning flap, counter, menu board
    body_y0, body_len = -1.05, 4.5
    by = body_y0 + 0.5 * body_len
    mb.add_box(size=(2.4, body_len, 2.3), location=(0.0, by, 2.0), mat="mat_enamel_cream")
    mb.add_box(size=(2.44, body_len, 0.28), location=(0.0, by, 1.25), mat="mat_enamel_red")
    mb.add_box(size=(2.44, body_len, 0.1), location=(0.0, by, 1.45), mat="mat_enamel_yellow")
    mb.add_box(size=(2.5, body_len + 0.1, 0.12), location=(0.0, by, 3.2), mat="mat_enamel_red")
    mb.add_box(size=(0.06, 2.4, 1.0), location=(1.21, by - 0.1, 2.25), mat="mat_steel_dark")
    mb.add_box(size=(0.08, 2.5, 0.06), location=(1.24, by - 0.1, 2.77), mat="mat_chrome")
    mb.add_box(size=(0.06, 2.6, 1.1), location=(1.62, by - 0.1, 3.0), rotation=(0.0, -0.95, 0.0), mat="mat_enamel_yellow")
    for ay in (-1.1, 0.9):
        mb.add_tube_between((1.22, by - 0.1 + ay, 2.75), (2.0, by - 0.1 + ay, 3.38), radius=0.02, segments=6, mat="mat_chrome")
    mb.add_box(size=(0.45, 2.6, 0.06), location=(1.43, by - 0.1, 1.72), mat="mat_chrome")
    for k, mat in enumerate(("mat_enamel_red", "mat_amber_plastic", "mat_enamel_teal")):
        mb.add_cylinder(radius=0.07, depth=0.18, segments=8, location=(1.45, by - 0.8 + k * 0.3, 1.84), mat=mat)
    mb.add_box(size=(0.05, 1.2, 0.8), location=(1.23, by + 1.65, 2.25), mat="mat_vinyl_black")
    for k in range(4):
        mb.add_box(size=(0.03, 0.9, 0.06), location=(1.26, by + 1.65, 2.5 - k * 0.15), mat="mat_enamel_cream")

    # Roof vents, AC unit, rear doors, propane tanks, step
    mb.add_box(size=(1.0, 1.0, 0.35), location=(0.0, by + 0.8, 3.42), mat="mat_galvanized")
    for vy in (-0.6, 0.0):
        mb.add_cylinder(radius=0.2, depth=0.3, segments=10, location=(0.4, by + vy, 3.4), mat="mat_galvanized")
        mb.add_cone(radius1=0.28, radius2=0.05, depth=0.12, segments=10, location=(0.4, by + vy, 3.62), mat="mat_steel_dark")
    for sx in (-0.58, 0.58):
        mb.add_box(size=(1.1, 0.05, 2.0), location=(sx, 3.47, 2.0), mat="mat_enamel_cream")
        mb.add_box(size=(0.06, 0.06, 0.4), location=(sx * 0.15, 3.52, 2.0), mat="mat_chrome")
    for tx in (-0.6, 0.6):
        mb.add_cylinder(radius=0.17, depth=0.8, segments=10, location=(tx, 3.75, 1.25), mat="mat_ceramic_white")
        mb.add_cylinder(radius=0.06, depth=0.12, segments=8, location=(tx, 3.75, 1.71), mat="mat_brass")
    mb.add_box(size=(1.0, 0.3, 0.06), location=(0.0, 3.62, 0.5), mat="mat_steel_dark")
    for sx in (-1.1, 1.1):
        mb.add_box(size=(0.12, 0.06, 0.22), location=(sx, 3.42, 1.0), mat="mat_enamel_red")

    mb.build_object()
    add_socket("SOCKET_SERVING_WINDOW", location=(1.8, by - 0.1, 0.0))
    return export_world_glb(out_dir, ZONE, "wo_taco_truck")


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        build_wo_warehouse_brick(out_dir),
        build_wo_bart_overpass_pillar(out_dir),
        build_wo_garage_facade(out_dir),
        build_wo_taco_truck(out_dir),
        build_wo_road_pothole_20m(out_dir),
    ]

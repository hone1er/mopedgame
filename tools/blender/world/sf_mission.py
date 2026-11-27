"""Zone_SF_Mission modular kit: Victorian rowhouse, corner taqueria, Muni rail street, catenary pole,
steep crosswalk street, and courier delivery beacon."""

from __future__ import annotations

import math
from typing import Dict, List, Sequence, Tuple

from common_utils import MeshPartBuilder, add_socket, clear_scene
from world.world_common import (
    add_ellipsoid,
    ProfilePoint,
    add_snap_sockets,
    export_world_glb,
    frames_at,
    straight_frames,
    street_profile,
    sweep_profile,
)


ZONE = "SFMission"


def _window(mb: MeshPartBuilder, x: float, y: float, z: float, w: float, h: float, trim: str, sash: str) -> None:
    """Front-facing (-Y) double-hung window with trim surround, sill and meeting rail."""
    mb.add_box(size=(w, 0.08, h), location=(x, y - 0.03, z), mat="mat_window_glass")
    mb.add_box(size=(w + 0.24, 0.1, 0.12), location=(x, y - 0.05, z + 0.5 * h + 0.06), mat=trim)
    mb.add_box(size=(w + 0.3, 0.18, 0.08), location=(x, y - 0.09, z - 0.5 * h - 0.04), mat=trim)
    for sx in (-0.5, 0.5):
        mb.add_box(size=(0.1, 0.1, h), location=(x + sx * (w + 0.1), y - 0.05, z), mat=trim)
    mb.add_box(size=(w, 0.1, 0.05), location=(x, y - 0.06, z), mat=sash)


def build_sf_victorian_rowhouse(out_dir: str) -> Dict[str, object]:
    """Three-story SF Victorian: angled bay window, bracketed cornice, false-front pediment, stoop."""
    clear_scene()
    mb = MeshPartBuilder("sf_victorian_rowhouse")
    w, d = 7.6, 12.0
    fy = -0.5 * d
    body, trim, accent = "mat_paint_victorian_lilac", "mat_paint_trim_white", "mat_paint_victorian_blue"

    mb.add_box(size=(w, d, 3.0), location=(0.0, 0.0, 1.5), mat="mat_concrete")
    mb.add_box(size=(w, d, 6.6), location=(0.0, 0.0, 3.0 + 3.3), mat=body)
    mb.add_box(size=(w + 0.08, 0.2, 0.25), location=(0.0, fy - 0.05, 3.05), mat=trim)
    mb.add_box(size=(w + 0.08, 0.16, 0.2), location=(0.0, fy - 0.04, 6.25), mat=trim)
    for sx in (-0.5 * w + 0.12, 0.5 * w - 0.12):
        mb.add_box(size=(0.24, 0.14, 6.6), location=(sx, fy - 0.07, 6.3), mat=trim)
    # Shiplap siding lines on the flat (right) half of the facade
    for k in range(14):
        mb.add_box(size=(3.6, 0.05, 0.04), location=(1.7, fy - 0.02, 3.4 + k * 0.42), mat=accent)

    # Angled bay window spanning floors 2-3 on the left half
    bx0, bx1 = -3.4, -0.2
    proj = 0.9
    fx0, fx1 = bx0 + 0.8, bx1 - 0.8
    bay_z0, bay_z1 = 3.2, 9.6
    bay_h = bay_z1 - bay_z0
    bay_zc = 0.5 * (bay_z0 + bay_z1)
    mb.add_box(size=(fx1 - fx0 + 0.1, 0.16, bay_h), location=(0.5 * (fx0 + fx1), fy - proj, bay_zc), mat=body)
    mb.add_box(size=(fx1 - fx0, proj, bay_h), location=(0.5 * (fx0 + fx1), fy - 0.5 * proj, bay_zc), mat=body)
    side_len = math.hypot(0.8, proj)
    for ax, bx in ((bx0, fx0), (bx1, fx1)):
        ang = math.atan2(-proj, bx - ax)
        mx, my = 0.5 * (ax + bx), fy - 0.5 * proj
        # Outward (street-facing) panel normal: the box's local +/-Y axis with negative world Y
        nx, ny = -math.sin(ang), math.cos(ang)
        if ny > 0.0:
            nx, ny = -nx, -ny
        mb.add_box(size=(side_len, 0.16, bay_h), location=(mx, my, bay_zc), rotation=(0.0, 0.0, ang), mat=body)
        for wz in (4.75, 7.95):
            mb.add_box(size=(side_len - 0.45, 0.1, 1.6), location=(mx + nx * 0.06, my + ny * 0.06, wz), rotation=(0.0, 0.0, ang), mat="mat_window_glass")
            mb.add_box(size=(side_len - 0.3, 0.14, 0.1), location=(mx + nx * 0.1, my + ny * 0.1, wz - 0.86), rotation=(0.0, 0.0, ang), mat=trim)
            mb.add_box(size=(side_len - 0.3, 0.1, 0.1), location=(mx + nx * 0.08, my + ny * 0.08, wz + 0.86), rotation=(0.0, 0.0, ang), mat=trim)
    for wz in (4.75, 7.95):
        _window(mb, 0.5 * (fx0 + fx1), fy - proj - 0.08, wz, 1.3, 1.7, trim, accent)
    # Bay roof cap, frieze and soffit brackets
    mb.add_box(size=(bx1 - bx0 + 0.4, proj + 0.5, 0.25), location=(0.5 * (bx0 + bx1), fy - 0.5 * proj - 0.1, bay_z1 + 0.1), mat=trim)
    mb.add_box(size=(bx1 - bx0 + 0.1, proj + 0.2, 0.3), location=(0.5 * (bx0 + bx1), fy - 0.5 * proj - 0.02, bay_z0 - 0.05), mat=trim)
    for kx in (fx0 + 0.15, 0.5 * (fx0 + fx1), fx1 - 0.15):
        mb.add_box(size=(0.14, 0.4, 0.4), location=(kx, fy - proj + 0.05, bay_z0 - 0.4), mat=trim)
    for k in range(8):
        mb.add_box(size=(0.1, 0.12, 0.25), location=(fx0 - 0.15 + k * 0.27, fy - proj - 0.12, bay_z1 - 0.25), mat=accent)

    # Right-half tall windows with pediment hoods
    for wz in (4.75, 7.95):
        _window(mb, 1.9, fy, wz, 1.2, 1.9, trim, accent)
        for sgn in (-1.0, 1.0):
            mb.add_box(size=(0.85, 0.16, 0.1), location=(1.9 + sgn * 0.38, fy - 0.1, wz + 1.25), rotation=(0.0, sgn * 0.38, 0.0), mat=trim)

    # Ground floor: garage door under the bay, stoop stairs to raised entry on the right
    mb.add_box(size=(2.8, 0.1, 2.3), location=(-1.8, fy - 0.03, 1.15), mat=accent)
    for k in range(5):
        mb.add_box(size=(2.8, 0.05, 0.04), location=(-1.8, fy - 0.1, 0.35 + k * 0.42), mat=trim)
    mb.add_box(size=(3.2, 0.2, 0.2), location=(-1.8, fy - 0.08, 2.4), mat=trim)
    entry_z = 1.4
    mb.add_box(size=(1.8, 1.4, entry_z), location=(2.2, fy - 0.7, 0.5 * entry_z), mat="mat_concrete")
    for k in range(7):
        sh = entry_z * (k + 1) / 8.0
        mb.add_box(size=(1.6, 0.3, sh), location=(2.2, fy - 1.4 - (6 - k) * 0.3 - 0.15, 0.5 * sh), mat="mat_concrete")
    for sx in (1.35, 3.05):
        mb.add_tube_between((sx, fy - 3.4, 1.0), (sx, fy - 1.2, 2.3), radius=0.03, segments=6, mat="mat_steel_dark")
    mb.add_box(size=(1.0, 0.1, 2.3), location=(2.2, fy - 0.03, entry_z + 1.15), mat=accent)
    mb.add_box(size=(0.7, 0.11, 0.5), location=(2.2, fy - 0.04, entry_z + 1.8), mat="mat_window_glass")
    mb.add_box(size=(1.5, 0.3, 0.2), location=(2.2, fy - 0.12, entry_z + 2.5), mat=trim)
    for sx in (1.55, 2.85):
        mb.add_box(size=(0.16, 0.16, 2.4), location=(sx, fy - 0.1, entry_z + 1.2), mat=trim)

    # Bracketed cornice, frieze panels, false-front parapet and pediment
    top = 9.6
    mb.add_box(size=(w + 0.5, 0.9, 0.3), location=(0.0, fy - 0.3, top + 0.55), mat=trim)
    mb.add_box(size=(w, 0.2, 0.5), location=(0.0, fy - 0.08, top + 0.15), mat=accent)
    for k in range(11):
        mb.add_box(size=(0.14, 0.55, 0.42), location=(-3.5 + k * 0.7, fy - 0.3, top + 0.2), mat=trim)
    mb.add_box(size=(w, 0.3, 1.0), location=(0.0, fy + 0.15, top + 1.2), mat=body)
    for sgn in (-1.0, 1.0):
        mb.add_box(size=(2.4, 0.4, 0.18), location=(sgn * 1.05, fy - 0.05, top + 1.95), rotation=(0.0, sgn * 0.42, 0.0), mat=trim)
    mb.add_cylinder(radius=0.3, depth=0.12, segments=12, location=(0.0, fy - 0.1, top + 1.5), rotation=(math.pi * 0.5, 0.0, 0.0), mat=accent)
    for sx in (-3.65, 3.65):
        mb.add_cone(radius1=0.12, radius2=0.0, depth=0.5, segments=6, location=(sx, fy - 0.1, top + 2.0), mat=trim)
    # Downspout + rooftop vent stack
    mb.add_cylinder(radius=0.06, depth=10.0, segments=6, location=(3.7, fy + 0.1, 5.0), mat="mat_galvanized")
    mb.add_cylinder(radius=0.12, depth=1.0, segments=8, location=(1.0, 2.0, top + 0.5), mat="mat_galvanized")

    mb.build_object()
    add_socket("SOCKET_ENTRANCE", location=(2.2, fy - 3.6, 0.0))
    add_socket("SOCKET_GARAGE_DOOR", location=(-1.8, fy - 0.6, 0.0))
    return export_world_glb(out_dir, ZONE, "sf_victorian_rowhouse")


def build_sf_mission_storefront(out_dir: str) -> Dict[str, object]:
    """Vibrant two-story Mission corner taqueria: glazed corner storefront, striped awnings, mural."""
    clear_scene()
    mb = MeshPartBuilder("sf_mission_storefront")
    w, d, h = 12.0, 12.0, 8.0
    fy, lx = -0.5 * d, -0.5 * w
    body, trim = "mat_paint_mission_turquoise", "mat_paint_mission_marigold"

    mb.add_box(size=(w, d, h), location=(0.0, 0.0, 0.5 * h), mat=body)
    mb.add_box(size=(w + 0.3, d + 0.3, 0.3), location=(0.0, 0.0, 4.05), mat=trim)
    # Mission-revival stepped parapet
    mb.add_box(size=(w + 0.4, d + 0.4, 0.35), location=(0.0, 0.0, h + 0.1), mat=trim)
    for step, (sw, sh) in enumerate(((5.0, 0.7), (3.0, 1.2), (1.4, 1.6))):
        mb.add_box(size=(sw, 0.35, sh), location=(1.0, fy - 0.02, h + 0.3 + 0.5 * sh), mat=body if step % 2 == 0 else trim)
        mb.add_box(size=(0.35, sw, sh), location=(lx - 0.02, 1.0, h + 0.3 + 0.5 * sh), mat=body if step % 2 == 0 else trim)

    # Ground floor storefront on both street faces: tile bulkhead, glazing, mullions
    for face in ("front", "side"):
        for k in range(4):
            c = -4.3 + k * 2.6
            if face == "front":
                mb.add_box(size=(2.3, 0.12, 0.7), location=(c, fy - 0.04, 0.35), mat="mat_paint_mission_pink")
                mb.add_box(size=(2.3, 0.08, 2.3), location=(c, fy - 0.03, 1.9), mat="mat_window_glass")
                mb.add_box(size=(0.16, 0.16, 3.2), location=(c + 1.3, fy - 0.07, 1.6), mat=trim)
            else:
                mb.add_box(size=(0.12, 2.3, 0.7), location=(lx - 0.04, c, 0.35), mat="mat_paint_mission_pink")
                mb.add_box(size=(0.08, 2.3, 2.3), location=(lx - 0.03, c, 1.9), mat="mat_window_glass")
                mb.add_box(size=(0.16, 0.16, 3.2), location=(lx - 0.07, c + 1.3, 1.6), mat=trim)
    # Chamfered corner entry with door and transom
    mb.add_box(size=(1.8, 0.3, 3.4), location=(lx + 0.45, fy + 0.45, 1.7), rotation=(0.0, 0.0, -math.pi * 0.25), mat=trim)
    mb.add_box(size=(1.1, 0.34, 2.3), location=(lx + 0.4, fy + 0.4, 1.15), rotation=(0.0, 0.0, -math.pi * 0.25), mat="mat_window_glass")
    mb.add_cylinder(radius=0.08, depth=8.0, segments=8, location=(lx + 0.05, fy + 0.05, 4.0), mat=trim)

    # Striped awnings along both faces + papel picado banners
    stripes = ("mat_paint_mission_pink", "mat_enamel_cream")
    for k in range(10):
        c = -5.4 + k * 1.2
        mat = stripes[k % 2]
        mb.add_box(size=(1.2, 1.6, 0.06), location=(c, fy - 0.7, 3.4), rotation=(-0.4, 0.0, 0.0), mat=mat)
        mb.add_box(size=(1.6, 1.2, 0.06), location=(lx - 0.7, c, 3.4), rotation=(0.0, 0.4, 0.0), mat=mat)
    mb.add_box(size=(w, 0.05, 0.3), location=(0.0, fy - 1.44, 2.95), mat="mat_paint_mission_pink")
    mb.add_box(size=(0.05, d, 0.3), location=(lx - 1.44, 0.0, 2.95), mat="mat_paint_mission_pink")
    picado = ("mat_paint_mission_pink", "mat_paint_mission_marigold", "mat_enamel_teal", "mat_paint_trim_white", "mat_enamel_red")
    for k in range(16):
        mb.add_box(size=(0.32, 0.02, 0.36), location=(-5.6 + k * 0.72, fy - 1.5, 2.55 - 0.08 * math.sin(k * 0.8)), mat=picado[k % len(picado)])

    # Sign board above awning
    mb.add_box(size=(6.0, 0.2, 0.9), location=(1.0, fy - 0.12, 4.75), mat="mat_paint_mission_pink")
    mb.add_box(size=(5.6, 0.1, 0.6), location=(1.0, fy - 0.25, 4.75), mat=trim)
    mb.add_cylinder(radius=0.32, depth=0.12, segments=12, location=(-1.4, fy - 0.32, 4.75), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_enamel_red")
    mb.add_cylinder(radius=0.5, depth=0.1, segments=12, location=(-1.4, fy - 0.28, 4.62), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_enamel_yellow")

    # Upper floor windows (front + side)
    for k in range(4):
        c = -4.3 + k * 2.6
        mb.add_box(size=(1.3, 0.08, 1.6), location=(c, fy - 0.03, 6.0), mat="mat_window_glass")
        mb.add_box(size=(1.6, 0.16, 0.18), location=(c, fy - 0.08, 6.9), mat="mat_paint_mission_pink")
        mb.add_box(size=(1.6, 0.22, 0.12), location=(c, fy - 0.1, 5.15), mat="mat_paint_mission_pink")
    for k in range(2):
        c = 2.0 + k * 2.6
        mb.add_box(size=(0.08, 1.3, 1.6), location=(lx - 0.03, c, 6.0), mat="mat_window_glass")
        mb.add_box(size=(0.16, 1.6, 0.18), location=(lx - 0.08, c, 6.9), mat="mat_paint_mission_pink")
    # Mural on the side upper wall: sun disc, rays, rolling bay waves, flowers
    mx = lx - 0.06
    mb.add_box(size=(0.04, 6.2, 3.2), location=(mx + 0.03, -2.4, 6.0), mat="mat_enamel_cream")
    mb.add_cylinder(radius=0.9, depth=0.06, segments=16, location=(mx, -3.4, 6.6), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_paint_mission_marigold")
    for k in range(8):
        ang = k * math.pi / 4.0
        mb.add_box(size=(0.05, 0.16, 0.7), location=(mx - 0.01, -3.4 + math.cos(ang) * 1.3, 6.6 + math.sin(ang) * 1.3), rotation=(ang + math.pi * 0.5, 0.0, 0.0), mat="mat_enamel_red")
    for k in range(5):
        mb.add_box(size=(0.05, 1.5, 0.35), location=(mx - 0.01, -4.8 + k * 1.2, 5.0 + 0.18 * (k % 2)), rotation=(0.35 * (1 if k % 2 else -1), 0.0, 0.0), mat="mat_paint_victorian_blue")
    for k, mat in enumerate(("mat_paint_mission_pink", "mat_enamel_red", "mat_paint_mission_pink")):
        mb.add_cylinder(radius=0.28, depth=0.06, segments=10, location=(mx, -1.6 + k * 0.8, 6.3 + 0.3 * (k % 2)), rotation=(0.0, math.pi * 0.5, 0.0), mat=mat)
        mb.add_box(size=(0.04, 0.06, 0.9), location=(mx + 0.01, -1.6 + k * 0.8, 5.75 + 0.3 * (k % 2)), mat="mat_awning_green")

    # Sidewalk A-frame menu sign and rooftop water tank
    mb.add_box(size=(0.6, 0.06, 0.9), location=(2.5, fy - 2.2, 0.45), rotation=(0.22, 0.0, 0.0), mat="mat_vinyl_black")
    mb.add_box(size=(0.6, 0.06, 0.9), location=(2.5, fy - 2.5, 0.45), rotation=(-0.22, 0.0, 0.0), mat="mat_vinyl_black")
    mb.add_cylinder(radius=1.0, depth=1.8, segments=12, location=(3.5, 3.0, h + 1.6), mat="mat_wood_weathered")
    mb.add_cone(radius1=1.05, radius2=0.1, depth=0.5, segments=12, location=(3.5, 3.0, h + 2.75), mat="mat_wood_weathered")
    for sx in (-0.7, 0.7):
        mb.add_box(size=(0.12, 0.12, 0.7), location=(3.5 + sx, 3.0, h + 0.35), mat="mat_steel_dark")

    mb.build_object()
    add_socket("SOCKET_ENTRANCE", location=(lx - 0.4, fy - 0.4, 0.0))
    add_socket("SOCKET_DELIVERY_PICKUP", location=(1.0, fy - 1.8, 0.0))
    return export_world_glb(out_dir, ZONE, "sf_mission_storefront")


def _muni_profile(
    half_road: float,
    sidewalk: float,
    track_centers: Sequence[float],
    gauge: float = 1.435,
    groove_w: float = 0.1,
    groove_d: float = 0.05,
    curb_h: float = 0.15,
    skirt: float = 0.35,
) -> Tuple[List[ProfilePoint], List[str], List[float]]:
    """Street cross-section with concrete trackway panels and recessed rail grooves."""
    rails = sorted(tc + s * 0.5 * gauge for tc in track_centers for s in (-1.0, 1.0))
    groove_starts = {round(r - 0.5 * groove_w, 4) for r in rails}
    trackway = [(tc - 0.95, tc + 0.95) for tc in track_centers]
    paint = [
        (-0.16, -0.06, "mat_road_paint_yellow"),
        (0.06, 0.16, "mat_road_paint_yellow"),
        (-3.65, -3.55, "mat_road_paint_white"),
        (3.55, 3.65, "mat_road_paint_white"),
    ]
    stops = {half_road}
    for a, b in trackway:
        stops.update((round(a, 4), round(b, 4)))
    for a, b, _ in paint:
        stops.update((round(a, 4), round(b, 4)))
    stops.update(groove_starts)

    def surface(mid: float) -> str:
        for a, b, m in paint:
            if a <= mid <= b:
                return m
        for a, b in trackway:
            if a <= mid <= b:
                return "mat_concrete_dark"
        return "mat_asphalt"

    edge = half_road + sidewalk
    pts: List[ProfilePoint] = [(-edge, -skirt), (-edge, curb_h), (-half_road, curb_h), (-half_road, 0.0)]
    mats: List[str] = ["mat_concrete", "mat_concrete", "mat_concrete_dark"]
    x = -half_road
    for xs in sorted(stops):
        if xs <= x + 1e-6:
            continue
        mats.append(surface(0.5 * (x + xs)))
        pts.append((xs, 0.0))
        x = xs
        if xs in groove_starts:
            g1 = round(xs + groove_w, 4)
            pts += [(xs, -groove_d), (g1, -groove_d), (g1, 0.0)]
            mats += ["mat_steel_dark", "mat_steel_dark", "mat_steel_dark"]
            x = g1
    pts += [(half_road, curb_h), (edge, curb_h), (edge, -skirt)]
    mats += ["mat_concrete_dark", "mat_concrete", "mat_concrete"]
    return pts, mats, rails


def build_sf_muni_rail_road_20m(out_dir: str) -> Dict[str, object]:
    """20 m Mission street segment with twin Muni tracks: steel rails recessed in grooved trackway."""
    clear_scene()
    surf = MeshPartBuilder("sf_muni_rail_road_20m_surface")
    mb = MeshPartBuilder("sf_muni_rail_road_20m_details")
    frames = straight_frames(20.0, 10)
    half_road = 6.0
    profile, mats, rails = _muni_profile(half_road, 2.6, track_centers=(-1.9, 1.9))

    def cell_mat(iu, iv, c):
        # Dashed lane dividers (2 m dash / 2 m gap)
        if mats[iu] == "mat_road_paint_white" and iv % 2 == 1:
            return "mat_asphalt"
        return None

    sweep_profile(surf, frames, profile, mats, cell_mat=cell_mat)
    surf.build_object(recalc_normals=False)

    # Steel girder rails seated in the grooves (rail head 1 cm below the street surface)
    for rx in rails:
        mb.add_box(size=(0.07, 20.0, 0.035), location=(rx, 0.0, -0.0275), mat="mat_chrome")
        for k in range(4):
            mb.add_box(size=(0.09, 0.05, 0.012), location=(rx, -7.5 + k * 5.0, -0.045), mat="mat_steel_dark")
    # Track drains, manhole, sidewalk tree wells, Muni stop pole, fire hydrant
    for tc in (-1.9, 1.9):
        mb.add_box(size=(1.2, 0.3, 0.02), location=(tc, 5.0, 0.004), mat="mat_cast_iron")
    mb.add_cylinder(radius=0.38, depth=0.03, segments=16, location=(4.6, -4.0, 0.005), mat="mat_cast_iron", smooth=False)
    for ty in (-6.0, 4.0):
        mb.add_box(size=(1.2, 1.2, 0.04), location=(7.4, ty, 0.15), mat="mat_dry_grass")
        mb.add_cylinder(radius=0.12, depth=3.0, segments=8, location=(7.4, ty, 1.65), mat="mat_bark_eucalyptus")
        add_ellipsoid(mb, radius=1.2, u_segments=8, v_segments=5, location=(7.4, ty, 3.8), scale=(1.0, 1.0, 1.2), mat="mat_foliage_sage", smooth=False)
    mb.add_cylinder(radius=0.05, depth=3.0, segments=8, location=(-6.4, 2.0, 1.65), mat="mat_galvanized")
    mb.add_box(size=(0.05, 0.6, 0.6), location=(-6.4, 2.0, 2.9), mat="mat_enamel_yellow")
    mb.add_box(size=(0.3, 1.0, 0.02), location=(-6.4, 2.0, 0.16), mat="mat_enamel_yellow")
    mb.add_cylinder(radius=0.12, depth=0.7, segments=10, location=(6.3, 7.5, 0.5), mat="mat_ceramic_white")
    add_ellipsoid(mb, radius=0.14, u_segments=8, v_segments=4, location=(6.3, 7.5, 0.88), mat="mat_paint_victorian_blue")
    mb.add_cylinder(radius=0.05, depth=0.4, segments=6, location=(6.3, 7.5, 0.62), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_ceramic_white")
    mb.build_object()

    add_snap_sockets(frames)
    for i, tc in enumerate((-1.9, 1.9)):
        add_socket(f"SOCKET_TRACK_{i + 1}_START", location=(tc, -10.0, 0.0))
        add_socket(f"SOCKET_TRACK_{i + 1}_END", location=(tc, 10.0, 0.0))
    return export_world_glb(out_dir, ZONE, "sf_muni_rail_road_20m")


def build_sf_catenary_pole(out_dir: str) -> Dict[str, object]:
    """Overhead Muni contact-wire pole with bracket crossarm, stay, insulators and street lamp."""
    clear_scene()
    mb = MeshPartBuilder("sf_catenary_pole")
    pole_h = 8.6
    arm_z = 6.6
    wire_x = -3.8
    wire_z = 5.6

    # Footing, base plate + anchor bolts, decorative base shroud, tapered pole, finial
    mb.add_cylinder(radius=0.45, depth=0.25, segments=12, location=(0.0, 0.0, 0.125), mat="mat_concrete")
    mb.add_box(size=(0.5, 0.5, 0.04), location=(0.0, 0.0, 0.27), mat="mat_steel_dark")
    for sx in (-0.19, 0.19):
        for sy in (-0.19, 0.19):
            mb.add_hex_bolt(radius=0.025, height=0.05, location=(sx, sy, 0.31), mat="mat_galvanized")
    mb.add_cone(radius1=0.24, radius2=0.15, depth=1.1, segments=12, location=(0.0, 0.0, 0.84), mat="mat_awning_green")
    mb.add_torus(major_radius=0.17, minor_radius=0.03, major_segments=12, minor_segments=4, location=(0.0, 0.0, 1.4), mat="mat_awning_green")
    mb.add_cone(radius1=0.14, radius2=0.08, depth=pole_h - 1.4, segments=12, location=(0.0, 0.0, 1.4 + 0.5 * (pole_h - 1.4)), mat="mat_steel_dark")
    mb.add_cone(radius1=0.1, radius2=0.0, depth=0.3, segments=8, location=(0.0, 0.0, pole_h + 0.15), mat="mat_awning_green")
    for z in (arm_z - 0.2, arm_z + 0.6):
        mb.add_cylinder(radius=0.13, depth=0.12, segments=12, location=(0.0, 0.0, z), mat="mat_galvanized")

    # Bracket crossarm with diagonal stay and span insulators
    mb.add_tube_between((0.0, 0.0, arm_z), (wire_x - 0.4, 0.0, arm_z), radius=0.055, segments=8, mat="mat_galvanized")
    mb.add_tube_between((0.0, 0.0, arm_z + 0.6), (wire_x * 0.75, 0.0, arm_z), radius=0.035, segments=6, mat="mat_galvanized")
    for k in range(3):
        mb.add_cylinder(radius=0.07, depth=0.04, segments=10, location=(-0.6 - k * 0.08, 0.0, arm_z), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_ceramic_white")
    # Drop hanger, pull-off insulator, registration arm, contact + messenger wires
    mb.add_tube_between((wire_x, 0.0, arm_z), (wire_x, 0.0, wire_z + 0.35), radius=0.02, segments=6, mat="mat_galvanized")
    for k in range(4):
        mb.add_cylinder(radius=0.055, depth=0.035, segments=10, location=(wire_x, 0.0, arm_z - 0.12 - k * 0.07), mat="mat_ceramic_white")
    mb.add_tube_between((wire_x + 0.6, 0.0, wire_z + 0.3), (wire_x, 0.0, wire_z + 0.05), radius=0.015, segments=6, mat="mat_galvanized")
    mb.add_cylinder(radius=0.012, depth=12.0, segments=6, location=(wire_x, 0.0, wire_z), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_copper")
    mb.add_cylinder(radius=0.01, depth=12.0, segments=6, location=(wire_x, 0.0, wire_z + 0.35), rotation=(math.pi * 0.5, 0.0, 0.0), mat="mat_steel_dark")
    for dy in (-4.5, -1.5, 1.5, 4.5):
        mb.add_tube_between((wire_x, dy, wire_z + 0.35), (wire_x, dy, wire_z), radius=0.006, segments=4, mat="mat_steel_dark", smooth=False)
        mb.add_box(size=(0.04, 0.06, 0.04), location=(wire_x, dy, wire_z + 0.01), mat="mat_brass")

    # Street lamp on the sidewalk side (+X) and route/stop sign plate
    mb.add_polyline_tube([(0.0, 0.0, pole_h - 0.6), (0.6, 0.0, pole_h - 0.2), (1.6, 0.0, pole_h - 0.1)], [0.045, 0.04, 0.035], segments=8, mat="mat_steel_dark")
    mb.add_box(size=(0.7, 0.32, 0.16), location=(1.85, 0.0, pole_h - 0.15), mat="mat_steel_dark")
    mb.add_box(size=(0.6, 0.26, 0.03), location=(1.85, 0.0, pole_h - 0.24), mat="mat_glass_lens")
    mb.add_box(size=(0.04, 0.45, 0.6), location=(0.17, 0.0, 2.8), mat="mat_enamel_yellow")
    mb.add_box(size=(0.045, 0.35, 0.12), location=(0.175, 0.0, 2.95), mat="mat_vinyl_black")
    for z in (2.55, 3.05):
        mb.add_torus(major_radius=0.13, minor_radius=0.012, major_segments=10, minor_segments=4, location=(0.0, 0.0, z), mat="mat_galvanized")

    mb.build_object()
    add_socket("SOCKET_CONTACT_WIRE", location=(wire_x, 0.0, wire_z))
    add_socket("SOCKET_GROUND_ANCHOR", location=(0.0, 0.0, 0.0))
    return export_world_glb(out_dir, ZONE, "sf_catenary_pole")


def build_sf_hill_road_20m(out_dir: str) -> Dict[str, object]:
    """20 m steep (18 % grade) SF street with sidewalks, ladder crosswalk, stop line and hydrant."""
    clear_scene()
    surf = MeshPartBuilder("sf_hill_road_20m_surface")
    mb = MeshPartBuilder("sf_hill_road_20m_details")
    grade = 0.18
    half_road = 5.5
    cw0, cw1 = -8.5, -5.5
    stop0, stop1 = -5.0, -4.6
    stations = sorted({-10.0, cw0, cw1, stop0, stop1} | {float(s) for s in range(-4, 11)})
    frames = frames_at(stations, grade=grade)

    bars = [(-5.1 + k * 1.2, -4.5 + k * 1.2) for k in range(9)]
    paint = [
        (-0.16, -0.06, "mat_road_paint_yellow"),
        (0.06, 0.16, "mat_road_paint_yellow"),
        (-3.3, -3.2, "mat_road_paint_white"),
        (3.2, 3.3, "mat_road_paint_white"),
    ]
    breaks = [x for bar in bars for x in bar]
    profile, mats = street_profile(half_road, 2.5, paint=paint, extra_breaks=breaks)

    def cell_mat(iu, iv, c):
        if mats[iu] not in ("mat_asphalt", "mat_road_paint_yellow", "mat_road_paint_white"):
            return None
        if cw0 <= c.y <= cw1:
            for a, b in bars:
                if a <= c.x <= b:
                    return "mat_road_paint_white"
            return "mat_asphalt"
        if stop0 <= c.y <= stop1 and 0.16 <= c.x <= half_road:
            return "mat_road_paint_white"
        return None

    sweep_profile(surf, frames, profile, mats, cell_mat=cell_mat)
    surf.build_object(recalc_normals=False)

    def on_grade(x: float, y: float, z: float) -> Tuple[float, float, float]:
        return (x, y, grade * y + z)

    pitch = math.atan(grade)
    # Parking tee marks, storm drain, curb ramps at the crosswalk, hydrant, street sign, curb chocks
    for ty in (-2.0, 3.5, 9.0):
        for sx in (-4.9, 4.9):
            mb.add_box(size=(0.6, 0.1, 0.012), location=on_grade(sx, ty, 0.004), rotation=(pitch, 0.0, 0.0), mat="mat_road_paint_white")
    mb.add_box(size=(0.5, 0.9, 0.04), location=on_grade(-5.2, 1.0, 0.0), rotation=(pitch, 0.0, 0.0), mat="mat_cast_iron")
    for sx in (-6.2, 6.2):
        mb.add_box(size=(1.4, 1.6, 0.05), location=on_grade(sx, -7.0, 0.14), rotation=(pitch, 0.0, 0.0), mat="mat_enamel_yellow")
    hx, hy = 6.2, 1.5
    mb.add_cylinder(radius=0.13, depth=0.65, segments=10, location=on_grade(hx, hy, 0.47), mat="mat_ceramic_white")
    add_ellipsoid(mb, radius=0.15, u_segments=8, v_segments=4, location=on_grade(hx, hy, 0.82), mat="mat_paint_victorian_blue")
    mb.add_cylinder(radius=0.055, depth=0.42, segments=6, location=on_grade(hx, hy, 0.6), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_ceramic_white")
    sx, sy = -6.4, -4.2
    mb.add_cylinder(radius=0.04, depth=3.2, segments=8, location=on_grade(sx, sy, 1.75), mat="mat_galvanized")
    mb.add_box(size=(0.03, 0.9, 0.2), location=on_grade(sx, sy, 3.1), mat="mat_awning_green")
    mb.add_box(size=(0.9, 0.03, 0.2), location=on_grade(sx, sy, 3.35), mat="mat_awning_green")
    mb.add_cylinder(radius=0.3, depth=0.03, segments=8, location=on_grade(sx - 0.05, sy, 2.4), rotation=(0.0, math.pi * 0.5, 0.0), mat="mat_enamel_red", smooth=False)
    for k in range(8):
        ty = -3.0 + k * 1.6
        mb.add_box(size=(2.5, 0.06, 0.01), location=on_grade(-half_road - 1.25, ty, 0.152), rotation=(pitch, 0.0, 0.0), mat="mat_concrete_dark")
        mb.add_box(size=(2.5, 0.06, 0.01), location=on_grade(half_road + 1.25, ty, 0.152), rotation=(pitch, 0.0, 0.0), mat="mat_concrete_dark")
    mb.build_object()

    add_snap_sockets(frames)
    add_socket("SOCKET_CROSSWALK", location=on_grade(0.0, 0.5 * (cw0 + cw1), 0.0))
    return export_world_glb(out_dir, ZONE, "sf_hill_road_20m")


def build_sf_delivery_beacon(out_dir: str) -> Dict[str, object]:
    """Drive-through courier drop marker: glowing ground ring with chevrons and a floating diamond (no lane obstacles)."""
    clear_scene()
    mb = MeshPartBuilder("sf_delivery_beacon")

    # Drop-zone pad and emissive ground ring with chevron arrows
    mb.add_cylinder(radius=2.0, depth=0.04, segments=24, location=(0.0, 0.0, 0.02), mat="mat_concrete_dark", smooth=False)
    mb.add_torus(major_radius=1.85, minor_radius=0.06, major_segments=32, minor_segments=4, location=(0.0, 0.0, 0.06), mat="mat_beacon_glow")
    mb.add_torus(major_radius=1.2, minor_radius=0.03, major_segments=24, minor_segments=4, location=(0.0, 0.0, 0.05), mat="mat_beacon_glow")
    for k in range(4):
        ang = k * math.pi * 0.5
        cx, cy = math.cos(ang) * 1.55, math.sin(ang) * 1.55
        for sgn in (-1.0, 1.0):
            mb.add_box(size=(0.4, 0.07, 0.02), location=(cx, cy, 0.05), rotation=(0.0, 0.0, ang + sgn * 0.6), mat="mat_beacon_glow")

    # Drive-through marker: checkpoints sit on the carriageway, so nothing solid-looking stands in the lane
    # (beacons are collider-free visuals). A floating double-pyramid diamond and halo mark the drop from a distance.
    mb.add_cone(radius1=0.5, radius2=0.0, depth=0.6, segments=4, location=(0.0, 0.0, 3.6), mat="mat_beacon_glow", smooth=False)
    mb.add_cone(radius1=0.0, radius2=0.5, depth=0.6, segments=4, location=(0.0, 0.0, 3.0), mat="mat_beacon_glow", smooth=False)
    mb.add_torus(major_radius=0.75, minor_radius=0.025, major_segments=20, minor_segments=4, location=(0.0, 0.0, 3.3), mat="mat_beacon_glow")
    # Low glowing kerb stubs at the pad's left/right edges frame the drive-through lane without blocking it.
    for sx in (-2.05, 2.05):
        mb.add_box(size=(0.12, 0.9, 0.12), location=(sx, 0.0, 0.06), mat="mat_beacon_glow")

    mb.build_object()
    add_socket("SOCKET_DROP_POINT", location=(0.0, 0.0, 0.04))
    add_socket("SOCKET_BEACON_TOP", location=(0.0, 0.0, 3.9))
    return export_world_glb(out_dir, ZONE, "sf_delivery_beacon")


def generate_all(out_dir: str) -> List[Dict[str, object]]:
    return [
        build_sf_victorian_rowhouse(out_dir),
        build_sf_mission_storefront(out_dir),
        build_sf_muni_rail_road_20m(out_dir),
        build_sf_catenary_pole(out_dir),
        build_sf_hill_road_20m(out_dir),
        build_sf_delivery_beacon(out_dir),
    ]

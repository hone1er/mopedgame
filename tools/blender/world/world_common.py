"""Shared helpers for stylized Bay Area modular world kits (roads, buildings, props).

Coordinate convention (Blender space, before export_yup=True):
  - X: lateral (road centerline at X = 0)
  - Y: along the road / building depth (-Y = front / forward, +Z glTF)
  - Z: up (+Y glTF)

Open road surfaces are authored with explicit counter-clockwise winding (normals up) and
built in a separate "surface" mesh object with recalc_normals=False; closed primitives
(boxes, cylinders, spheres) live in a "solid" mesh object that keeps recalc_normals=True.
"""

from __future__ import annotations

import math
import os
from typing import Callable, Dict, List, Optional, Sequence, Tuple

from mathutils import Vector

from common_utils import MeshPartBuilder, add_socket, export_glb


WORLD_TRI_BUDGET = 3500
WORLD_MAX_EXTENT_M = 30.0

Vec3 = Tuple[float, float, float]
Frame = Tuple[Vector, Vector, Vector]  # (origin, lateral/right, up)
ProfilePoint = Tuple[float, float]  # (lateral offset u, height h)


def world_glb_path(out_dir: str, zone: str, name: str) -> str:
    return os.path.join(out_dir, "World", zone, f"{name}.glb")


def export_world_glb(out_dir: str, zone: str, name: str) -> Dict[str, object]:
    return export_glb(world_glb_path(out_dir, zone, name), tri_budget=WORLD_TRI_BUDGET)


def add_grid(
    mb: MeshPartBuilder,
    rows: Sequence[Sequence[Vector]],
    mat_fn: Callable[[int, int], str],
    smooth: bool = False,
) -> None:
    """Add a quad grid; rows[iv][iu]. Faces wind (u, u+1, v+1) so that +u x +v is the normal."""
    bm = mb.bm
    vrows = [[bm.verts.new(Vector(p)) for p in row] for row in rows]
    for iv in range(len(vrows) - 1):
        for iu in range(len(vrows[iv]) - 1):
            face = bm.faces.new(
                (vrows[iv][iu], vrows[iv][iu + 1], vrows[iv + 1][iu + 1], vrows[iv + 1][iu])
            )
            face.material_index = mb.mat_index(mat_fn(iu, iv))
            face.smooth = smooth


def straight_frames(length: float, segments: int, grade: float = 0.0) -> List[Frame]:
    """Frames along +Y from -length/2 to +length/2 with optional rise/run grade along +Y."""
    frames: List[Frame] = []
    for i in range(segments + 1):
        s = -0.5 * length + length * i / segments
        frames.append((Vector((0.0, s, grade * s)), Vector((1.0, 0.0, 0.0)), Vector((0.0, 0.0, 1.0))))
    return frames


def frames_at(s_values: Sequence[float], grade: float = 0.0) -> List[Frame]:
    """Straight +Y frames at explicit along-road stations (sorted ascending)."""
    return [
        (Vector((0.0, s, grade * s)), Vector((1.0, 0.0, 0.0)), Vector((0.0, 0.0, 1.0)))
        for s in sorted(s_values)
    ]


def arc_frames(length: float, radius: float, segments: int, grade: float = 0.0) -> List[Frame]:
    """Frames along a horizontal arc (bending toward +X) centered on the origin, with grade."""
    frames: List[Frame] = []
    for i in range(segments + 1):
        s = -0.5 * length + length * i / segments
        th = s / radius
        origin = Vector((radius * (1.0 - math.cos(th)), radius * math.sin(th), grade * s))
        lat = Vector((math.cos(th), -math.sin(th), 0.0))
        frames.append((origin, lat, Vector((0.0, 0.0, 1.0))))
    return frames


def frame_heading(frame_a: Frame, frame_b: Frame) -> float:
    """Return Blender Z rotation (radians) aligning local +Y with the path direction a->b."""
    d = frame_b[0] - frame_a[0]
    return math.atan2(-d.x, d.y)


def profile_rows(
    frames: Sequence[Frame],
    profile: Sequence[ProfilePoint],
    displace: Optional[Callable[[Vector, float, float], float]] = None,
) -> List[List[Vector]]:
    rows: List[List[Vector]] = []
    for origin, lat, up in frames:
        row: List[Vector] = []
        for u, h in profile:
            p = origin + lat * u + up * h
            if displace is not None:
                p = p + up * displace(p, u, h)
            row.append(p)
        rows.append(row)
    return rows


def sweep_profile(
    mb: MeshPartBuilder,
    frames: Sequence[Frame],
    profile: Sequence[ProfilePoint],
    band_mats: Sequence[str],
    displace: Optional[Callable[[Vector, float, float], float]] = None,
    cell_mat: Optional[Callable[[int, int, Vector], Optional[str]]] = None,
) -> None:
    """Sweep a left-to-right open cross-section (u, h) along frames into an up-facing surface."""
    if len(band_mats) != len(profile) - 1:
        raise ValueError("band_mats must have len(profile) - 1 entries")
    rows = profile_rows(frames, profile, displace)

    def mat_fn(iu: int, iv: int) -> str:
        if cell_mat is not None:
            centroid = (rows[iv][iu] + rows[iv][iu + 1] + rows[iv + 1][iu] + rows[iv + 1][iu + 1]) * 0.25
            override = cell_mat(iu, iv, centroid)
            if override is not None:
                return override
        return band_mats[iu]

    add_grid(mb, rows, mat_fn)


def street_profile(
    half_road: float,
    sidewalk: float,
    curb_h: float = 0.15,
    paint: Sequence[Tuple[float, float, str]] = (),
    extra_breaks: Sequence[float] = (),
    skirt: float = 0.35,
    asphalt: str = "mat_asphalt",
    walk: str = "mat_concrete",
    curb: str = "mat_concrete_dark",
) -> Tuple[List[ProfilePoint], List[str]]:
    """Return (profile, band_mats) for skirt|sidewalk|curb|asphalt+paint|curb|sidewalk|skirt."""
    edge = half_road + sidewalk
    xs = {-half_road, half_road}
    for a, b, _ in paint:
        xs.add(round(a, 4))
        xs.add(round(b, 4))
    for x in extra_breaks:
        if -half_road < x < half_road:
            xs.add(round(x, 4))
    road_xs = sorted(x for x in xs if -half_road <= x <= half_road)

    pts: List[ProfilePoint] = []
    mats: List[str] = []
    if sidewalk > 0.0:
        pts += [(-edge, -skirt), (-edge, curb_h), (-half_road, curb_h)]
        mats += [walk, walk, curb]
    else:
        pts += [(-half_road, -skirt)]
        mats += [curb]
    pts.append((-half_road, 0.0))
    for x0, x1 in zip(road_xs[:-1], road_xs[1:]):
        mid = 0.5 * (x0 + x1)
        band = asphalt
        for a, b, m in paint:
            if a <= mid <= b:
                band = m
                break
        mats.append(band)
        pts.append((x1, 0.0))
    if sidewalk > 0.0:
        pts += [(half_road, curb_h), (edge, curb_h), (edge, -skirt)]
        mats += [curb, walk, walk]
    else:
        pts += [(half_road, -skirt)]
        mats += [curb]
    return pts, mats


def add_snap_sockets(frames: Sequence[Frame], prefix: str = "SOCKET_SNAP") -> None:
    """Add SOCKET_SNAP_START / SOCKET_SNAP_END empties at path ends, oriented along the path."""
    start, nxt = frames[0], frames[1]
    prev, end = frames[-2], frames[-1]
    add_socket(f"{prefix}_START", location=tuple(start[0]), rotation=(0.0, 0.0, frame_heading(start, nxt)))
    add_socket(f"{prefix}_END", location=tuple(end[0]), rotation=(0.0, 0.0, frame_heading(prev, end)))


def sloped_point(s: float, h: float, angle: float, x: float = 0.0) -> Vec3:
    """Map (along-slope distance s, perpendicular height h) on a plane pitched about X to world."""
    return (x, s * math.cos(angle) - h * math.sin(angle), s * math.sin(angle) + h * math.cos(angle))


def add_extrusion(
    mb: MeshPartBuilder,
    section_xz: Sequence[Tuple[float, float]],
    y0: float,
    y1: float,
    mat: str,
    offset: Vec3 = (0.0, 0.0, 0.0),
) -> None:
    """Extrude a closed (possibly concave) X/Z polygon along Y into a capped prism.

    Intended for builders that keep recalc_normals=True (closed volume).
    """
    bm = mb.bm
    ox, oy, oz = offset
    ring0 = [bm.verts.new((ox + x, oy + y0, oz + z)) for x, z in section_xz]
    ring1 = [bm.verts.new((ox + x, oy + y1, oz + z)) for x, z in section_xz]
    m_idx = mb.mat_index(mat)
    n = len(section_xz)
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((ring0[i], ring0[j], ring1[j], ring1[i])))
    faces.append(bm.faces.new(list(reversed(ring0))))
    faces.append(bm.faces.new(ring1))
    for f in faces:
        f.material_index = m_idx
        f.smooth = False


def add_ellipsoid(
    mb: MeshPartBuilder,
    radius: float,
    u_segments: int = 8,
    v_segments: int = 5,
    location: Vec3 = (0.0, 0.0, 0.0),
    scale: Vec3 = (1.0, 1.0, 1.0),
    mat: str = "mat_foliage_eucalyptus",
    smooth: bool = False,
) -> None:
    """Deterministic lat/long ellipsoid (closed volume, pole triangle fans).

    Used instead of MeshPartBuilder.add_sphere because bmesh.ops.create_uvsphere emits
    faces in a run-to-run varying order, which makes exported .glb index buffers
    non-reproducible.
    """
    bm = mb.bm
    u_n = max(3, u_segments)
    v_n = max(2, v_segments)
    lx, ly, lz = location
    sx, sy, sz = scale

    def vert(theta: float, phi: float):
        return bm.verts.new((
            lx + radius * sx * math.sin(theta) * math.cos(phi),
            ly + radius * sy * math.sin(theta) * math.sin(phi),
            lz + radius * sz * math.cos(theta),
        ))

    top = bm.verts.new((lx, ly, lz + radius * sz))
    bottom = bm.verts.new((lx, ly, lz - radius * sz))
    rings = [
        [vert(math.pi * r / v_n, 2.0 * math.pi * k / u_n) for k in range(u_n)]
        for r in range(1, v_n)
    ]
    m_idx = mb.mat_index(mat)
    faces = []
    for k in range(u_n):
        k1 = (k + 1) % u_n
        faces.append(bm.faces.new((top, rings[0][k], rings[0][k1])))
        for r in range(len(rings) - 1):
            faces.append(bm.faces.new((rings[r][k], rings[r + 1][k], rings[r + 1][k1], rings[r][k1])))
        faces.append(bm.faces.new((bottom, rings[-1][k1], rings[-1][k])))
    for f in faces:
        f.material_index = m_idx
        f.smooth = smooth

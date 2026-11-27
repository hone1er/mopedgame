"""Shared headless Blender 5.3.0 procedural modeling and glTF 2.0 (.glb) export utilities."""

from __future__ import annotations

import math
import os
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector


Vec3 = Tuple[float, float, float]


# Material palette definitions: (BaseColor RGBA, Metallic, Roughness)
MATERIAL_PALETTE: Dict[str, Tuple[Tuple[float, float, float, float], float, float]] = {
    "mat_anodized_alu": ((0.70, 0.72, 0.75, 1.0), 0.88, 0.32),
    "mat_anodized_gold": ((0.84, 0.62, 0.20, 1.0), 0.90, 0.28),
    "mat_anodized_blue": ((0.14, 0.36, 0.74, 1.0), 0.86, 0.30),
    "mat_anodized_red": ((0.74, 0.14, 0.14, 1.0), 0.86, 0.30),
    "mat_cast_alu": ((0.56, 0.58, 0.60, 1.0), 0.78, 0.48),
    "mat_cast_iron": ((0.19, 0.20, 0.22, 1.0), 0.65, 0.68),
    "mat_chrome": ((0.90, 0.92, 0.94, 1.0), 1.00, 0.10),
    "mat_exhaust_steel": ((0.34, 0.31, 0.29, 1.0), 0.84, 0.36),
    "mat_steel_dark": ((0.24, 0.25, 0.27, 1.0), 0.82, 0.45),
    "mat_rubber": ((0.06, 0.06, 0.07, 1.0), 0.00, 0.88),
    "mat_enamel_orange": ((0.72, 0.14, 0.015, 1.0), 0.12, 0.32),
    "mat_enamel_teal": ((0.03, 0.24, 0.22, 1.0), 0.12, 0.32),
    "mat_enamel_yellow": ((0.81, 0.49, 0.018, 1.0), 0.10, 0.34),
    "mat_enamel_red": ((0.56, 0.024, 0.024, 1.0), 0.10, 0.36),
    "mat_enamel_cream": ((0.82, 0.76, 0.62, 1.0), 0.08, 0.36),
    "mat_charcoal_abs": ((0.11, 0.115, 0.125, 1.0), 0.0, 0.54),
    "mat_amber_plastic": ((0.92, 0.56, 0.10, 1.0), 0.00, 0.22),
    "mat_glass_lens": ((0.94, 0.91, 0.80, 1.0), 0.05, 0.12),
    "mat_ceramic_white": ((0.94, 0.93, 0.89, 1.0), 0.00, 0.26),
    "mat_vinyl_black": ((0.10, 0.09, 0.09, 1.0), 0.00, 0.68),
    "mat_vinyl_tan": ((0.46, 0.28, 0.14, 1.0), 0.00, 0.66),
    "mat_crate_blue": ((0.12, 0.32, 0.64, 1.0), 0.00, 0.58),
    "mat_wood_bench": ((0.56, 0.38, 0.22, 1.0), 0.00, 0.62),
    "mat_copper": ((0.82, 0.44, 0.24, 1.0), 0.92, 0.30),
    "mat_brass": ((0.82, 0.68, 0.26, 1.0), 0.92, 0.26),
    "mat_foam_filter": ((0.16, 0.14, 0.13, 1.0), 0.00, 0.92),
    # --- Bay Area world module palette (tools/blender/world/) ---
    "mat_asphalt": ((0.075, 0.075, 0.08, 1.0), 0.00, 0.90),
    "mat_asphalt_patch": ((0.035, 0.035, 0.04, 1.0), 0.00, 0.80),
    "mat_concrete": ((0.52, 0.51, 0.48, 1.0), 0.00, 0.82),
    "mat_concrete_dark": ((0.32, 0.32, 0.31, 1.0), 0.00, 0.86),
    "mat_brick_red": ((0.36, 0.11, 0.06, 1.0), 0.00, 0.84),
    "mat_brick_dark": ((0.22, 0.07, 0.05, 1.0), 0.00, 0.86),
    "mat_road_paint_white": ((0.86, 0.86, 0.82, 1.0), 0.00, 0.60),
    "mat_road_paint_yellow": ((0.86, 0.60, 0.04, 1.0), 0.00, 0.58),
    "mat_road_paint_green": ((0.10, 0.46, 0.22, 1.0), 0.00, 0.62),
    "mat_window_glass": ((0.10, 0.16, 0.20, 1.0), 0.30, 0.08),
    "mat_corrugated_steel": ((0.46, 0.48, 0.50, 1.0), 0.70, 0.46),
    "mat_bark_eucalyptus": ((0.55, 0.47, 0.38, 1.0), 0.00, 0.78),
    "mat_foliage_eucalyptus": ((0.20, 0.32, 0.22, 1.0), 0.00, 0.72),
    "mat_foliage_sage": ((0.36, 0.44, 0.30, 1.0), 0.00, 0.76),
    "mat_dry_grass": ((0.58, 0.46, 0.22, 1.0), 0.00, 0.90),
    "mat_wood_weathered": ((0.40, 0.31, 0.22, 1.0), 0.00, 0.80),
    "mat_sign_brown": ((0.24, 0.14, 0.07, 1.0), 0.00, 0.60),
    "mat_bridge_grey": ((0.50, 0.55, 0.58, 1.0), 0.55, 0.42),
    "mat_galvanized": ((0.62, 0.64, 0.64, 1.0), 0.80, 0.40),
    "mat_paint_victorian_blue": ((0.30, 0.46, 0.62, 1.0), 0.00, 0.56),
    "mat_paint_victorian_lilac": ((0.56, 0.44, 0.62, 1.0), 0.00, 0.56),
    "mat_paint_trim_white": ((0.90, 0.88, 0.82, 1.0), 0.00, 0.50),
    "mat_paint_mission_pink": ((0.86, 0.30, 0.46, 1.0), 0.00, 0.58),
    "mat_paint_mission_turquoise": ((0.08, 0.60, 0.62, 1.0), 0.00, 0.58),
    "mat_paint_mission_marigold": ((0.94, 0.58, 0.08, 1.0), 0.00, 0.56),
    "mat_awning_green": ((0.06, 0.36, 0.20, 1.0), 0.00, 0.70),
    "mat_beacon_glow": ((0.10, 0.90, 0.70, 1.0), 0.00, 0.30),
    # Vintage moped paint & trim (Pedal Code Army stable references: Maxi, Hobbit, Magnum II)
    "mat_enamel_black": ((0.018, 0.018, 0.020, 1.0), 0.10, 0.30),
    "mat_enamel_white": ((0.80, 0.80, 0.77, 1.0), 0.06, 0.32),
    "mat_enamel_slate_blue": ((0.024, 0.155, 0.262, 1.0), 0.14, 0.32),
    "mat_enamel_mag_blue": ((0.035, 0.085, 0.36, 1.0), 0.45, 0.34),
    "mat_enamel_stripe_green": ((0.02, 0.30, 0.16, 1.0), 0.10, 0.34),
    # Non-paintable body trim (names deliberately NOT mat_enamel_*: the paint booth only tints mat_enamel_* submeshes)
    "mat_frame_black": ((0.018, 0.018, 0.020, 1.0), 0.10, 0.30),
    "mat_trim_black": ((0.018, 0.018, 0.020, 1.0), 0.10, 0.30),
    "mat_trim_white": ((0.80, 0.80, 0.77, 1.0), 0.06, 0.32),
    "mat_trim_stripe_green": ((0.02, 0.30, 0.16, 1.0), 0.10, 0.34),
    "mat_trim_red": ((0.56, 0.024, 0.024, 1.0), 0.10, 0.36),
    "mat_lens_red": ((0.62, 0.03, 0.02, 1.0), 0.00, 0.18),
}

# Rear shock eyes sit at X = +/- this offset (frame SOCKET_SHOCK_TOP_L/R); shocks_* .glb files bake the pair.
SHOCK_HALF_SPACING = 0.085

# Every crankcase tilts SOCKET_CYLINDER_BASE forward by about this angle (rad, about +X) so the barrel lies
# near-horizontal like Puch E50 / Honda PA50 engines. Cylinder port sockets counter-rotate by it, so
# exhausts and intakes are authored in the bike frame (-Y forward, +Z up) with the flange at the origin.
CYLINDER_FORWARD_TILT = 1.22


def superellipse_profile(segments: int = 16, exponent: float = 2.6) -> List[Tuple[float, float]]:
    """Unit rounded-rectangle profile (u = lateral, v = normal), counter-clockwise, |u|,|v| <= 1."""
    pts: List[Tuple[float, float]] = []
    seg = max(4, segments)
    for k in range(seg):
        ang = (2.0 * math.pi * k) / seg
        c, s = math.cos(ang), math.sin(ang)
        u = math.copysign(abs(c) ** (2.0 / exponent), c)
        v = math.copysign(abs(s) ** (2.0 / exponent), s)
        pts.append((u, v))
    return pts


def fender_profile(half_width: float, crown: float, thickness: float, segments: int = 8) -> List[Tuple[float, float]]:
    """Closed crescent cross-section of a pressed-steel fender: arched outer skin plus inner skin.

    ``v`` points away from the wheel (sweep normal); the profile spans ``u`` in [-half_width, half_width].
    """
    outer: List[Tuple[float, float]] = []
    seg = max(3, segments)
    for k in range(seg + 1):
        u = -half_width + (2.0 * half_width * k) / seg
        t = u / half_width
        outer.append((u, crown * (1.0 - t * t) + thickness))
    inner = [(u, v - thickness) for (u, v) in reversed(outer)]
    return outer + inner


def arc_points(center_yz: Tuple[float, float], radius: float, deg_from: float, deg_to: float, count: int, x: float = 0.0) -> List[Vec3]:
    """Points on a circle in the YZ plane; 0 deg = straight up, positive = toward the rear (+Y)."""
    out: List[Vec3] = []
    n = max(2, count)
    for i in range(n):
        a = math.radians(deg_from + (deg_to - deg_from) * i / (n - 1))
        out.append((x, center_yz[0] + radius * math.sin(a), center_yz[1] + radius * math.cos(a)))
    return out


# Optional emissive overrides (RGB, strength) for palette materials; only applied when listed.
EMISSIVE_PALETTE: Dict[str, Tuple[Tuple[float, float, float], float]] = {
    "mat_beacon_glow": ((0.10, 0.90, 0.70), 2.0),
}


def clear_scene() -> bpy.types.Scene:
    """Reset Blender to an empty metric (1 unit = 1 m) scene."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    return scene


def get_material(name: str) -> bpy.types.Material:
    """Return or create a stylized glTF-compatible PBR material from MATERIAL_PALETTE."""
    existing = bpy.data.materials.get(name)
    if existing is not None:
        return existing

    base_color, metallic, roughness = MATERIAL_PALETTE.get(
        name, ((0.65, 0.65, 0.65, 1.0), 0.0, 0.5)
    )
    mat = bpy.data.materials.new(name=name)
    if hasattr(mat, "use_nodes") and not mat.use_nodes:
        mat.use_nodes = True
    nt = mat.node_tree
    if nt is None:
        return mat
    bsdf = None
    out_node = None
    for node in nt.nodes:
        if node.type == "BSDF_PRINCIPLED":
            bsdf = node
        elif node.type == "OUTPUT_MATERIAL":
            out_node = node
    if bsdf is None:
        bsdf = nt.nodes.new(type="ShaderNodeBsdfPrincipled")
    if out_node is None:
        out_node = nt.nodes.new(type="ShaderNodeOutputMaterial")
    if not any(link.to_node == out_node for link in nt.links):
        nt.links.new(bsdf.outputs["BSDF"], out_node.inputs["Surface"])

    if "Base Color" in bsdf.inputs:
        bsdf.inputs["Base Color"].default_value = base_color
    if "Metallic" in bsdf.inputs:
        bsdf.inputs["Metallic"].default_value = metallic
    if "Roughness" in bsdf.inputs:
        bsdf.inputs["Roughness"].default_value = roughness
    emissive = EMISSIVE_PALETTE.get(name)
    if emissive is not None:
        rgb, strength = emissive
        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = strength
    return mat


def _make_transform(
    location: Vec3 = (0.0, 0.0, 0.0),
    rotation: Vec3 = (0.0, 0.0, 0.0),
    scale: Vec3 = (1.0, 1.0, 1.0),
) -> Matrix:
    """Build a 4x4 TRS matrix from Euler XYZ angles in radians."""
    mat_loc = Matrix.Translation(Vector(location))
    mat_rot = Euler(rotation, "XYZ").to_matrix().to_4x4()
    mat_sca = Matrix.Diagonal(Vector((scale[0], scale[1], scale[2], 1.0)))
    return mat_loc @ mat_rot @ mat_sca


def _set_faces_material_and_smooth(
    faces: Iterable[bmesh.types.BMFace],
    mat_index: int,
    smooth: bool,
) -> None:
    for face in faces:
        face.material_index = mat_index
        face.smooth = smooth


def ensure_uv0(bm: bmesh.types.BMesh, scale: float = 2.0) -> None:
    """Populate a clean box/triplanar UVMap (TEXCOORD_0) on all mesh loops."""
    uv_layer = bm.loops.layers.uv.verify()
    bm.normal_update()
    for face in bm.faces:
        nx = abs(face.normal.x)
        ny = abs(face.normal.y)
        nz = abs(face.normal.z)
        for loop in face.loops:
            co = loop.vert.co
            if nx >= ny and nx >= nz:
                u, v = co.y * scale, co.z * scale
            elif ny >= nx and ny >= nz:
                u, v = co.x * scale, co.z * scale
            else:
                u, v = co.x * scale, co.y * scale
            loop[uv_layer].uv = (float(u), float(v))


class MeshPartBuilder:
    """Lightweight BMesh builder supporting multiple materials and metric primitives.

    Coordinate convention in Blender space before export_yup=True:
      - X: lateral (0 = centerline, symmetric around X = 0)
      - -Y: forward (+Z in glTF)
      - +Y: rear (-Z in glTF)
      - +Z: up (+Y in glTF)
    """

    def __init__(self, name: str) -> None:
        self.name = name
        self.bm = bmesh.new()
        self.materials: List[str] = []
        self._mat_map: Dict[str, int] = {}

    def mat_index(self, mat_name: str) -> int:
        if mat_name not in self._mat_map:
            idx = len(self.materials)
            self.materials.append(mat_name)
            self._mat_map[mat_name] = idx
        return self._mat_map[mat_name]

    def _tag_new_faces(
        self,
        geom_verts: Iterable[bmesh.types.BMVert],
        mat_name: str,
        smooth: bool,
    ) -> None:
        vert_set = set(geom_verts)
        m_idx = self.mat_index(mat_name)
        faces = {
            f
            for v in vert_set
            for f in v.link_faces
            if all(lv in vert_set for lv in f.verts)
        }
        _set_faces_material_and_smooth(faces, m_idx, smooth)

    def add_box(
        self,
        size: Vec3,
        location: Vec3 = (0.0, 0.0, 0.0),
        rotation: Vec3 = (0.0, 0.0, 0.0),
        mat: str = "mat_anodized_alu",
        smooth: bool = False,
    ) -> None:
        xform = _make_transform(location, rotation, size)
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=xform)
        self._tag_new_faces(res["verts"], mat, smooth)

    def add_cylinder(
        self,
        radius: float,
        depth: float,
        segments: int = 12,
        location: Vec3 = (0.0, 0.0, 0.0),
        rotation: Vec3 = (0.0, 0.0, 0.0),
        mat: str = "mat_anodized_alu",
        smooth: bool = True,
        cap_ends: bool = True,
    ) -> None:
        self.add_cone(
            radius1=radius,
            radius2=radius,
            depth=depth,
            segments=segments,
            location=location,
            rotation=rotation,
            mat=mat,
            smooth=smooth,
            cap_ends=cap_ends,
        )

    def add_cone(
        self,
        radius1: float,
        radius2: float,
        depth: float,
        segments: int = 12,
        location: Vec3 = (0.0, 0.0, 0.0),
        rotation: Vec3 = (0.0, 0.0, 0.0),
        mat: str = "mat_anodized_alu",
        smooth: bool = True,
        cap_ends: bool = True,
    ) -> None:
        xform = _make_transform(location, rotation)
        res = bmesh.ops.create_cone(
            self.bm,
            cap_ends=cap_ends,
            cap_tris=False,
            segments=max(3, segments),
            radius1=radius1,
            radius2=radius2,
            depth=depth,
            matrix=xform,
        )
        self._tag_new_faces(res["verts"], mat, smooth)

    def add_sphere(
        self,
        radius: float,
        u_segments: int = 12,
        v_segments: int = 8,
        location: Vec3 = (0.0, 0.0, 0.0),
        rotation: Vec3 = (0.0, 0.0, 0.0),
        scale: Vec3 = (1.0, 1.0, 1.0),
        mat: str = "mat_anodized_alu",
        smooth: bool = True,
    ) -> None:
        """Deterministic lat/long UV sphere (closed volume, pole triangle fans).

        Built vertex-by-vertex instead of via ``bmesh.ops.create_uvsphere``, which emits faces in a
        run-to-run varying order and makes exported .glb index buffers non-reproducible. Topology
        matches the old primitive (2*u pole tris + u*(v-2) quads), so triangle budgets are unchanged.
        ``scale`` is applied first, then ``rotation`` (Euler XYZ), then ``location``.
        """
        xform = _make_transform(location, rotation, scale)
        bm = self.bm
        u_n = max(6, u_segments)
        v_n = max(4, v_segments)

        def vert(theta: float, phi: float) -> bmesh.types.BMVert:
            local = Vector((
                radius * math.sin(theta) * math.cos(phi),
                radius * math.sin(theta) * math.sin(phi),
                radius * math.cos(theta),
            ))
            return bm.verts.new(xform @ local)

        top = bm.verts.new(xform @ Vector((0.0, 0.0, radius)))
        bottom = bm.verts.new(xform @ Vector((0.0, 0.0, -radius)))
        rings = [
            [vert(math.pi * r / v_n, 2.0 * math.pi * k / u_n) for k in range(u_n)]
            for r in range(1, v_n)
        ]
        m_idx = self.mat_index(mat)
        faces = []
        for k in range(u_n):
            k1 = (k + 1) % u_n
            faces.append(bm.faces.new((top, rings[0][k], rings[0][k1])))
            for r in range(len(rings) - 1):
                faces.append(bm.faces.new((rings[r][k], rings[r + 1][k], rings[r + 1][k1], rings[r][k1])))
            faces.append(bm.faces.new((bottom, rings[-1][k1], rings[-1][k])))
        _set_faces_material_and_smooth(faces, m_idx, smooth)

    def add_tube_between(
        self,
        p0: Vec3,
        p1: Vec3,
        radius: float,
        segments: int = 10,
        mat: str = "mat_chrome",
        smooth: bool = True,
        cap_ends: bool = True,
    ) -> None:
        self.add_tapered_tube_between(
            p0=p0,
            p1=p1,
            r0=radius,
            r1=radius,
            segments=segments,
            mat=mat,
            smooth=smooth,
            cap_ends=cap_ends,
        )

    def add_tapered_tube_between(
        self,
        p0: Vec3,
        p1: Vec3,
        r0: float,
        r1: float,
        segments: int = 10,
        mat: str = "mat_chrome",
        smooth: bool = True,
        cap_ends: bool = True,
    ) -> None:
        v0 = Vector(p0)
        v1 = Vector(p1)
        delta = v1 - v0
        length = delta.length
        if length < 1e-6:
            return
        mid = (v0 + v1) * 0.5
        quat = delta.normalized().to_track_quat("Z", "Y")
        xform = Matrix.Translation(mid) @ quat.to_matrix().to_4x4()
        res = bmesh.ops.create_cone(
            self.bm,
            cap_ends=cap_ends,
            cap_tris=False,
            segments=max(3, segments),
            radius1=r0,
            radius2=r1,
            depth=length,
            matrix=xform,
        )
        self._tag_new_faces(res["verts"], mat, smooth)

    def add_polyline_tube(
        self,
        points: Sequence[Vec3],
        radii: Sequence[float],
        segments: int = 10,
        mat: str = "mat_exhaust_steel",
        smooth: bool = True,
        cap_start: bool = True,
        cap_end: bool = True,
    ) -> None:
        """Loft a smooth connected multi-segment tube along 3D points."""
        if len(points) < 2 or len(points) != len(radii):
            return

        # Filter consecutive duplicate points to prevent zero-length tangents.
        filtered_pts: List[Vector] = [Vector(points[0])]
        filtered_rad: List[float] = [float(radii[0])]
        for pt, r in zip(points[1:], radii[1:]):
            vec = Vector(pt)
            if (vec - filtered_pts[-1]).length > 1e-5:
                filtered_pts.append(vec)
                filtered_rad.append(float(r))
        if len(filtered_pts) < 2:
            return

        m_idx = self.mat_index(mat)
        rings: List[List[bmesh.types.BMVert]] = []
        pts = filtered_pts
        seg = max(3, segments)

        prev_dir: Optional[Vector] = None
        for i, pt in enumerate(pts):
            if i == 0:
                tangent = (pts[1] - pts[0]).normalized()
            elif i == len(pts) - 1:
                tangent = (pts[-1] - pts[-2]).normalized()
            else:
                d0 = (pt - pts[i - 1]).normalized()
                d1 = (pts[i + 1] - pt).normalized()
                tangent = d0 + d1
                if tangent.length < 1e-5:
                    tangent = d1
                else:
                    tangent.normalize()

            if prev_dir is None:
                ref = Vector((1.0, 0.0, 0.0)) if abs(tangent.x) < 0.9 else Vector((0.0, 0.0, 1.0))
                side = tangent.cross(ref).normalized()
            else:
                side = prev_dir - tangent * prev_dir.dot(tangent)
                if side.length < 1e-5:
                    ref = Vector((1.0, 0.0, 0.0)) if abs(tangent.x) < 0.9 else Vector((0.0, 0.0, 1.0))
                    side = tangent.cross(ref).normalized()
                else:
                    side.normalize()
            prev_dir = side
            up = tangent.cross(side).normalized()

            r = filtered_rad[i]
            ring: List[bmesh.types.BMVert] = []
            for k in range(seg):
                ang = (2.0 * math.pi * k) / seg
                offset = side * (math.cos(ang) * r) + up * (math.sin(ang) * r)
                ring.append(self.bm.verts.new(pt + offset))
            rings.append(ring)

        new_faces: List[bmesh.types.BMFace] = []
        for i in range(len(rings) - 1):
            r_a = rings[i]
            r_b = rings[i + 1]
            for k in range(seg):
                k_next = (k + 1) % seg
                try:
                    f = self.bm.faces.new((r_a[k], r_a[k_next], r_b[k_next], r_b[k]))
                    new_faces.append(f)
                except ValueError:
                    pass

        if cap_start:
            try:
                f = self.bm.faces.new(list(reversed(rings[0])))
                new_faces.append(f)
            except ValueError:
                pass
        if cap_end:
            try:
                f = self.bm.faces.new(rings[-1])
                new_faces.append(f)
            except ValueError:
                pass

        _set_faces_material_and_smooth(new_faces, m_idx, smooth)

    def add_profile_sweep(
        self,
        points: Sequence[Vec3],
        profile: Sequence[Tuple[float, float]],
        scales: Optional[Sequence[Tuple[float, float]]] = None,
        mat: str = "mat_enamel_orange",
        smooth: bool = True,
        cap_start: bool = True,
        cap_end: bool = True,
    ) -> None:
        """Sweep a closed 2D profile along a path (pressed-steel spines, tanks, seats, fenders).

        The profile's ``u`` axis stays on world +X (lateral) and ``v`` is ``X x tangent`` (up when the path runs
        toward the rear), so paths should run mostly in the YZ plane. ``scales`` holds per-point (u, v)
        multipliers; omit it to use the profile in meters.
        """
        if len(points) < 2 or len(profile) < 3:
            return
        sc = list(scales) if scales is not None else [(1.0, 1.0)] * len(points)
        if len(sc) != len(points):
            return
        pts = [Vector(p) for p in points]
        x_axis = Vector((1.0, 0.0, 0.0))
        rings: List[List[bmesh.types.BMVert]] = []
        for i, pt in enumerate(pts):
            if i == 0:
                tangent = pts[1] - pts[0]
            elif i == len(pts) - 1:
                tangent = pts[-1] - pts[-2]
            else:
                tangent = (pts[i] - pts[i - 1]).normalized() + (pts[i + 1] - pts[i]).normalized()
            tangent.normalize()
            side = x_axis - tangent * x_axis.dot(tangent)
            side.normalize()
            normal = side.cross(tangent).normalized()
            su, sv = sc[i]
            rings.append([self.bm.verts.new(pt + side * (u * su) + normal * (v * sv)) for (u, v) in profile])

        m_idx = self.mat_index(mat)
        n = len(profile)
        faces: List[bmesh.types.BMFace] = []
        for i in range(len(rings) - 1):
            a, b = rings[i], rings[i + 1]
            for k in range(n):
                k1 = (k + 1) % n
                try:
                    faces.append(self.bm.faces.new((a[k], b[k], b[k1], a[k1])))
                except ValueError:
                    pass
        if cap_start:
            try:
                faces.append(self.bm.faces.new(rings[0]))
            except ValueError:
                pass
        if cap_end:
            try:
                faces.append(self.bm.faces.new(list(reversed(rings[-1]))))
            except ValueError:
                pass
        _set_faces_material_and_smooth(faces, m_idx, smooth)

    def add_torus(
        self,
        major_radius: float,
        minor_radius: float,
        major_segments: int = 24,
        minor_segments: int = 8,
        location: Vec3 = (0.0, 0.0, 0.0),
        rotation: Vec3 = (0.0, 0.0, 0.0),
        mat: str = "mat_rubber",
        smooth: bool = True,
    ) -> None:
        """Create a torus ring in the local XY plane around local Z."""
        xform = _make_transform(location, rotation)
        m_idx = self.mat_index(mat)
        maj = max(6, major_segments)
        min_s = max(4, minor_segments)
        rings: List[List[bmesh.types.BMVert]] = []
        for i in range(maj):
            theta = (2.0 * math.pi * i) / maj
            ct, st = math.cos(theta), math.sin(theta)
            ring: List[bmesh.types.BMVert] = []
            for j in range(min_s):
                phi = (2.0 * math.pi * j) / min_s
                cp, sp = math.cos(phi), math.sin(phi)
                rr = major_radius + minor_radius * cp
                local_co = Vector((rr * ct, rr * st, minor_radius * sp))
                ring.append(self.bm.verts.new(xform @ local_co))
            rings.append(ring)

        new_faces: List[bmesh.types.BMFace] = []
        for i in range(maj):
            i_next = (i + 1) % maj
            for j in range(min_s):
                j_next = (j + 1) % min_s
                try:
                    f = self.bm.faces.new(
                        (
                            rings[i][j],
                            rings[i_next][j],
                            rings[i_next][j_next],
                            rings[i][j_next],
                        )
                    )
                    new_faces.append(f)
                except ValueError:
                    pass
        _set_faces_material_and_smooth(new_faces, m_idx, smooth)

    def add_hex_bolt(
        self,
        radius: float = 0.006,
        height: float = 0.006,
        location: Vec3 = (0.0, 0.0, 0.0),
        rotation: Vec3 = (0.0, 0.0, 0.0),
        mat: str = "mat_chrome",
    ) -> None:
        self.add_cylinder(
            radius=radius,
            depth=height,
            segments=6,
            location=location,
            rotation=rotation,
            mat=mat,
            smooth=False,
        )

    def add_fin_stack(
        self,
        center: Vec3,
        size_xy: Tuple[float, float],
        fin_count: int,
        fin_thickness: float,
        fin_pitch: float,
        mat: str = "mat_cast_iron",
        rotation: Vec3 = (0.0, 0.0, 0.0),
    ) -> None:
        cx, cy, cz = center
        rot_mat = Euler(rotation, "XYZ").to_matrix()
        for i in range(fin_count):
            local_offset = Vector((0.0, 0.0, (i - 0.5 * (fin_count - 1)) * fin_pitch))
            world_offset = rot_mat @ local_offset
            pos = (cx + world_offset.x, cy + world_offset.y, cz + world_offset.z)
            self.add_box(
                size=(size_xy[0], size_xy[1], fin_thickness),
                location=pos,
                rotation=rotation,
                mat=mat,
                smooth=False,
            )

    def build_object(self, triangulate: bool = False, recalc_normals: bool = True) -> bpy.types.Object:
        """Finalize BMesh into a Blender Mesh object with UV0 and unit scale.

        Pass recalc_normals=False for open surfaces (road ribbons) whose winding is
        authored explicitly, since recalc_face_normals is only reliable on closed volumes.
        """
        if recalc_normals:
            bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        if triangulate:
            bmesh.ops.triangulate(self.bm, faces=self.bm.faces)
        ensure_uv0(self.bm)

        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()

        for mat_name in self.materials:
            mesh.materials.append(get_material(mat_name))

        mesh.update()
        if len(mesh.uv_layers) == 0:
            raise RuntimeError(f"Mesh '{self.name}' has no UV0 layer")
        if len(mesh.polygons) == 0:
            raise RuntimeError(f"Mesh '{self.name}' has zero polygons")

        obj = bpy.data.objects.new(self.name, mesh)
        obj.location = (0.0, 0.0, 0.0)
        obj.rotation_euler = (0.0, 0.0, 0.0)
        obj.scale = (1.0, 1.0, 1.0)
        bpy.context.scene.collection.objects.link(obj)
        return obj


def _add_empty(
    name: str,
    location: Vec3 = (0.0, 0.0, 0.0),
    rotation: Vec3 = (0.0, 0.0, 0.0),
    display_size: float = 0.03,
) -> bpy.types.Object:
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = display_size
    obj.location = Vector(location)
    obj.rotation_euler = Euler(rotation, "XYZ")
    obj.scale = (1.0, 1.0, 1.0)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def add_socket(
    name: str,
    location: Vec3 = (0.0, 0.0, 0.0),
    rotation: Vec3 = (0.0, 0.0, 0.0),
) -> bpy.types.Object:
    """Create a named SOCKET_* Empty node exported into the glTF node hierarchy."""
    if not name.startswith("SOCKET_"):
        raise ValueError(f"Socket name must start with 'SOCKET_': {name}")
    return _add_empty(name, location=location, rotation=rotation, display_size=0.035)


def add_bolt(
    name: str,
    location: Vec3 = (0.0, 0.0, 0.0),
    rotation: Vec3 = (0.0, 0.0, 0.0),
) -> bpy.types.Object:
    """Create a named BOLT_* Empty node exported into the glTF node hierarchy."""
    if not name.startswith("BOLT_"):
        raise ValueError(f"Bolt name must start with 'BOLT_': {name}")
    return _add_empty(name, location=location, rotation=rotation, display_size=0.02)


def count_scene_triangles() -> int:
    """Return total triangulated polygon count across all mesh objects in the scene."""
    total = 0
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.data is None:
            continue
        for poly in obj.data.polygons:
            vcount = len(poly.vertices)
            if vcount >= 3:
                total += vcount - 2
    return total


def export_glb(filepath: str, tri_budget: Optional[int] = None) -> Dict[str, object]:
    """Validate scene transforms/UV0/tri budget and export binary glTF 2.0 (.glb)."""
    abs_path = os.path.abspath(filepath)
    os.makedirs(os.path.dirname(abs_path), exist_ok=True)

    mesh_objects = [o for o in bpy.data.objects if o.type == "MESH"]
    if not mesh_objects:
        raise RuntimeError(f"No mesh objects in scene for export: {abs_path}")

    for obj in bpy.data.objects:
        # Ensure exact unit scale across every exported node.
        obj.scale = (1.0, 1.0, 1.0)
        if obj.type == "MESH":
            if len(obj.data.polygons) == 0:
                raise RuntimeError(f"Empty mesh '{obj.name}' for export: {abs_path}")
            if len(obj.data.uv_layers) == 0:
                raise RuntimeError(f"Missing UV0 on '{obj.name}' for export: {abs_path}")

    tris = count_scene_triangles()
    if tri_budget is not None and tris > tri_budget:
        raise RuntimeError(
            f"Triangle count {tris} exceeds budget {tri_budget} for {abs_path}"
        )

    if not hasattr(bpy.ops.export_scene, "gltf"):
        import addon_utils

        addon_utils.enable("io_scene_gltf2", default_set=False, persistent=False)

    rna_props = {
        prop.identifier
        for prop in bpy.ops.export_scene.gltf.get_rna_type().properties
    }
    desired_kwargs: Dict[str, object] = {
        "filepath": abs_path,
        "export_format": "GLB",
        "export_yup": True,
        "export_texcoords": True,
        "export_normals": True,
        "export_materials": "EXPORT",
        "export_apply": True,
        "export_cameras": False,
        "export_lights": False,
        "use_selection": False,
    }
    export_kwargs = {k: v for k, v in desired_kwargs.items() if k in rna_props}
    result = bpy.ops.export_scene.gltf(**export_kwargs)
    if "FINISHED" not in result:
        raise RuntimeError(f"glTF export failed for {abs_path}: {result}")

    return {
        "filepath": abs_path,
        "tris": tris,
        "nodes": [o.name for o in bpy.data.objects],
    }

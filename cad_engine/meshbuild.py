"""Display meshes and the watertight solid used for STL export.

Display geometry is a cutaway: hot-wall liner, circular coolant passages,
and a closeout jacket with a window. The printable solid is the same wall
with those passages subtracted, so the STL is a manifold part.
"""

from __future__ import annotations

import numpy as np
import trimesh

from engine_math.design import evaluate


def _downsample_indices(n: int, target: int) -> np.ndarray:
    target = max(8, min(int(target), n))
    return np.unique(np.linspace(0, n - 1, target).astype(int))


def _wall_profile(design: dict, n_axial: int | None = None) -> np.ndarray:
    """Closed (r, z) loop of the metal cross-section, meters. Axis at r = 0."""
    wall = design["wall"]
    idx = _downsample_indices(len(wall.z_m), n_axial or len(wall.z_m))
    z = wall.z_m[idx]
    r_out = wall.r_jacket_outer_m[idx]
    r_gas = wall.r_gas_m[idx]
    pts: list[tuple[float, float]] = [(0.0, 0.0), (float(r_out[0]), 0.0)]
    pts.extend((float(r), float(zz)) for r, zz in zip(r_out, z))
    pts.append((float(r_gas[-1]), float(z[-1])))
    pts.extend((float(r), float(zz)) for r, zz in zip(r_gas[-2::-1], z[-2::-1]))
    pts.append((0.0, float(z[0])))
    cleaned = [pts[0]]
    for p in pts[1:]:
        if (p[0] - cleaned[-1][0]) ** 2 + (p[1] - cleaned[-1][1]) ** 2 > 1e-14:
            cleaned.append(p)
    if (cleaned[-1][0] - cleaned[0][0]) ** 2 + (cleaned[-1][1] - cleaned[0][1]) ** 2 < 1e-14:
        cleaned.pop()
    return np.asarray(cleaned, dtype=float)


def channel_paths(design: dict, n_samples: int = 42) -> list[np.ndarray]:
    """Centerlines in meters, shape (n_samples, 3), from injector toward the exit."""
    wall = design["wall"]
    gas = design["gas"]
    z0 = float(gas.gas_face_z_m + 0.005)
    z1 = float(gas.exit_z_m - 0.008)
    if z1 <= z0 + 0.01:
        z1 = z0 + 0.01
    zz = np.linspace(z0, z1, n_samples)
    rr = np.interp(zz, wall.z_m, wall.r_channel_m)
    turns = float(design.get("helix_turns", 0.0))
    count = int(design["inputs"]["channel_count"])
    span = zz[-1] - zz[0]
    paths = []
    for i in range(count):
        theta = 2.0 * np.pi * i / count + 2.0 * np.pi * turns * (zz - zz[0]) / span
        paths.append(np.stack([rr * np.cos(theta), rr * np.sin(theta), zz], axis=1))
    return paths


def _orient(mesh: trimesh.Trimesh, *, repair_winding: bool = True) -> trimesh.Trimesh:
    """Clean a mesh and make its signed volume positive.

    `fix_normals` is right for a single solid body. On a boolean result it
    flips the faces of enclosed cooling passages and adds their volume back,
    so those meshes only get an invert when the sign is wrong.
    """
    mesh.merge_vertices()
    mesh.update_faces(mesh.unique_faces() & mesh.nondegenerate_faces())
    mesh.remove_unreferenced_vertices()
    if repair_winding:
        mesh.fix_normals()
    if mesh.is_volume and mesh.volume < 0:
        mesh.invert()
    return mesh


def revolve_polygon(loop_rz: np.ndarray, n_theta: int = 64) -> trimesh.Trimesh:
    """Revolve a closed r-z loop around z into a solid. `loop_rz` columns are (r, z)."""
    thetas = np.linspace(0.0, 2.0 * np.pi, n_theta, endpoint=False)
    vertices: list[tuple[float, float, float]] = []
    rings: list[tuple[str, int]] = []
    for radius, z in loop_rz:
        if radius <= 1e-8:
            rings.append(("axis", len(vertices)))
            vertices.append((0.0, 0.0, float(z)))
        else:
            rings.append(("ring", len(vertices)))
            c = np.cos(thetas)
            s = np.sin(thetas)
            vertices.extend((float(radius * c[j]), float(radius * s[j]), float(z)) for j in range(n_theta))

    def connect(a: tuple[str, int], b: tuple[str, int]) -> list[tuple[int, int, int]]:
        faces: list[tuple[int, int, int]] = []
        if a[0] == "axis" and b[0] == "axis":
            return faces
        if a[0] == "axis" and b[0] == "ring":
            for j in range(n_theta):
                j2 = (j + 1) % n_theta
                faces.append((a[1], b[1] + j, b[1] + j2))
        elif a[0] == "ring" and b[0] == "axis":
            for j in range(n_theta):
                j2 = (j + 1) % n_theta
                faces.append((b[1], a[1] + j2, a[1] + j))
        else:
            for j in range(n_theta):
                j2 = (j + 1) % n_theta
                a0, a1 = a[1] + j, a[1] + j2
                b0, b1 = b[1] + j, b[1] + j2
                faces.append((a0, b0, b1))
                faces.append((a0, b1, a1))
        return faces

    faces: list[tuple[int, int, int]] = []
    for i in range(len(rings)):
        faces.extend(connect(rings[i], rings[(i + 1) % len(rings)]))
    mesh = trimesh.Trimesh(vertices=np.asarray(vertices), faces=np.asarray(faces, dtype=np.int64), process=False)
    return _orient(mesh)


def make_tube(points: np.ndarray, radius: float, n_seg: int = 10, *, process: bool = True) -> trimesh.Trimesh:
    """Closed tube swept along `points` with a constant radius."""
    tangents = np.gradient(points, axis=0)
    tangents /= np.clip(np.linalg.norm(tangents, axis=1, keepdims=True), 1e-12, None)
    normals = np.zeros_like(points)
    binormals = np.zeros_like(points)
    seed = np.array([1.0, 0.0, 0.0]) if abs(float(tangents[0, 2])) > 0.85 else np.array([0.0, 0.0, 1.0])
    n0 = np.cross(tangents[0], seed)
    n0 /= np.linalg.norm(n0)
    b0 = np.cross(tangents[0], n0)
    normals[0] = n0
    binormals[0] = b0
    for i in range(1, len(points)):
        n = normals[i - 1] - tangents[i] * np.dot(normals[i - 1], tangents[i])
        length = float(np.linalg.norm(n))
        if length < 1e-8:
            n = np.cross(tangents[i], binormals[i - 1])
            length = float(np.linalg.norm(n))
        n = n / length
        b = np.cross(tangents[i], n)
        normals[i] = n
        binormals[i] = b

    angles = np.linspace(0.0, 2.0 * np.pi, n_seg, endpoint=False)
    cos_a = np.cos(angles)
    sin_a = np.sin(angles)
    n_pts = len(points)
    ring = np.empty((n_pts, n_seg, 3))
    for i in range(n_pts):
        ring[i] = points[i] + radius * (cos_a[:, None] * normals[i] + sin_a[:, None] * binormals[i])
    verts = ring.reshape(-1, 3)
    faces: list[tuple[int, int, int]] = []
    for i in range(n_pts - 1):
        for j in range(n_seg):
            j2 = (j + 1) % n_seg
            a = i * n_seg + j
            b = i * n_seg + j2
            c = (i + 1) * n_seg + j2
            d = (i + 1) * n_seg + j
            faces.append((a, d, c))
            faces.append((a, c, b))
    start_center = len(verts)
    end_center = start_center + 1
    verts = np.vstack([verts, points[0], points[-1]])
    for j in range(n_seg):
        j2 = (j + 1) % n_seg
        faces.append((start_center, j2, j))
        faces.append((end_center, (n_pts - 1) * n_seg + j, (n_pts - 1) * n_seg + j2))
    mesh = trimesh.Trimesh(vertices=verts, faces=np.asarray(faces, dtype=np.int64), process=False)
    return _orient(mesh) if process else mesh


def build_solid(
    design: dict,
    *,
    n_theta: int = 56,
    n_axial: int = 110,
    tube_samples: int = 36,
    tube_sections: int = 10,
) -> trimesh.Trimesh:
    """Watertight metal solid in millimeters, cooling passages removed."""
    wall_mesh = revolve_polygon(_wall_profile(design, n_axial=n_axial), n_theta=n_theta)
    if not wall_mesh.is_watertight or wall_mesh.volume <= 0:
        raise RuntimeError("Chamber revolve is not a closed solid.")
    radius = 0.46 * float(design["wall"].channel_diameter_m)
    tubes = [make_tube(path, radius, n_seg=tube_sections) for path in channel_paths(design, tube_samples)]
    for tube in tubes:
        if tube.volume <= 0:
            raise RuntimeError("A coolant passage failed to close.")
    cut = trimesh.boolean.difference([wall_mesh, *tubes], engine="manifold")
    if cut is None or cut.volume <= 0:
        raise RuntimeError("Cooling-passage boolean failed.")
    _orient(cut, repair_winding=False)
    # Keep the meter-space volume, then convert the export mesh to millimeters.
    cut.metadata = {"volume_m3": float(cut.volume)}
    cut.apply_scale(1000.0)
    return cut


def _theta_count(full: bool, n_theta: int) -> tuple[np.ndarray, bool]:
    if full:
        return np.linspace(0.0, 2.0 * np.pi, n_theta, endpoint=False), True
    return np.linspace(0.0, 1.0, n_theta), False


def revolved_annulus(
    z_m: np.ndarray,
    r0_m: np.ndarray,
    r1_m: np.ndarray,
    i0: np.ndarray,
    i1: np.ndarray,
    *,
    theta0: float,
    theta1: float,
    n_theta: int,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Closed (or partial) annulus. Vertices are millimeters; intensity matches vertices."""
    full = abs((theta1 - theta0) - 2.0 * np.pi) < 1e-8
    if full:
        thetas = np.linspace(theta0, theta1, n_theta, endpoint=False)
    else:
        thetas = np.linspace(theta0, theta1, n_theta, endpoint=True)
    n_th = len(thetas)
    n_z = len(z_m)
    c = np.cos(thetas)
    s = np.sin(thetas)

    def grid(radius: np.ndarray, intens: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        x = radius[:, None] * c[None, :] * 1000.0
        y = radius[:, None] * s[None, :] * 1000.0
        zz = np.broadcast_to(z_m[:, None], (n_z, n_th)) * 1000.0
        vals = np.broadcast_to(intens[:, None], (n_z, n_th))
        return np.stack([x, y, zz], axis=-1).reshape(-1, 3), np.array(vals, dtype=float).reshape(-1)

    v0, s0 = grid(r0_m, i0)
    v1, s1 = grid(r1_m, i1)
    vertices = np.vstack([v0, v1])
    intensity = np.concatenate([s0, s1])

    def vid(layer: int, i: int, j: int) -> int:
        return layer * n_z * n_th + i * n_th + j

    faces: list[tuple[int, int, int]] = []
    j_count = n_th if full else n_th - 1
    for i in range(n_z - 1):
        for j in range(j_count):
            j2 = (j + 1) % n_th if full else j + 1
            a, b = vid(1, i, j), vid(1, i, j2)
            c3, d = vid(1, i + 1, j2), vid(1, i + 1, j)
            faces.append((a, d, c3))
            faces.append((a, c3, b))
            a, b = vid(0, i, j), vid(0, i, j2)
            c3, d = vid(0, i + 1, j2), vid(0, i + 1, j)
            faces.append((a, b, c3))
            faces.append((a, c3, d))
    for j in range(j_count):
        j2 = (j + 1) % n_th if full else j + 1
        faces.append((vid(0, 0, j), vid(1, 0, j), vid(1, 0, j2)))
        faces.append((vid(0, 0, j), vid(1, 0, j2), vid(0, 0, j2)))
        last = n_z - 1
        faces.append((vid(0, last, j), vid(0, last, j2), vid(1, last, j2)))
        faces.append((vid(0, last, j), vid(1, last, j2), vid(1, last, j)))
    if not full:
        for j, sign in ((0, -1), (n_th - 1, 1)):
            for i in range(n_z - 1):
                a, b = vid(0, i, j), vid(1, i, j)
                c3, d = vid(1, i + 1, j), vid(0, i + 1, j)
                if sign < 0:
                    faces.append((a, d, c3))
                    faces.append((a, c3, b))
                else:
                    faces.append((a, b, c3))
                    faces.append((a, c3, d))
    return vertices, np.asarray(faces, dtype=np.int64), intensity


def revolved_surface(
    z_m: np.ndarray,
    r_m: np.ndarray,
    intens: np.ndarray,
    *,
    n_theta: int,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    thetas = np.linspace(0.0, 2.0 * np.pi, n_theta, endpoint=False)
    n_th = len(thetas)
    n_z = len(z_m)
    x = r_m[:, None] * np.cos(thetas)[None, :] * 1000.0
    y = r_m[:, None] * np.sin(thetas)[None, :] * 1000.0
    zz = np.broadcast_to(z_m[:, None], (n_z, n_th)) * 1000.0
    vals = np.broadcast_to(intens[:, None], (n_z, n_th))
    vertices = np.stack([x, y, zz], axis=-1).reshape(-1, 3)
    intensity = np.array(vals, dtype=float).reshape(-1)
    faces: list[tuple[int, int, int]] = []
    for i in range(n_z - 1):
        for j in range(n_th):
            j2 = (j + 1) % n_th
            a = i * n_th + j
            b = i * n_th + j2
            c3 = (i + 1) * n_th + j2
            d = (i + 1) * n_th + j
            faces.append((a, d, c3))
            faces.append((a, c3, b))
    return vertices, np.asarray(faces, dtype=np.int64), intensity


def _tube_display(path_m: np.ndarray, radius_m: float, temps: np.ndarray, n_seg: int = 8):
    mesh = make_tube(path_m, radius_m, n_seg=n_seg, process=False)
    # make_tube vertex order is rings then two centers. Paint rings from station temps.
    n_pts = len(path_m)
    intensity = np.empty(len(mesh.vertices))
    ring = np.repeat(temps, n_seg)
    intensity[: n_pts * n_seg] = ring
    intensity[n_pts * n_seg :] = temps[0], temps[-1]
    return mesh.vertices * 1000.0, mesh.faces, intensity


def _sample_design(design: dict, n_axial: int = 100):
    wall = design["wall"]
    thermal = design["thermal"]
    idx = _downsample_indices(len(wall.z_m), n_axial)
    z = wall.z_m[idx]
    tw = thermal.wall_temp_k[idx]
    tc = thermal.coolant_temp_k[idx]
    q = thermal.heat_flux_w_m2[idx]
    h = max(float(thermal.h_coolant), 1.0)
    t_root = np.minimum(tw, tc + q / h)
    return {
        "z": z,
        "r_gas": wall.r_gas_m[idx],
        "r_liner": wall.r_liner_outer_m[idx],
        "r_jacket_in": wall.r_jacket_inner_m[idx],
        "r_jacket_out": wall.r_jacket_outer_m[idx],
        "tw": tw,
        "tc": tc,
        "t_root": t_root,
    }


def build_display(design: dict, mode: str = "cutaway") -> list[dict]:
    """Meshes in millimeters for the Plotly viewport."""
    s = _sample_design(design, 96)
    traces: list[dict] = []
    gas = design["gas"]
    bulk_r = float(s["r_jacket_out"][0])
    bulk_h = float(gas.bulkhead_m)
    bulk_t = float(s["tw"][0])
    cyl = trimesh.creation.cylinder(radius=bulk_r * 1000.0, height=bulk_h * 1000.0, sections=40)
    cyl.apply_translation((0.0, 0.0, bulk_h * 500.0))
    traces.append(
        {
            "vertices": np.asarray(cyl.vertices),
            "faces": np.asarray(cyl.faces),
            "intensity": np.full(len(cyl.vertices), bulk_t),
            "name": "Injector bulkhead",
            "opacity": 1.0,
        }
    )

    if mode == "exterior":
        v, f, inten = revolved_annulus(
            s["z"],
            s["r_jacket_in"],
            s["r_jacket_out"],
            s["tw"],
            s["tw"],
            theta0=0.0,
            theta1=2.0 * np.pi,
            n_theta=48,
        )
        traces.append({"vertices": v, "faces": f, "intensity": inten, "name": "Closeout", "opacity": 1.0})
        v, f, inten = revolved_surface(s["z"], s["r_gas"], s["tw"], n_theta=40)
        traces.append({"vertices": v, "faces": f, "intensity": inten, "name": "Gas wall", "opacity": 1.0})
        return traces

    v, f, inten = revolved_annulus(
        s["z"],
        s["r_gas"],
        s["r_liner"],
        s["tw"],
        s["t_root"],
        theta0=0.0,
        theta1=2.0 * np.pi,
        n_theta=48,
    )
    traces.append({"vertices": v, "faces": f, "intensity": inten, "name": "Hot wall", "opacity": 1.0})

    window = 1.05
    v, f, inten = revolved_annulus(
        s["z"],
        s["r_jacket_in"],
        s["r_jacket_out"],
        s["tc"],
        s["tc"],
        theta0=window,
        theta1=2.0 * np.pi - window,
        n_theta=40,
    )
    traces.append({"vertices": v, "faces": f, "intensity": inten, "name": "Closeout", "opacity": 1.0})

    thermal = design["thermal"]
    radius = 0.46 * float(design["wall"].channel_diameter_m)
    blocks_v = []
    blocks_f = []
    blocks_i = []
    offset = 0
    for path in channel_paths(design, n_samples=28):
        temps = np.interp(path[:, 2], thermal.z_m, thermal.coolant_temp_k)
        v, f, inten = _tube_display(path, radius, temps, n_seg=7)
        blocks_v.append(v)
        blocks_f.append(f + offset)
        blocks_i.append(inten)
        offset += len(v)
    if blocks_v:
        traces.append(
            {
                "vertices": np.vstack(blocks_v),
                "faces": np.vstack(blocks_f),
                "intensity": np.concatenate(blocks_i),
                "name": "Coolant",
                "opacity": 1.0,
            }
        )
    return traces


def design_from_params(params: dict) -> dict:
    """Accept either a finished design or the flight-point input dict."""
    if "wall" in params and "thermal" in params:
        return params
    from engine_math.design import reference_inputs

    merged = reference_inputs()
    for key, value in params.items():
        if key in merged and np.isscalar(value):
            merged[key] = value
    if "design_expansion_ratio" in params:
        merged["design_expansion_ratio"] = params["design_expansion_ratio"]
    return evaluate(merged)

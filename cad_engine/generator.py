"""STL and STEP export.

`generate_engine_stl` matches the project brief: a params dict in, a printable
file path out. Cooling passages are boolean-subtracted in the mesh kernel.
The STEP file is a CadQuery solid of revolution; swept passage cuts are
attempted and, if the kernel rejects a path, the exact wall solid is kept.
"""

from __future__ import annotations

from lazyimp import lazy

Path = (lazy)("pathlib", "Path")
np = (lazy)("numpy")
cq = (lazy)("cadquery")
_wall_profile = (lazy)("cad_engine.meshbuild", "_wall_profile")
build_solid = (lazy)("cad_engine.meshbuild", "build_solid")
channel_paths = (lazy)("cad_engine.meshbuild", "channel_paths")
design_from_params = (lazy)("cad_engine.meshbuild", "design_from_params")

ROOT = Path(__file__).resolve().parents[1]
OUTPUTS = ROOT / "outputs"


def generate_engine_stl(params: dict, output_path: str = "outputs/engine.stl", **mesh_kwargs) -> str:
    """Generate a 3D-printable engine with internal cooling passages."""
    design = design_from_params(params)
    mesh = build_solid(design, **mesh_kwargs)
    path = Path(output_path)
    if not path.is_absolute():
        path = ROOT / path
    path.parent.mkdir(parents=True, exist_ok=True)
    mesh.export(path)
    return str(path)


def _revolved_wall(loop_mm: np.ndarray):
    pts = [(float(r), float(z)) for r, z in loop_mm]
    return cq.Workplane("XZ").polyline(pts).close().revolve(360, (0, 0, 0), (0, 0, 1))


def generate_engine_step(
    params: dict,
    output_path: str = "outputs/engine.step",
    *,
    cut_channels: bool = True,
) -> tuple[str, str]:
    """Write a STEP solid. Returns (path, note)."""
    design = design_from_params(params)
    loop = _wall_profile(design, n_axial=80) * 1000.0
    # Keep the axis point off exact zero so the revolve stays a valid solid.
    loop[:, 0] = np.maximum(loop[:, 0], 0.02)
    solid = _revolved_wall(loop)
    note = "STEP is the revolved chamber and nozzle wall."
    if cut_channels:
        try:
            radius = 0.46 * float(design["wall"].channel_diameter_m) * 1000.0
            for path_m in channel_paths(design, n_samples=28):
                sample = path_m[:: max(1, len(path_m) // 24)] * 1000.0
                if len(sample) < 4:
                    sample = path_m * 1000.0
                vectors = [cq.Vector(float(p[0]), float(p[1]), float(p[2])) for p in sample]
                wire = cq.Wire.assembleEdges([cq.Edge.makeSpline(vectors)])
                tube = cq.Workplane("XY").circle(radius).sweep(wire, isFrenet=True, combine=False)
                solid = solid.cut(tube)
            note = "STEP includes the cooling passages as swept cuts."
        except Exception as exc:  # noqa: BLE001 — kernel failures are reported, not fatal
            solid = _revolved_wall(loop)
            note = (
                "STEP is the chamber wall. Passage cuts were rejected by the CAD kernel "
                f"({exc.__class__.__name__}); the STL still contains them."
            )
    path = Path(output_path)
    if not path.is_absolute():
        path = ROOT / path
    path.parent.mkdir(parents=True, exist_ok=True)
    cq.exporters.export(solid, str(path))
    return str(path), note


def export_engine(params: dict, *, cut_step_channels: bool = False) -> dict:
    """Build STL bytes, optional STEP, and mesh statistics."""
    design = design_from_params(params)
    mesh = build_solid(design)
    stl_path = OUTPUTS / "engine.stl"
    stl_path.parent.mkdir(parents=True, exist_ok=True)
    mesh.export(stl_path)
    loaded = mesh
    step_bytes = None
    step_note = "STEP export was skipped."
    try:
        step_path, step_note = generate_engine_step(
            design, output_path=str(OUTPUTS / "engine.step"), cut_channels=cut_step_channels
        )
        step_bytes = Path(step_path).read_bytes()
    except Exception as exc:  # noqa: BLE001
        step_note = f"STEP export failed ({exc.__class__.__name__}: {exc}). The STL is still valid."
    volume_cm3 = float(mesh.metadata.get("volume_m3", 0.0)) * 1e6
    return {
        "stl_bytes": stl_path.read_bytes(),
        "stl_path": str(stl_path),
        "step_bytes": step_bytes,
        "step_note": step_note,
        "triangles": int(len(loaded.faces)),
        "volume_cm3": volume_cm3,
        "watertight": bool(loaded.is_watertight),
        "bounds_mm": loaded.bounds.tolist(),
    }

"""Contour mesh, cooling passages, and file export."""

from __future__ import annotations

from lazyimp import lazy

Path = (lazy)("pathlib", "Path")
np = (lazy)("numpy")
trimesh = (lazy)("trimesh")
pytest = (lazy)("pytest")
generate_engine_stl = (lazy)("cad_engine.generator", "generate_engine_stl")
build_display = (lazy)("cad_engine.meshbuild", "build_display")
build_solid = (lazy)("cad_engine.meshbuild", "build_solid")
make_tube = (lazy)("cad_engine.meshbuild", "make_tube")
_wall_profile = (lazy)("cad_engine.meshbuild", "_wall_profile")
revolve_polygon = (lazy)("cad_engine.meshbuild", "revolve_polygon")
evaluate = (lazy)("engine_math.design", "evaluate")
reference_inputs = (lazy)("engine_math.design", "reference_inputs")
render_station_png = (lazy)("engine_math.figures", "render_station_png")


def test_tube_volume_matches_a_cylinder():
    length = 0.08
    radius = 0.002
    points = np.stack([np.zeros(20), np.zeros(20), np.linspace(0.0, length, 20)], axis=1)
    mesh = make_tube(points, radius, n_seg=16)
    assert mesh.is_watertight
    assert mesh.volume == pytest.approx(np.pi * radius**2 * length, rel=0.08)


def test_display_meshes_have_matching_intensity():
    design = evaluate(reference_inputs(channel_count=12))
    for mode in ("cutaway", "exterior"):
        traces = build_display(design, mode)
        assert len(traces) >= 2
        for trace in traces:
            assert len(trace["intensity"]) == len(trace["vertices"])
            assert trace["faces"].max() < len(trace["vertices"])
            assert np.isfinite(trace["vertices"]).all()
            assert np.isfinite(trace["intensity"]).all()


def test_printable_solid_loses_volume_when_channels_are_cut(tmp_path: Path):
    design = evaluate(reference_inputs(channel_count=10, helix_turns=0.0))
    kwargs = dict(n_theta=36, n_axial=70, tube_samples=22, tube_sections=8)
    solid = build_solid(design, **kwargs)
    uncut = revolve_polygon(_wall_profile(design, n_axial=70), n_theta=36)
    assert solid.is_watertight
    assert solid.metadata["volume_m3"] < uncut.volume * 0.995
    span = solid.bounds[1] - solid.bounds[0]
    assert span[2] > 50.0
    path = generate_engine_stl(design, output_path=str(tmp_path / "engine.stl"), **kwargs)
    loaded = trimesh.load(path)
    assert len(loaded.faces) > 1000


def test_contour_png(tmp_path: Path):
    design = evaluate(reference_inputs())
    path = render_station_png(design, tmp_path / "contour.png")
    assert Path(path).stat().st_size > 5_000

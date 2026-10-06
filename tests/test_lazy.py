"""Deferred imports stay unloaded until a name is actually used."""

from lazyimp import lazy

pickle = (lazy)("pickle")
subprocess = (lazy)("subprocess")
sys = (lazy)("sys")
hashlib = (lazy)("hashlib")
CacheType = (lazy)("streamlit.runtime.caching.cache_type", "CacheType")
update_hash = (lazy)("streamlit.runtime.caching.hashing", "update_hash")


def test_proxy_reduce_ignores_the_loaded_module():
    binding = (lazy)("math", "sqrt")
    before = binding.__reduce__()
    assert binding(4) == 2
    assert binding.__reduce__() == before
    assert pickle.loads(pickle.dumps(binding))(9) == 3


def test_numeric_and_mapping_proxies():
    g0 = (lazy)("engine_math.presets", "G0")
    r_univ = (lazy)("engine_math.presets", "R_UNIV")
    ambients = (lazy)("engine_math.presets", "AMBIENTS")
    assert 1000.0 / (2.0 * g0) > 0.0
    assert r_univ / 21.0 > 100.0
    assert "sea_level" in ambients
    assert ambients["sea_level"]["pressure_pa"] == 101325
    assert "vacuum" in list(ambients)


def test_streamlit_hash_stays_stable_after_the_import_resolves():
    binding = (lazy)("math", "pi")

    def digest() -> str:
        hasher = hashlib.md5()
        update_hash(binding, hasher, CacheType.DATA)
        return hasher.hexdigest()

    first = digest()
    assert float(binding) > 3.0
    assert digest() == first


def test_heavy_libraries_stay_unloaded_until_use():
    script = r"""
import sys
import engine_math.nozzle
import cad_engine.generator
import engine_math.figures
heavy = ("numpy", "scipy", "trimesh", "cadquery", "streamlit", "matplotlib", "plotly")
loaded = [name for name in heavy if name in sys.modules]
assert loaded == [], loaded
from engine_math.nozzle import calculate_nozzle_parameters
calculate_nozzle_parameters(25000, 3.5e6, 101325)
for name in ("numpy", "scipy"):
    assert name in sys.modules, name
for name in ("cadquery", "trimesh", "matplotlib", "streamlit", "plotly"):
    assert name not in sys.modules, name
"""
    proc = subprocess.run(
        [sys.executable, "-c", script],
        cwd="/workspace",
        capture_output=True,
        text=True,
    )
    assert proc.returncode == 0, proc.stdout + proc.stderr

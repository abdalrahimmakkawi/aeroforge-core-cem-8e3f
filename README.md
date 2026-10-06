# NeuralOS

Generative propulsion geometry. NeuralOS sizes a rocket chamber and bell nozzle from thrust, chamber pressure, and a frozen-γ gas model, then builds a parametric solid with regenerative cooling passages and a thermal map of the wall.

The viewport is a cutaway: hot-wall temperature on the liner, coolant temperature in the passages. The STL is a watertight millimeter solid with those passages subtracted. STEP is a CadQuery solid of the same wall.

This is preliminary design, not equilibrium chemistry and not conjugate heat transfer. Preset gas properties are representative ideal-gas values. Heat flux uses the Bartz correlation. Coolant-side convection uses Dittus–Boelter.

## Run

```bash
python3 -m venv venv
source venv/bin/activate
pip install --upgrade pip
pip install -r requirements.txt
streamlit run app.py
```

The app listens on port **8742**. Open [http://127.0.0.1:8742](http://127.0.0.1:8742).

```bash
pytest
```

Libraries load on first use. Each import is written `(lazy)("module")` or `(lazy)("module", "name")`, so NumPy, SciPy, Trimesh, Matplotlib, and CadQuery stay out of the process until a calculation, a mesh, or an export actually needs them.

## Layout

```
app.py                  Streamlit dashboard
engine_math/nozzle.py   Isentropic throat, expansion ratio, c*, Cf, Isp
engine_math/contour.py  Chamber, throat arcs, parabolic bell
engine_math/thermal.py  Bartz heat flux and regenerative coolant march
engine_math/design.py   One call that returns the full firing point
cad_engine/meshbuild.py Cutaway meshes and the printable solid
cad_engine/generator.py STL and STEP export
llm_client.py           OpenAI-compatible client for NVIDIA NIM
outputs/                Generated STL, STEP, and figures
```

`generate_engine_stl(params)` writes `outputs/engine.stl`. Pass the same flight-point dictionary the dashboard uses, or the scalar dictionary returned by `calculate_nozzle_parameters`.

## LLM (NVIDIA NIM)

Inference uses the OpenAI Python SDK against NVIDIA Build:

```bash
cp .env.example .env   # set NVIDIA_API_KEY
python -c "from llm_client import chat_text; print(chat_text([{'role':'user','content':'ping'}]))"
```

Default preferred model: `nvidia/nemotron-4-340b-instruct` (`meta/llama-3.3-70b-instruct` is retired on this endpoint). Override with `NVIDIA_MODEL` in `.env` if that model is not enabled for your key. Alternate: `nvidia/llama-3.1-nemotron-70b-instruct`. Base URL: `https://integrate.api.nvidia.com/v1`.
## Geometry kernel

CadQuery builds the STEP solid of revolution. Trimesh and manifold3d subtract the coolant passages for the STL, which keeps the interactive view fast and the print file manifold. PicoGK is the open voxel kernel (C# / C++) to reach for when a design needs true volumetric lattices rather than swept circular passages. It is not a runtime dependency of this Python MVP.

## Model notes

- Sea level, 10 km, and 20 km size an adapted nozzle (`Pe = Pa`). Vacuum sizes a chosen expansion ratio and reports the underexpanded thrust.
- `L*` sets the cylindrical chamber length after the convergent volume is accounted for.
- Channel diameter is chosen for about 22 m/s of fuel flow, then reduced so the throat lands stay above 1.4 mm.
- Switching the liner from GRCop-42 to Inconel 718 is the quick way to see why a copper hot wall exists.

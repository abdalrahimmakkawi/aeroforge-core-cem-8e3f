# AeroForge Studio

Internal AI computational propulsion workbench for designing novel 3D-printable jet and rocket engine components (combustion chambers, nozzles, injectors, aerospikes).

**Stack:** Next.js 15 + React Three Fiber · FastAPI (NVIDIA NIM) · .NET PicoGK voxel kernel

## Quick start

```bash
# One-time: Python venv + frontend deps + .NET kernel
python3 -m venv venv && ./venv/bin/pip install -r requirements.txt
cd frontend && npm install && cd ..
cp .env.example .env   # set NVIDIA_API_KEY (+ optional NVIDIA_MODEL)

# Launch API (:5000) + UI (:43123)
./scripts/dev.sh
```

- UI: [http://127.0.0.1:43123](http://127.0.0.1:43123)
- API health: [http://127.0.0.1:5000/api/health](http://127.0.0.1:5000/api/health)

## Studio layout

| Pane | Role |
|------|------|
| AI Command Console | Natural-language prompt, presets, agent reasoning |
| 3D Viewport | STL mesh, orbit controls, wireframe / thermal, section cut |
| Thermodynamic Controls | Thrust, chamber pressure, propellant |
| PicoGK Code & Metrics | Generated C#, \(A_t\), \(\varepsilon\), Inconel mass, STL download |

## Architecture

```
frontend/                              Next.js App Router studio UI
backend/ai_engine/                     FastAPI + NIM → EngineSpecs JSON only
backend/geometry_kernel/
  AlgorithmX/                          Field-based generative propulsion (default)
    PhysicsEngine.cs                   Rao gas-core solid
    ImplicitDensityGyroid.cs           Throat-dense TPMS SDF
    GyroidHeatExchangerEngine.cs       Conformal gyroid cooling matrix
    BiomimeticManifoldEngine.cs        Murray-law branching feeds
    AlgorithmicInjectorEngine.cs       60–120 coax swirlers + LOX/CH₄ plenums
    AlgorithmXMaster.cs                Monolithic field union orchestrator
  AeroForge.Core/                      Flight-grade CEM (mode: flight fallback)
  PicoRunner.cs                        Legacy Roslyn path (optional)
scripts/dev.sh                         Single-command local startup
```

Design flow: prompt + sliders → NIM extracts **EngineSpecs JSON** → C#
`AeroForgeEngine.Build(specs)` (GasDynamics → Rao → hollow wall → cooling
subtract → manifolds → injector) → watertight STL → R3F viewport.
Typical compile: **under 5 seconds**.

LEAP 71 dependencies (auto-fetched by `scripts/build_kernel.sh`):

```bash
./scripts/fetch_leap71.sh   # ShapeKernel + LatticeLibrary into third_party/
```

Without a valid `NVIDIA_API_KEY`, the orchestrator falls back to deterministic ShapeKernel
builders sized from isentropic relations so the studio still forges STLs.

## Environment

```bash
NVIDIA_API_KEY=nvapi-...
# Optional override if preferred models are unavailable on your key:
NVIDIA_MODEL=google/gemma-4-31b-it
```

## Kernel only

```bash
./scripts/build_kernel.sh
```

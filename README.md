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
frontend/                 Next.js App Router studio UI
backend/ai_engine/        FastAPI + isentropic thermo + NVIDIA NIM orchestrator
backend/geometry_kernel/  .NET PicoGK builders + Roslyn PicoRunner
scripts/dev.sh            Single-command local startup
```

Design flow: prompt + sliders → NIM creative engineer JSON → ShapeKernel `BasePipe` /
`ControlPointSpline` builders (+ PicoGK voxels) → watertight STL under
`frontend/public/outputs/` → R3F viewport.

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

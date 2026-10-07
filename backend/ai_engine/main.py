"""AeroForge Studio API — NVIDIA NIM + PicoGK geometry kernel."""

from __future__ import annotations

import json
import os
import subprocess
import time
import uuid
from pathlib import Path
from typing import Any, Literal

from dotenv import load_dotenv
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

from . import orchestrator, thermo

ROOT = Path(__file__).resolve().parents[2]
load_dotenv(ROOT / ".env", override=False)

OUTPUT_DIR = ROOT / "frontend" / "public" / "outputs"
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
KERNEL = ROOT / "backend" / "geometry_kernel" / "bin" / "Release" / "net9.0" / "AeroForge.Geometry"
INCONEL_DENSITY = 8.19  # g/cm^3

app = FastAPI(title="AeroForge Studio", version="1.0.0")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
app.mount("/outputs", StaticFiles(directory=str(OUTPUT_DIR)), name="outputs")


class DesignRequest(BaseModel):
    prompt: str = Field(..., min_length=3)
    thrust_kn: float = Field(20.0, ge=5.0, le=100.0)
    chamber_pressure_mpa: float = Field(3.5, ge=1.0, le=15.0)
    propellant: Literal["lox_methane", "lox_rp1", "lox_lh2"] = "lox_methane"
    preset: Literal["aerospike", "regenerative", "swirler"] = "aerospike"
    ambient_pressure_pa: float = 101325.0
    use_ai_script: bool = False


class DesignResponse(BaseModel):
    ok: bool
    job_id: str
    reasoning: str
    model: str
    thermo: dict[str, Any]
    preset: str
    geometry: dict[str, Any]
    picogk_csharp: str
    stl_url: str
    stl_path: str
    metrics: dict[str, Any]
    compile_ms: float
    kernel_mode: str


def _run_kernel(payload: dict[str, Any]) -> dict[str, Any]:
    if not KERNEL.exists():
        raise HTTPException(500, f"Geometry kernel missing at {KERNEL}. Run: scripts/build_kernel.sh")
    spec_path = OUTPUT_DIR / f"spec_{payload.get('job_id', uuid.uuid4().hex)}.json"
    spec_path.write_text(json.dumps(payload), encoding="utf-8")
    env = os.environ.copy()
    env["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1"
    t0 = time.perf_counter()
    proc = subprocess.run(
        [str(KERNEL), str(spec_path)],
        capture_output=True,
        text=True,
        cwd=str(KERNEL.parent),
        env=env,
        timeout=180,
    )
    dt = (time.perf_counter() - t0) * 1000.0
    stdout_lines = (proc.stdout or "").strip().splitlines()
    # PicoGK may log "Disposing Library" after earlier prints — find last JSON object.
    result: dict[str, Any] = {"ok": False, "error": "no kernel JSON"}
    for line in reversed(stdout_lines):
        line = line.strip()
        if line.startswith("{") and line.endswith("}"):
            try:
                result = json.loads(line)
                break
            except json.JSONDecodeError:
                continue
    else:
        last = stdout_lines[-1] if stdout_lines else ""
        result = {"ok": False, "error": last or (proc.stderr or "")[-500:]}
    result["compile_ms"] = dt
    result["stderr"] = (proc.stderr or "")[-2000:]
    result["returncode"] = proc.returncode
    return result


@app.get("/api/health")
def health() -> dict[str, Any]:
    return {
        "ok": True,
        "kernel": KERNEL.exists(),
        "model": orchestrator._model(),
        "base_url": orchestrator.NVIDIA_BASE_URL,
    }


@app.get("/api/propellants")
def propellants() -> dict[str, Any]:
    return thermo.PROPELLANTS


@app.post("/api/design", response_model=DesignResponse)
def design(req: DesignRequest) -> DesignResponse:
    # Skip blocking NIM by default — UI sliders + thermo size EngineSpecs.
    # use_ai_script=True opts into a short NIM enrichment pass (hard-timed out).
    plan = orchestrator.orchestrate(
        req.prompt,
        thrust_kn=req.thrust_kn,
        chamber_pressure_mpa=req.chamber_pressure_mpa,
        propellant=req.propellant,
        preset=req.preset,
        ambient_pressure_pa=req.ambient_pressure_pa,
        skip_llm=not req.use_ai_script,
    )
    job_id = uuid.uuid4().hex[:12]
    stl_name = f"engine_{job_id}.stl"
    stl_path = OUTPUT_DIR / stl_name
    # Also keep a stable name for the viewport auto-reload convention.
    stable = OUTPUT_DIR / "engine_output.stl"

    geo = plan["geometry"]
    engine_specs = plan.get("engine_specs") if isinstance(plan.get("engine_specs"), dict) else {}
    # Primary path: deterministic AeroForge.Core CEM from EngineSpecs JSON.
    # Legacy Roslyn script mode only when explicitly requested AND preset needs it.
    use_core = plan["preset"] == "regenerative" or not req.use_ai_script
    payload: dict[str, Any] = {
        "job_id": job_id,
        "mode": "core" if use_core else ("script" if req.use_ai_script else "core"),
        "preset": plan["preset"],
        "thrust_kn": req.thrust_kn,
        "chamber_pressure_mpa": req.chamber_pressure_mpa,
        "throat_radius_mm": geo["throat_radius_mm"],
        "exit_radius_mm": geo["exit_radius_mm"],
        "length_mm": geo["length_mm"],
        "expansion_ratio": plan["thermo"]["expansion_ratio"],
        "channel_count": geo["channel_count"],
        "channel_radius_mm": geo["channel_radius_mm"],
        "wall_mm": geo["wall_mm"],
        "helix_turns": geo["helix_turns"],
        "voxel_mm": geo["voxel_mm"],
        "output": str(stl_path),
        "csharp": plan["picogk_csharp"],
        "engine_specs": engine_specs,
        # Flat EngineSpecs fields for AeroForge.Core.EngineSpecs.FromJson
        "ThrustkN": engine_specs.get("ThrustkN", req.thrust_kn),
        "ChamberPressureBar": engine_specs.get(
            "ChamberPressureBar", req.chamber_pressure_mpa * 10.0
        ),
        "CoolingChannelCount": engine_specs.get("CoolingChannelCount", geo["channel_count"]),
        "WallThicknessMm": engine_specs.get("WallThicknessMm", geo["wall_mm"]),
        "ChannelDepthMm": engine_specs.get("ChannelDepthMm", 2.5),
        "HelixTurns": engine_specs.get("HelixTurns", geo["helix_turns"]),
        "PipeDiameterMm": engine_specs.get("PipeDiameterMm", 18.0),
        "AmbientPressurePa": req.ambient_pressure_pa,
    }

    kernel_mode = "core"
    result = _run_kernel(payload)
    if not result.get("ok") and payload.get("mode") == "script":
        payload["mode"] = "core"
        kernel_mode = "core_fallback"
        result = _run_kernel(payload)
    elif result.get("ok"):
        kernel_mode = str(result.get("mode") or payload["mode"])

    if not result.get("ok") or not stl_path.exists():
        raise HTTPException(
            500,
            detail={
                "message": "Geometry kernel failed",
                "error": result.get("error"),
                "stderr": result.get("stderr"),
            },
        )

    stable.write_bytes(stl_path.read_bytes())
    size = stl_path.stat().st_size
    # Rough metal volume from STL size is wrong; use thermo length * annular estimate.
    rt = geo["throat_radius_mm"] / 10.0  # cm
    re = geo["exit_radius_mm"] / 10.0
    L = geo["length_mm"] / 10.0
    vol_cm3 = max(8.0, 0.35 * 3.1416 * ((re**2 + rt**2) / 2) * L * 0.15)
    mass_kg = vol_cm3 * INCONEL_DENSITY / 1000.0

    return DesignResponse(
        ok=True,
        job_id=job_id,
        reasoning=plan["reasoning"],
        model=plan["model"],
        thermo=plan["thermo"],
        preset=plan["preset"],
        geometry=geo,
        picogk_csharp=plan["picogk_csharp"],
        stl_url=f"/outputs/{stl_name}",
        stl_path=str(stl_path),
        metrics={
            "stl_bytes": size,
            "est_volume_cm3": round(vol_cm3, 2),
            "est_mass_kg_inconel718": round(mass_kg, 3),
            "density_g_cm3": INCONEL_DENSITY,
            "throat_area_m2": plan["thermo"]["throat_area_m2"],
            "expansion_ratio": plan["thermo"]["expansion_ratio"],
            "isp_s": plan["thermo"]["isp_s"],
            "cf": plan["thermo"]["cf"],
        },
        compile_ms=float(result.get("compile_ms", 0.0)),
        kernel_mode=kernel_mode,
    )


@app.get("/api/stl/{name}")
def download_stl(name: str) -> FileResponse:
    path = OUTPUT_DIR / name
    if not path.exists():
        # stable alias
        if name == "engine_output.stl":
            path = OUTPUT_DIR / "engine_output.stl"
        if not path.exists():
            raise HTTPException(404, "STL not found")
    return FileResponse(path, media_type="model/stl", filename=name)

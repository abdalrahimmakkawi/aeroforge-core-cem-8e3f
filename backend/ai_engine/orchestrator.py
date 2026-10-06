"""NVIDIA NIM creative propulsion orchestrator."""

from __future__ import annotations

import json
import os
import re
from pathlib import Path
from typing import Any

from dotenv import load_dotenv
from openai import OpenAI

from . import thermo
from .prompts import SYSTEM

ROOT = Path(__file__).resolve().parents[2]
load_dotenv(ROOT / ".env", override=False)

NVIDIA_BASE_URL = "https://integrate.api.nvidia.com/v1"
PREFERRED_MODEL = "nvidia/nemotron-4-340b-instruct"
# meta/llama-3.3-70b-instruct is EOL on integrate.api.nvidia.com


def _client() -> OpenAI:
    key = (os.environ.get("NVIDIA_API_KEY") or "").strip()
    if not key:
        raise RuntimeError("NVIDIA_API_KEY missing in environment / .env")
    return OpenAI(api_key=key, base_url=NVIDIA_BASE_URL)


def _model() -> str:
    return (os.environ.get("NVIDIA_MODEL") or PREFERRED_MODEL).strip()


def _extract_json(text: str) -> dict[str, Any]:
    text = text.strip()
    if text.startswith("```"):
        text = re.sub(r"^```(?:json)?\s*", "", text)
        text = re.sub(r"\s*```$", "", text)
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        m = re.search(r"\{[\s\S]*\}", text)
        if not m:
            raise
        return json.loads(m.group(0))


def _infer_preset(prompt: str, preset_hint: str) -> str:
    p = (prompt or "").lower()
    hint = (preset_hint or "").lower()
    if "aerospike" in p or "spike" in p:
        return "aerospike"
    if "swirl" in p or "injector" in p:
        return "swirler"
    if any(k in p for k in ("nozzle", "bell", "regen", "chamber", "rocket engine")):
        return "regenerative"
    if hint in ("aerospike", "regenerative", "swirler", "regen"):
        return "regenerative" if hint == "regen" else hint
    return "regenerative"


def orchestrate(
    prompt: str,
    *,
    thrust_kn: float,
    chamber_pressure_mpa: float,
    propellant: str,
    preset: str,
    ambient_pressure_pa: float = 101325.0,
) -> dict[str, Any]:
    sized = thermo.size_engine(
        thrust_n=thrust_kn * 1000.0,
        chamber_pressure_pa=chamber_pressure_mpa * 1e6,
        ambient_pressure_pa=ambient_pressure_pa,
        propellant=propellant,
    )
    inferred = _infer_preset(prompt, preset)

    user = {
        "prompt": prompt,
        "constraints": {
            "thrust_kn": thrust_kn,
            "chamber_pressure_mpa": chamber_pressure_mpa,
            "propellant": propellant,
            "preset_hint": inferred,
            "thermo": sized,
            "cad_stack": ["PicoGK", "Leap71.ShapeKernel", "Leap71.LatticeLibrary", "AeroForge.Geometry.EngineBuilders"],
        },
    }

    reasoning = ""
    ai: dict[str, Any] = {}
    model_used = _model()
    try:
        client = _client()
        stream = client.chat.completions.create(
            model=model_used,
            temperature=0.25,
            max_tokens=1800,
            stream=True,
            messages=[
                {"role": "system", "content": SYSTEM},
                {"role": "user", "content": json.dumps(user)},
            ],
        )
        chunks: list[str] = []
        for piece in stream:
            delta = piece.choices[0].delta.content if piece.choices else None
            if delta:
                chunks.append(delta)
        raw = "".join(chunks)
        ai = _extract_json(raw)
        reasoning = str(ai.get("reasoning") or raw[:1200])
    except Exception as exc:  # noqa: BLE001 — fall back to deterministic ShapeKernel builders
        reasoning = (
            f"NIM call unavailable ({exc.__class__.__name__}: {exc}). "
            "Falling back to ShapeKernel BasePipe regenerative / aerospike builders "
            "sized from isentropic thermo."
        )
        ai = {
            "preset": inferred,
            "geometry": {},
            "picogk_csharp": _default_csharp(inferred, sized),
        }

    geo_in = ai.get("geometry") if isinstance(ai.get("geometry"), dict) else {}
    chosen_preset = str(ai.get("preset") or inferred).lower()
    if chosen_preset == "regen":
        chosen_preset = "regenerative"
    if chosen_preset not in ("aerospike", "regenerative", "swirler"):
        chosen_preset = inferred

    raw_voxel = float(geo_in.get("voxel_mm", 1.2))
    # Larger engines need coarser voxels for interactive STL size.
    min_vox = 1.5 if thrust_kn >= 40 else 1.2
    voxel_mm = min(2.0, max(min_vox, raw_voxel if raw_voxel >= min_vox else min_vox))
    geometry = {
        "throat_radius_mm": float(geo_in.get("throat_radius_mm", sized["throat_radius_mm"])),
        "exit_radius_mm": float(geo_in.get("exit_radius_mm", sized["exit_radius_mm"])),
        "length_mm": float(geo_in.get("length_mm", sized["length_mm"])),
        # Modular CEM cooling channels: 32–64 helical starts (visible outer ribs).
        "channel_count": int(min(64, max(32, geo_in.get("channel_count", 48)))),
        "channel_radius_mm": float(geo_in.get("channel_radius_mm", 1.2)),
        "wall_mm": float(max(3.0, geo_in.get("wall_mm", 3.5))),
        "helix_turns": float(geo_in.get("helix_turns", 2.6)),
        "voxel_mm": voxel_mm,
    }
    csharp = str(ai.get("picogk_csharp") or _default_csharp(chosen_preset, sized | geometry))

    return {
        "reasoning": reasoning,
        "model": model_used,
        "thermo": sized,
        "preset": chosen_preset,
        "geometry": geometry,
        "picogk_csharp": csharp,
    }


def _default_csharp(preset: str, geo: dict[str, Any]) -> str:
    builder = {
        "swirler": "BuildSwirler",
        "aerospike": "BuildAerospike",
    }.get(preset, "BuildRegenBell")
    rt = float(geo.get("throat_radius_mm", 38))
    re = float(geo.get("exit_radius_mm", 95))
    length = float(geo.get("length_mm", 220))
    eps = float(geo.get("expansion_ratio", 8))
    if builder == "BuildRegenBell":
        channels = int(min(64, max(32, geo.get("channel_count", 48))))
        return f"""
using AeroForge.Modules;

var specs = new EngineSpecs {{
    rThroat = {rt:0.3f}f,
    rExit = {re:0.3f}f,
    rInjector = {rt * 2.35:0.3f}f,
    length = {length:0.3f}f,
    wallMm = 3.5f,
    expansionRatio = {eps:0.3f}f,
    channelCount = {channels},
    channelDepthMm = 2.5f
}};

// 1. Build Base Chamber
Voxels chamber = ChamberBuilder.BuildRaoBell(specs.rThroat, specs.rExit, specs.length, specs.wallMm);

// 2. Add Visible Surface Cooling Channels
Voxels channels = CoolingChannelBuilder.BuildHelical(chamber, count: specs.channelCount, depthMm: specs.channelDepthMm);
chamber.BoolSubtract(channels);

// 3. Attach Visible Distribution Manifolds & Fuel Ports
Voxels manifolds = ManifoldBuilder.BuildToroidalRings(specs.rInjector, pipeDiameter: 18.0f, rExit: specs.rExit, length: specs.length);
chamber.BoolAdd(manifolds);

// 4. Wrap with Structural Ribs/Lattice
Voxels latticeJacket = StructuralLatticeBuilder.BuildIsoGrid(chamber, ribThickness: 1.5f);
chamber.BoolAdd(latticeJacket);

return chamber.mshAsMesh();
""".strip()
    return f"""
var spec = new EngineSpec {{
    Preset = "{preset}",
    ThroatRadiusMm = {rt:0.3f}f,
    ExitRadiusMm = {re:0.3f}f,
    LengthMm = {length:0.3f}f,
    ChannelCount = 16,
    ChannelRadiusMm = 1.35f,
    WallMm = 2.4f,
    HelixTurns = 2.6f,
    ExpansionRatio = {eps:0.3f}f
}};
return EngineBuilders.{builder}(lib, spec);
""".strip()

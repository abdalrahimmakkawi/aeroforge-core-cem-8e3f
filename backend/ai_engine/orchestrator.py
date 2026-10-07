"""NVIDIA NIM orchestrator — extracts EngineSpecs JSON for AeroForge.Core."""

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
            "cad_stack": [
                "AeroForge.Core",
                "GasDynamics",
                "RaoContour",
                "PicoGK",
                "Leap71.ShapeKernel",
            ],
        },
    }

    reasoning = ""
    ai: dict[str, Any] = {}
    model_used = _model()
    try:
        client = _client()
        stream = client.chat.completions.create(
            model=model_used,
            temperature=0.2,
            max_tokens=1200,
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
    except Exception as exc:  # noqa: BLE001
        reasoning = (
            f"NIM unavailable ({exc.__class__.__name__}: {exc}). "
            "Falling back to deterministic EngineSpecs sized from isentropic thermo; "
            "AeroForge.Core will compile the STL."
        )
        ai = {"preset": inferred, "geometry": {}, "engine_specs": {}}

    geo_in = ai.get("geometry") if isinstance(ai.get("geometry"), dict) else {}
    specs_in = ai.get("engine_specs") if isinstance(ai.get("engine_specs"), dict) else {}
    chosen_preset = str(ai.get("preset") or inferred).lower()
    if chosen_preset == "regen":
        chosen_preset = "regenerative"
    if chosen_preset not in ("aerospike", "regenerative", "swirler"):
        chosen_preset = inferred

    raw_voxel = float(geo_in.get("voxel_mm", 1.5))
    min_vox = 1.5 if thrust_kn >= 40 else 1.2
    voxel_mm = min(2.0, max(min_vox, raw_voxel if raw_voxel >= min_vox else min_vox))

    channel_count = int(
        specs_in.get(
            "CoolingChannelCount",
            geo_in.get("channel_count", 48),
        )
    )
    channel_count = min(64, max(32, channel_count))
    wall_mm = float(specs_in.get("WallThicknessMm", geo_in.get("wall_mm", 3.5)))
    wall_mm = max(3.0, wall_mm)
    helix = float(specs_in.get("HelixTurns", geo_in.get("helix_turns", 2.6)))
    depth = float(specs_in.get("ChannelDepthMm", 2.5))
    pipe_d = float(specs_in.get("PipeDiameterMm", 18.0))

    engine_specs = {
        "ThrustkN": float(specs_in.get("ThrustkN", thrust_kn)),
        "ChamberPressureBar": float(
            specs_in.get("ChamberPressureBar", chamber_pressure_mpa * 10.0)
        ),
        "CoolingChannelCount": channel_count,
        "WallThicknessMm": wall_mm,
        "ChannelDepthMm": depth,
        "HelixTurns": helix,
        "PipeDiameterMm": pipe_d,
        "AmbientPressurePa": ambient_pressure_pa,
        "Gamma": float(sized.get("gamma", 1.20)),
        "VoxelMm": voxel_mm,
    }

    geometry = {
        "throat_radius_mm": float(geo_in.get("throat_radius_mm", sized["throat_radius_mm"])),
        "exit_radius_mm": float(geo_in.get("exit_radius_mm", sized["exit_radius_mm"])),
        "length_mm": float(geo_in.get("length_mm", sized["length_mm"])),
        "channel_count": channel_count,
        "channel_radius_mm": float(geo_in.get("channel_radius_mm", depth * 0.5)),
        "wall_mm": wall_mm,
        "helix_turns": helix,
        "voxel_mm": voxel_mm,
    }

    csharp = _display_csharp(engine_specs)

    if not reasoning or reasoning.startswith("NIM unavailable"):
        pass
    elif "AeroForge.Core" not in reasoning and "GasDynamics" not in reasoning:
        reasoning = (
            "Sized via AeroForge.Core GasDynamics + RaoContour; assembled by "
            "ChamberBuilder → CoolingBuilder → ManifoldBuilder → InjectorBuilder. "
            + reasoning
        )

    return {
        "reasoning": reasoning,
        "model": model_used,
        "thermo": sized,
        "preset": chosen_preset,
        "geometry": geometry,
        "engine_specs": engine_specs,
        "picogk_csharp": csharp,
    }


def _display_csharp(specs: dict[str, Any]) -> str:
    """UI-facing snippet showing the deterministic Core call (not executed via Roslyn)."""
    return f"""// AeroForge.Core — deterministic CEM (no LLM geometry)
using AeroForge.Core;
using AeroForge.Core.Physics;
using AeroForge.Core.Modules;

var specs = new EngineSpecs {{
    ThrustkN = {float(specs['ThrustkN']):0.2f}f,
    ChamberPressureBar = {float(specs['ChamberPressureBar']):0.1f}f,
    CoolingChannelCount = {int(specs['CoolingChannelCount'])},
    WallThicknessMm = {float(specs['WallThicknessMm']):0.1f}f,
    ChannelDepthMm = {float(specs['ChannelDepthMm']):0.1f}f,
    HelixTurns = {float(specs['HelixTurns']):0.1f}f,
    PipeDiameterMm = {float(specs['PipeDiameterMm']):0.1f}f
}};

// 1. GasDynamics.Calculate → A_t, r_t, ε, L*
// 2. RaoContour.GetEngineProfile → parabolic bell
// 3. ChamberBuilder.BuildHollowWall → hollow jacket
// 4. CoolingBuilder.BuildConformalChannels → BoolSubtract (48 helical ribs)
// 5. ManifoldBuilder.BuildRings → BoolAdd (toroids + pipe stubs)
// 6. InjectorBuilder.BuildFlange → BoolAdd
return AeroForgeEngine.BuildMesh(specs);
""".strip()

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


SYSTEM = """You are a Creative Propulsion Engineer for AeroForge Studio.
You design novel 3D-printable rocket/jet hardware using LEAP 71 PicoGK voxel fields.

Rules:
1) Never propose plain cylinders or flat cones as the finished part.
2) Prefer organic conformal cooling channels, gyroid/TPMS lattices, and biomimetic manifolds.
3) Use isentropic relations: A_t = F / (P_c * C_f); expansion ratio from P_c, P_a, gamma.
4) Respond with ONE JSON object only (no markdown fences) with keys:
{
  "reasoning": "short engineering narrative",
  "preset": "aerospike" | "regenerative" | "swirler",
  "geometry": {
    "throat_radius_mm": number,
    "exit_radius_mm": number,
    "length_mm": number,
    "channel_count": int,
    "channel_radius_mm": number,
    "wall_mm": number,
    "helix_turns": number,
    "voxel_mm": number
  },
  "picogk_csharp": "C# statements for Mesh Build(Library lib) { ... } body that returns a Mesh. May call AeroForge.Geometry.EngineBuilders.* or use PicoGK Voxels/Lattice/IBoundedImplicit. Must end with return <Mesh>;"
}
"""


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

    user = {
        "prompt": prompt,
        "constraints": {
            "thrust_kn": thrust_kn,
            "chamber_pressure_mpa": chamber_pressure_mpa,
            "propellant": propellant,
            "preset_hint": preset,
            "thermo": sized,
        },
    }

    reasoning = ""
    ai: dict[str, Any] = {}
    model_used = _model()
    try:
        client = _client()
        stream = client.chat.completions.create(
            model=model_used,
            temperature=0.35,
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
    except Exception as exc:  # noqa: BLE001 — fall back to deterministic creative defaults
        reasoning = (
            f"NIM call unavailable ({exc.__class__.__name__}: {exc}). "
            "Falling back to deterministic creative geometry sized from isentropic thermo."
        )
        ai = {
            "preset": preset or "aerospike",
            "geometry": {},
            "picogk_csharp": _default_csharp(preset or "aerospike"),
        }

    geo_in = ai.get("geometry") if isinstance(ai.get("geometry"), dict) else {}
    chosen_preset = str(ai.get("preset") or preset or "aerospike").lower()
    geometry = {
        "throat_radius_mm": float(geo_in.get("throat_radius_mm", sized["throat_radius_mm"])),
        "exit_radius_mm": float(geo_in.get("exit_radius_mm", sized["exit_radius_mm"])),
        "length_mm": float(geo_in.get("length_mm", sized["length_mm"])),
        "channel_count": int(geo_in.get("channel_count", 16)),
        "channel_radius_mm": float(geo_in.get("channel_radius_mm", 1.35)),
        "wall_mm": float(geo_in.get("wall_mm", 2.0)),
        "helix_turns": float(geo_in.get("helix_turns", 2.2)),
        "voxel_mm": float(geo_in.get("voxel_mm", 1.2)),
    }
    csharp = str(ai.get("picogk_csharp") or _default_csharp(chosen_preset))

    return {
        "reasoning": reasoning,
        "model": model_used,
        "thermo": sized,
        "preset": chosen_preset,
        "geometry": geometry,
        "picogk_csharp": csharp,
    }


def _default_csharp(preset: str) -> str:
    builder = {
        "swirler": "BuildSwirler",
        "regenerative": "BuildRegenBell",
        "regen": "BuildRegenBell",
    }.get(preset, "BuildAerospike")
    return f"""
var spec = new EngineSpec {{
    Preset = "{preset}",
    ThroatRadiusMm = 38f,
    ExitRadiusMm = 95f,
    LengthMm = 170f,
    ChannelCount = 16,
    ChannelRadiusMm = 1.35f,
    WallMm = 2.0f,
    HelixTurns = 2.2f,
    ExpansionRatio = 8f
}};
return EngineBuilders.{builder}(lib, spec);
""".strip()

"""Single entry point used by the dashboard, the exporter, and the tests."""

from __future__ import annotations

import math

import numpy as np
from scipy.integrate import trapezoid

from engine_math.contour import build_wall
from engine_math.nozzle import calculate_nozzle_parameters
from engine_math.presets import MATERIALS, PROPELLANTS
from engine_math.thermal import analyze_thermal, size_channel_diameter


def reference_inputs(**overrides) -> dict:
    """Sea-level methalox point used by the dashboard and the tests."""
    gas = PROPELLANTS["lox_methane"]
    base = {
        "thrust_n": 25_000.0,
        "chamber_pressure_pa": 3.5e6,
        "ambient_pressure_pa": 101_325.0,
        "gamma": gas["gamma"],
        "chamber_temp_k": gas["chamber_temp_k"],
        "molar_mass": gas["molar_mass"],
        "of_ratio": gas["of_ratio"],
        "radius_ratio": 2.5,
        "lstar_m": 1.0,
        "exit_half_angle_deg": 10.0,
        "bell_fraction": 0.80,
        "design_expansion_ratio": None,
        "bulkhead_m": 0.010,
        "channel_count": 24,
        "helix_turns": 0.0,
        "hot_wall_m": 0.0007,
        "coolant_velocity_m_s": 38.0,
        "channel_diameter_m": None,
        "closeout_m": None,
        "coolant_rho": gas["coolant_rho"],
        "coolant_cp": gas["coolant_cp"],
        "coolant_k": gas["coolant_k"],
        "coolant_mu": gas["coolant_mu"],
        "coolant_pr": gas["coolant_pr"],
        "coolant_inlet_k": gas["coolant_inlet_k"],
        "material_key": "grcop42",
    }
    # closeout_m None means "derive inside evaluate"
    cleaned = {k: v for k, v in base.items() if v is not None}
    cleaned.update({k: v for k, v in overrides.items() if v is not None or k == "design_expansion_ratio"})
    if overrides.get("closeout_m") is None and "closeout_m" in overrides:
        cleaned.pop("closeout_m", None)
    return cleaned


def evaluate(inputs: dict) -> dict:
    """Run thermo, contour, and cooling. Returns numpy-bearing design state."""
    nozzle = calculate_nozzle_parameters(
        thrust_n=float(inputs["thrust_n"]),
        chamber_pressure_pa=float(inputs["chamber_pressure_pa"]),
        ambient_pressure_pa=float(inputs["ambient_pressure_pa"]),
        gamma=float(inputs["gamma"]),
        chamber_temp_k=float(inputs["chamber_temp_k"]),
        molar_mass=float(inputs["molar_mass"]),
        radius_ratio=float(inputs["radius_ratio"]),
        lstar_m=float(inputs["lstar_m"]),
        exit_half_angle_deg=float(inputs["exit_half_angle_deg"]),
        bell_fraction=float(inputs["bell_fraction"]),
        design_expansion_ratio=inputs.get("design_expansion_ratio"),
        bulkhead_m=float(inputs.get("bulkhead_m", 0.010)),
    )
    gas = nozzle["contour"]
    material = MATERIALS[inputs.get("material_key", "grcop42")]

    fuel_fraction = 1.0 / (1.0 + max(float(inputs["of_ratio"]), 0.3))
    mdot_cool = nozzle["mass_flow_kg_s"] * fuel_fraction
    diameter, dia_warnings = size_channel_diameter(
        throat_radius_m=nozzle["throat_radius_m"],
        hot_wall_m=float(inputs["hot_wall_m"]),
        channel_count=int(inputs["channel_count"]),
        coolant_mass_flow=mdot_cool,
        coolant_rho=float(inputs["coolant_rho"]),
        requested_diameter_m=inputs.get("channel_diameter_m"),
        target_velocity=float(inputs.get("coolant_velocity_m_s", 38.0)),
    )
    closeout = float(inputs.get("closeout_m", max(0.0016, 0.40 * diameter)))
    span = max(gas.exit_z_m - gas.gas_face_z_m - 0.010, 1e-3)
    turns = abs(float(inputs.get("helix_turns", 0.0)))
    helix_note: list[str] = []
    if turns > 0.0 and span / turns < 1.8 * diameter:
        turns = span / (1.8 * diameter)
        helix_note.append(
            f"Helix was relaxed to {turns:.2f} turns so adjacent passages do not intersect."
        )

    wall = build_wall(
        gas,
        hot_wall_m=float(inputs["hot_wall_m"]),
        channel_diameter_m=diameter,
        closeout_m=closeout,
        channel_count=int(inputs["channel_count"]),
    )
    thermal = analyze_thermal(
        gas=gas,
        wall=wall,
        gamma=nozzle["gamma"],
        chamber_pressure=nozzle["chamber_pressure_pa"],
        chamber_temp=nozzle["chamber_temp_k"],
        c_star=nozzle["c_star_m_s"],
        throat_radius_m=nozzle["throat_radius_m"],
        mass_flow=nozzle["mass_flow_kg_s"],
        of_ratio=float(inputs["of_ratio"]),
        coolant_rho=float(inputs["coolant_rho"]),
        coolant_cp=float(inputs["coolant_cp"]),
        coolant_k=float(inputs["coolant_k"]),
        coolant_mu=float(inputs["coolant_mu"]),
        coolant_pr=float(inputs["coolant_pr"]),
        coolant_inlet_k=float(inputs["coolant_inlet_k"]),
        material_k=float(material["k"]),
        material_t_max=float(material["t_max_k"]),
        channel_count=int(inputs["channel_count"]),
        helix_turns=turns,
    )

    warnings = helix_note + list(dia_warnings) + list(thermal.warnings)
    if nozzle["lstar_actual_m"] > float(inputs["lstar_m"]) * 1.15:
        warnings.append(
            "The convergent section already holds more volume than the requested L*. "
            "Chamber length was kept at a short minimum cylinder."
        )
    if nozzle["expansion_limited"] and float(inputs["ambient_pressure_pa"]) > 1000:
        warnings.append(
            "Expansion ratio was capped. The nozzle is underexpanded at this ambient pressure."
        )

    # Analytical metal volume: jacket annulus minus circular passages.
    z = wall.z_m
    vol_wall = float(
        trapezoid(math.pi * (wall.r_jacket_outer_m**2 - wall.r_gas_m**2), z)
    )
    r_bulk = float(wall.r_jacket_outer_m[0])
    vol_bulk = math.pi * r_bulk**2 * gas.bulkhead_m
    # Passage volume along the contour, one helix-aware path.
    arc = np.sqrt(np.diff(z) ** 2 + np.diff(wall.r_channel_m) ** 2)
    arc_len = float(np.sum(arc))
    mean_r = float(np.mean(wall.r_channel_m))
    path = math.sqrt(arc_len**2 + (turns * 2.0 * math.pi * mean_r) ** 2) if turns else arc_len
    vol_channels = int(inputs["channel_count"]) * math.pi * diameter**2 / 4.0 * path
    vol_metal = max(vol_wall + vol_bulk - vol_channels, 0.0)
    mass = vol_metal * float(material["density"])

    nozzle_public = {k: v for k, v in nozzle.items() if k != "contour"}
    return {
        "nozzle": nozzle_public,
        "gas": gas,
        "wall": wall,
        "thermal": thermal,
        "warnings": warnings,
        "material": material,
        "volume_m3": vol_metal,
        "mass_kg": mass,
        "helix_turns": turns,
        "inputs": dict(inputs),
    }

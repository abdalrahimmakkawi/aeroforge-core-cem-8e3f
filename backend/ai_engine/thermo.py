"""Isentropic nozzle sizing for AeroForge Studio."""

from __future__ import annotations

import math
from typing import Any

G0 = 9.80665
R_UNIV = 8314.462618

PROPELLANTS: dict[str, dict[str, Any]] = {
    "lox_methane": {
        "label": "LOX / LCH4 (MethaLOX)",
        "gamma": 1.20,
        "chamber_temp_k": 3550.0,
        "molar_mass": 21.0,
        "of_ratio": 3.4,
    },
    "lox_rp1": {
        "label": "LOX / RP-1",
        "gamma": 1.22,
        "chamber_temp_k": 3670.0,
        "molar_mass": 23.5,
        "of_ratio": 2.3,
    },
    "lox_lh2": {
        "label": "LOX / LH2",
        "gamma": 1.26,
        "chamber_temp_k": 3400.0,
        "molar_mass": 14.0,
        "of_ratio": 5.5,
    },
}


def characteristic_velocity(gamma: float, tc: float, molar_mass: float) -> float:
    r = R_UNIV / molar_mass
    exp = (gamma + 1.0) / (2.0 * (gamma - 1.0))
    return math.sqrt(r * tc / gamma) * ((gamma + 1.0) / 2.0) ** exp


def thrust_coefficient(gamma: float, pe_pc: float, pa_pc: float, epsilon: float) -> float:
    pe_pc = min(max(pe_pc, 1e-12), 0.999999)
    mom = (
        (2.0 * gamma * gamma / (gamma - 1.0))
        * (2.0 / (gamma + 1.0)) ** ((gamma + 1.0) / (gamma - 1.0))
        * (1.0 - pe_pc ** ((gamma - 1.0) / gamma))
    )
    return math.sqrt(max(mom, 0.0)) + (pe_pc - pa_pc) * epsilon


def expansion_ratio_from_pressure(gamma: float, pc: float, pa: float) -> tuple[float, float]:
    """Return (epsilon, exit_mach) for adapted nozzle Pe≈Pa."""
    if pa <= 1.0:
        # Vacuum design default
        eps = 25.0
        me = mach_from_epsilon(gamma, eps)
        return eps, me
    pe = pa
    ratio = (pc / pe) ** ((gamma - 1.0) / gamma)
    me = math.sqrt(max(2.0 / (gamma - 1.0) * (ratio - 1.0), 0.0))
    me = max(me, 1.05)
    eps = area_ratio(gamma, me)
    return min(eps, 80.0), me


def area_ratio(gamma: float, mach: float) -> float:
    mach = max(mach, 1e-6)
    core = (1.0 + 0.5 * (gamma - 1.0) * mach * mach) / ((gamma + 1.0) / 2.0)
    return (1.0 / mach) * core ** ((gamma + 1.0) / (2.0 * (gamma - 1.0)))


def mach_from_epsilon(gamma: float, epsilon: float) -> float:
    lo, hi = 1.0001, 2.0
    for _ in range(40):
        if area_ratio(gamma, hi) >= epsilon:
            break
        hi *= 1.4
        if hi > 50:
            return 50.0
    for _ in range(60):
        mid = 0.5 * (lo + hi)
        if area_ratio(gamma, mid) > epsilon:
            hi = mid
        else:
            lo = mid
    return 0.5 * (lo + hi)


def size_engine(
    *,
    thrust_n: float,
    chamber_pressure_pa: float,
    ambient_pressure_pa: float = 101325.0,
    propellant: str = "lox_methane",
    gamma: float | None = None,
    chamber_temp_k: float | None = None,
    molar_mass: float | None = None,
) -> dict[str, Any]:
    gas = PROPELLANTS.get(propellant, PROPELLANTS["lox_methane"])
    g = float(gamma if gamma is not None else gas["gamma"])
    tc = float(chamber_temp_k if chamber_temp_k is not None else gas["chamber_temp_k"])
    mm = float(molar_mass if molar_mass is not None else gas["molar_mass"])

    c_star = characteristic_velocity(g, tc, mm)
    epsilon, me = expansion_ratio_from_pressure(g, chamber_pressure_pa, ambient_pressure_pa)
    pe = ambient_pressure_pa if ambient_pressure_pa > 1.0 else chamber_pressure_pa * (1.0 + 0.5 * (g - 1) * me * me) ** (-g / (g - 1))
    cf = thrust_coefficient(
        g,
        pe / chamber_pressure_pa,
        ambient_pressure_pa / chamber_pressure_pa,
        epsilon,
    )
    # A_t = F / (P_c * C_f)
    at = thrust_n / (chamber_pressure_pa * max(cf, 0.05))
    rt = math.sqrt(at / math.pi)
    re = rt * math.sqrt(epsilon)
    mdot = chamber_pressure_pa * at / c_star
    isp = thrust_n / (mdot * G0)
    rt_mm = rt * 1000.0
    re_mm = re * 1000.0
    rc_mm = rt_mm * 2.35
    thrust_kn = thrust_n / 1000.0
    # Match AeroForge.Core GasDynamics length so live UI ≈ forged mesh.
    l_star = 700.0
    vc = l_star * (at * 1e9)
    l_conv = (rc_mm - rt_mm) / math.tan(math.radians(30.0))
    l_bell = 0.80 * (re_mm - rt_mm) / math.tan(math.radians(15.0))
    l_cyl_raw = vc / (math.pi * rc_mm * rc_mm) - 0.5 * l_conv
    l_cyl = min(max(l_cyl_raw, rt_mm * 1.6), rt_mm * 4.2)
    max_len = min(max(150.0 + thrust_kn * 3.4, 160.0), 420.0)
    min_len = min(max(110.0 + thrust_kn * 1.2, 120.0), 220.0)
    length_mm = min(max(l_cyl + l_conv + l_bell, min_len), max_len)
    return {
        "propellant": propellant,
        "propellant_label": gas["label"],
        "gamma": g,
        "chamber_temp_k": tc,
        "molar_mass": mm,
        "of_ratio": gas["of_ratio"],
        "c_star_m_s": c_star,
        "cf": cf,
        "expansion_ratio": epsilon,
        "exit_mach": me,
        "throat_area_m2": at,
        "throat_radius_mm": rt_mm,
        "exit_radius_mm": re_mm,
        "chamber_radius_mm": rc_mm,
        "mass_flow_kg_s": mdot,
        "isp_s": isp,
        "thrust_n": thrust_n,
        "chamber_pressure_pa": chamber_pressure_pa,
        "ambient_pressure_pa": ambient_pressure_pa,
        "exit_pressure_pa": pe,
        "length_mm": length_mm,
    }

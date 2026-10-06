"""Isentropic nozzle sizing.

Throat area is chosen so the delivered thrust equals the request at the
selected ambient pressure:

    F = Cf * Pc * At
    Cf = Cf_momentum(γ, Pe/Pc) + (Pe - Pa) / Pc * ε

Expansion ratio comes from the supersonic area–Mach relation, not from the
abbreviated pressure-ratio expression in the original sketch (that form drops
a γ factor and mis-sizes ε). Characteristic velocity is the ideal-gas frozen
value from chamber temperature, γ, and molar mass.
"""

from __future__ import annotations

import math

from scipy.optimize import brentq

from engine_math.contour import build_gas_contour
from engine_math.presets import G0, R_UNIV


def characteristic_velocity(gamma: float, chamber_temp_k: float, molar_mass: float) -> float:
    """Ideal-gas c* in m/s."""
    r_specific = R_UNIV / molar_mass
    exponent = (gamma + 1.0) / (2.0 * (gamma - 1.0))
    return math.sqrt(r_specific * chamber_temp_k / gamma) * ((gamma + 1.0) / 2.0) ** exponent


def exit_mach_from_pressure(gamma: float, chamber_pressure: float, exit_pressure: float) -> float:
    ratio = (chamber_pressure / exit_pressure) ** ((gamma - 1.0) / gamma)
    return math.sqrt(max(2.0 / (gamma - 1.0) * (ratio - 1.0), 0.0))


def pressure_ratio_from_mach(gamma: float, mach: float) -> float:
    """pe / pc."""
    return (1.0 + 0.5 * (gamma - 1.0) * mach * mach) ** (-gamma / (gamma - 1.0))


def expansion_ratio_from_mach(gamma: float, mach: float) -> float:
    """Ae / At for isentropic flow. Valid on both branches; singular at M = 0."""
    mach = max(mach, 1e-6)
    core = (1.0 + 0.5 * (gamma - 1.0) * mach * mach) / ((gamma + 1.0) / 2.0)
    return (1.0 / mach) * core ** ((gamma + 1.0) / (2.0 * (gamma - 1.0)))


def mach_from_expansion_ratio(gamma: float, epsilon: float, *, supersonic: bool) -> float:
    """Invert the area–Mach relation. ε >= 1."""
    epsilon = max(float(epsilon), 1.0)
    if epsilon < 1.0000001:
        return 1.0

    def residual(mach: float) -> float:
        return expansion_ratio_from_mach(gamma, mach) - epsilon

    if supersonic:
        lo = 1.0000001
        hi = 2.0
        for _ in range(24):
            if residual(hi) >= 0.0:
                break
            if hi >= 50.0:
                return 50.0
            hi *= 1.45
        return float(brentq(residual, lo, hi))
    return float(brentq(residual, 1e-5, 0.999999))


def thrust_coefficient(
    gamma: float,
    chamber_pressure: float,
    exit_pressure: float,
    ambient_pressure: float,
    epsilon: float,
) -> float:
    pr = min(max(exit_pressure / chamber_pressure, 1e-12), 0.999999)
    momentum = (
        (2.0 * gamma * gamma / (gamma - 1.0))
        * (2.0 / (gamma + 1.0)) ** ((gamma + 1.0) / (gamma - 1.0))
        * (1.0 - pr ** ((gamma - 1.0) / gamma))
    )
    return math.sqrt(max(momentum, 0.0)) + (exit_pressure - ambient_pressure) / chamber_pressure * epsilon


def calculate_nozzle_parameters(
    thrust_n: float,
    chamber_pressure_pa: float,
    ambient_pressure_pa: float,
    gamma: float = 1.2,
    chamber_temp_k: float = 3550.0,
    molar_mass: float = 21.0,
    radius_ratio: float = 2.5,
    lstar_m: float = 1.0,
    exit_half_angle_deg: float = 10.0,
    bell_fraction: float = 0.80,
    design_expansion_ratio: float | None = None,
    bulkhead_m: float = 0.010,
) -> dict:
    """Size throat, exit, and chamber from thrust and chamber pressure.

    If `design_expansion_ratio` is set, or ambient pressure is ~0, the nozzle
    is sized to that area ratio and may be underexpanded relative to ambient.
    Otherwise the nozzle is perfectly expanded (Pe = Pa).
    """
    if thrust_n <= 0 or chamber_pressure_pa <= 0:
        raise ValueError("Thrust and chamber pressure must be positive.")
    if not 1.05 < gamma < 1.67:
        raise ValueError("Gamma must be between 1.05 and 1.67.")
    if chamber_temp_k <= 0 or molar_mass <= 0:
        raise ValueError("Chamber temperature and molar mass must be positive.")
    if radius_ratio < 1.2:
        raise ValueError("Chamber-to-throat radius ratio must be at least 1.2.")

    c_star = characteristic_velocity(gamma, chamber_temp_k, molar_mass)
    limited = False
    vacuum = ambient_pressure_pa <= 1.0

    if design_expansion_ratio is not None or vacuum:
        epsilon = float(design_expansion_ratio if design_expansion_ratio is not None else 25.0)
        epsilon = min(max(epsilon, 1.05), 80.0)
        me = mach_from_expansion_ratio(gamma, epsilon, supersonic=True)
        pe = chamber_pressure_pa * pressure_ratio_from_mach(gamma, me)
        if vacuum or pe > ambient_pressure_pa * 1.02:
            limited = True
    else:
        if ambient_pressure_pa >= chamber_pressure_pa * 0.9:
            raise ValueError("Ambient pressure must be well below chamber pressure.")
        pe = ambient_pressure_pa
        me = exit_mach_from_pressure(gamma, chamber_pressure_pa, pe)
        if me < 1.05:
            raise ValueError("Pressure ratio is too small for a supersonic nozzle.")
        epsilon = expansion_ratio_from_mach(gamma, me)
        if epsilon > 80.0:
            epsilon = 80.0
            me = mach_from_expansion_ratio(gamma, epsilon, supersonic=True)
            pe = chamber_pressure_pa * pressure_ratio_from_mach(gamma, me)
            limited = True

    cf = thrust_coefficient(gamma, chamber_pressure_pa, pe, ambient_pressure_pa, epsilon)
    if cf <= 0.05:
        raise ValueError("Thrust coefficient is non-physical for these pressures.")

    throat_area = thrust_n / (chamber_pressure_pa * cf)
    throat_radius = math.sqrt(throat_area / math.pi)
    exit_radius = throat_radius * math.sqrt(epsilon)
    chamber_radius = throat_radius * radius_ratio

    contour = build_gas_contour(
        throat_radius,
        chamber_radius,
        exit_radius,
        lstar_m,
        exit_half_angle_deg=exit_half_angle_deg,
        bell_fraction=bell_fraction,
        bulkhead_m=bulkhead_m,
    )

    mass_flow = chamber_pressure_pa * throat_area / c_star
    isp = thrust_n / (mass_flow * G0)

    # Sea-level thrust of this same geometry, for the spec panel.
    cf_sl = thrust_coefficient(gamma, chamber_pressure_pa, pe, 101_325.0, epsilon)
    thrust_sl = cf_sl * chamber_pressure_pa * throat_area
    isp_sl = thrust_sl / (mass_flow * G0)

    p_throat = chamber_pressure_pa * (2.0 / (gamma + 1.0)) ** (gamma / (gamma - 1.0))
    t_throat = chamber_temp_k * 2.0 / (gamma + 1.0)

    adapted = (not limited) and abs(pe - ambient_pressure_pa) <= max(500.0, 0.01 * pe)

    return {
        "throat_radius_mm": throat_radius * 1000.0,
        "exit_radius_mm": exit_radius * 1000.0,
        "chamber_radius_mm": chamber_radius * 1000.0,
        "chamber_length_mm": contour.length_chamber_m * 1000.0,
        "nozzle_length_mm": contour.length_nozzle_m * 1000.0,
        "expansion_ratio": epsilon,
        "throat_radius_m": throat_radius,
        "exit_radius_m": exit_radius,
        "chamber_radius_m": chamber_radius,
        "throat_area_m2": throat_area,
        "c_star_m_s": c_star,
        "cf": cf,
        "cf_sea_level": cf_sl,
        "isp_s": isp,
        "isp_sea_level_s": isp_sl,
        "thrust_sea_level_n": thrust_sl,
        "mass_flow_kg_s": mass_flow,
        "exit_mach": me,
        "exit_pressure_pa": pe,
        "ambient_pressure_pa": ambient_pressure_pa,
        "chamber_pressure_pa": chamber_pressure_pa,
        "thrust_n": thrust_n,
        "gamma": gamma,
        "chamber_temp_k": chamber_temp_k,
        "molar_mass": molar_mass,
        "throat_pressure_pa": p_throat,
        "throat_temperature_k": t_throat,
        "adapted": adapted,
        "expansion_limited": limited,
        "lstar_requested_m": lstar_m,
        "lstar_actual_m": contour.lstar_actual_m,
        "bell_fraction_actual": contour.bell_fraction_actual,
        "entrance_angle_deg": contour.entrance_angle_deg,
        "contour": contour,
    }

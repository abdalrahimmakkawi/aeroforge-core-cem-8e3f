"""Bartz hot-gas heat flux and a single-pass regenerative coolant march.

The hot-gas coefficient is the SI transcription of Bartz (1967): gas
properties are taken at chamber stagnation conditions, and the σ correction
carries wall temperature and local Mach number. Coolant-side convection is
Dittus–Boelter for a circular passage. This is a preliminary design estimate,
not a conjugate CFD result.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np

from engine_math.contour import GasContour, WallContours
from engine_math.nozzle import mach_from_expansion_ratio


@dataclass
class ThermalField:
    z_m: np.ndarray
    mach: np.ndarray
    area_ratio: np.ndarray
    heat_flux_w_m2: np.ndarray
    h_gas: np.ndarray
    wall_temp_k: np.ndarray
    coolant_temp_k: np.ndarray
    recovery_temp_k: np.ndarray
    coolant_velocity_m_s: float
    coolant_mass_flow_kg_s: float
    coolant_delta_t_k: float
    coolant_delta_p_pa: float
    reynolds: float
    h_coolant: float
    peak_heat_flux_w_m2: float
    peak_wall_temp_k: float
    peak_wall_z_m: float
    channel_diameter_m: float
    warnings: list[str]


def size_channel_diameter(
    *,
    throat_radius_m: float,
    hot_wall_m: float,
    channel_count: int,
    coolant_mass_flow: float,
    coolant_rho: float,
    requested_diameter_m: float | None,
    target_velocity: float = 22.0,
    min_land_m: float = 0.0014,
) -> tuple[float, list[str]]:
    """Pick a circular passage diameter that fits the throat circumference."""
    warnings: list[str] = []
    r_pitch = throat_radius_m + hot_wall_m
    max_d = 2.0 * math.pi * r_pitch / channel_count - min_land_m
    max_d = max(max_d, 0.0012)

    area_one = coolant_mass_flow / (channel_count * coolant_rho * target_velocity)
    d_velocity = math.sqrt(max(4.0 * area_one / math.pi, 1e-12))

    if requested_diameter_m is None:
        diameter = min(d_velocity, max_d)
        diameter = min(max(diameter, 0.0015), 0.009)
        if d_velocity > max_d * 1.05:
            warnings.append(
                "Channel diameter was reduced so the lands at the throat stay above 1.4 mm. "
                f"Coolant velocity will run above the {target_velocity:.0f} m/s sizing target."
            )
    else:
        diameter = min(max(float(requested_diameter_m), 0.0012), 0.012)
        if diameter > max_d:
            warnings.append(
                f"Requested passages are wider than the throat can host with {channel_count} channels. "
                "Lands will be thin; reduce diameter or add channels before printing."
            )
            diameter = max_d
    return diameter, warnings


def _local_mach(area_ratio: np.ndarray, gamma: float, throat_z: float, z: np.ndarray) -> np.ndarray:
    mach = np.empty_like(area_ratio)
    for i, eps in enumerate(area_ratio):
        eps = float(max(eps, 1.0))
        supersonic = z[i] >= throat_z - 1e-6
        if eps < 1.0005:
            mach[i] = 1.0
            continue
        mach[i] = mach_from_expansion_ratio(gamma, eps, supersonic=supersonic)
    return mach


def _bartz_heat_flux(
    *,
    mach: np.ndarray,
    area_ratio: np.ndarray,
    gamma: float,
    chamber_pressure: float,
    chamber_temp: float,
    c_star: float,
    throat_diameter: float,
    throat_curvature: float,
    mu: float,
    cp: float,
    pr: float,
    wall_temp: np.ndarray,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    recovery = pr**0.33
    taw = chamber_temp * (1.0 + recovery * 0.5 * (gamma - 1.0) * mach**2) / (
        1.0 + 0.5 * (gamma - 1.0) * mach**2
    )
    sigma = (0.5 * wall_temp / chamber_temp * (1.0 + 0.5 * (gamma - 1.0) * mach**2) + 0.5) ** (
        -0.68
    ) * (1.0 + 0.5 * (gamma - 1.0) * mach**2) ** (-0.12)

    property_group = (mu**0.2) * cp / (pr**0.6)
    scale = (
        (0.026 / (throat_diameter**0.2))
        * property_group
        * (chamber_pressure / c_star) ** 0.8
        * (throat_diameter / throat_curvature) ** 0.1
    )
    h_gas = scale * (1.0 / np.maximum(area_ratio, 1.0)) ** 0.9 * sigma
    q = h_gas * np.maximum(taw - wall_temp, 0.0)
    return q, h_gas, taw


def analyze_thermal(
    *,
    gas: GasContour,
    wall: WallContours,
    gamma: float,
    chamber_pressure: float,
    chamber_temp: float,
    c_star: float,
    throat_radius_m: float,
    mass_flow: float,
    of_ratio: float,
    coolant_rho: float,
    coolant_cp: float,
    coolant_k: float,
    coolant_mu: float,
    coolant_pr: float,
    coolant_inlet_k: float,
    material_k: float,
    material_t_max: float,
    channel_count: int,
    helix_turns: float,
) -> ThermalField:
    z = gas.z_m
    r = gas.r_m
    area_ratio = (r / throat_radius_m) ** 2
    mach = _local_mach(area_ratio, gamma, gas.throat_z_m, z)

    # Recover the gas constant from c* so cp stays consistent with the nozzle sizing.
    # c* = sqrt(R Tc / γ) * ((γ+1)/2)^((γ+1)/(2(γ-1)))
    exponent = (gamma + 1.0) / (2.0 * (gamma - 1.0))
    r_specific = (c_star / (((gamma + 1.0) / 2.0) ** exponent)) ** 2 * gamma / chamber_temp
    cp_gas = gamma / (gamma - 1.0) * r_specific
    mu_gas = 8.0e-5 * (chamber_temp / 3500.0) ** 0.65
    pr_gas = 0.82

    fuel_fraction = 1.0 / (1.0 + max(of_ratio, 0.3))
    mdot_cool = mass_flow * fuel_fraction
    d = wall.channel_diameter_m
    area_ch = math.pi * d * d / 4.0
    velocity = mdot_cool / (channel_count * coolant_rho * area_ch)
    re = coolant_rho * velocity * d / coolant_mu
    # Turbulent heating correlation. Floor Re so a bad input still returns a number.
    nu = 0.023 * max(re, 3000.0) ** 0.8 * coolant_pr**0.4
    h_cool = nu * coolant_k / d

    # Slant-height surface element on the gas side.
    ds = np.sqrt(np.diff(z) ** 2 + np.diff(r) ** 2)
    r_mid = 0.5 * (r[:-1] + r[1:])

    throat_curvature = 0.382 * throat_radius_m
    dt = 2.0 * throat_radius_m
    tw = np.full_like(z, coolant_inlet_k + 450.0)

    q = np.zeros_like(z)
    h_gas = np.zeros_like(z)
    taw = np.zeros_like(z)
    t_cool = np.full_like(z, coolant_inlet_k)

    for _ in range(5):
        q, h_gas, taw = _bartz_heat_flux(
            mach=mach,
            area_ratio=area_ratio,
            gamma=gamma,
            chamber_pressure=chamber_pressure,
            chamber_temp=chamber_temp,
            c_star=c_star,
            throat_diameter=dt,
            throat_curvature=max(throat_curvature, 1e-4),
            mu=mu_gas,
            cp=cp_gas,
            pr=pr_gas,
            wall_temp=np.minimum(tw, taw - 30.0),
        )
        # Integrate coolant from the exit manifold (inlet) toward the injector.
        t_cool = np.empty_like(z)
        t_cool[-1] = coolant_inlet_k
        for i in range(len(z) - 2, -1, -1):
            q_seg = 0.5 * (q[i] + q[i + 1])
            d_area = 2.0 * math.pi * r_mid[i] * ds[i]
            t_cool[i] = t_cool[i + 1] + q_seg * d_area / (mdot_cool * coolant_cp)
        # Hot-wall resistance uses the normal thickness, which equals the
        # radial thickness at the throat where cos(ψ) = 1, and the local
        # value elsewhere.
        t_normal = wall.hot_wall_m
        resistance = t_normal / max(material_k, 1.0) + 1.0 / max(h_cool, 1.0)
        tw = t_cool + q * resistance
        tw = np.clip(tw, coolant_inlet_k + 5.0, chamber_temp * 0.98)

    # Path length of one passage, including helix.
    arc = float(np.sum(np.sqrt(np.diff(z) ** 2 + np.diff(wall.r_channel_m) ** 2)))
    # Restrict arc to the cooled span roughly by the full contour; helix adds r dθ.
    mean_r = float(np.mean(wall.r_channel_m))
    helix_extra = abs(helix_turns) * 2.0 * math.pi * mean_r
    path_length = math.sqrt(arc**2 + helix_extra**2) if helix_turns else arc
    friction = 0.04  # rough printed passage, order-of-magnitude
    delta_p = friction * (path_length / d) * 0.5 * coolant_rho * velocity**2

    i_peak = int(np.argmax(q))
    warnings: list[str] = []
    peak_tw = float(tw.max())
    if peak_tw > material_t_max:
        if material_k < 80.0:
            remedy = "A low-conductivity liner cannot reject this flux. Thin the hot wall or switch to a copper alloy."
        else:
            remedy = "Raise coolant velocity or thin the hot wall."
        warnings.append(
            f"Peak gas-side wall temperature is {peak_tw:.0f} K, above the "
            f"{material_t_max:.0f} K service limit for this alloy. {remedy}"
        )
    if velocity > 40.0:
        warnings.append(
            f"Coolant velocity is {velocity:.0f} m/s. Pressure drop and erosion risk climb quickly above ~40 m/s."
        )
    if velocity < 3.0:
        warnings.append(
            f"Coolant velocity is only {velocity:.1f} m/s. The film is lazy; heat transfer will suffer."
        )
    if wall.min_land_m < 0.0012:
        warnings.append(
            f"Minimum land between channels is {wall.min_land_m * 1000:.2f} mm at the throat. "
            "That is fragile for LPBF. Reduce diameter or channel count."
        )
    if d < 0.0015:
        warnings.append(
            f"Channel diameter is {d * 1000:.2f} mm, below a comfortable LPBF feature size."
        )
    if delta_p > 0.7 * chamber_pressure:
        warnings.append(
            "Estimated coolant pressure drop exceeds half the chamber pressure. "
            "The pump margin on a real feed system would be tight."
        )

    return ThermalField(
        z_m=z,
        mach=mach,
        area_ratio=area_ratio,
        heat_flux_w_m2=q,
        h_gas=h_gas,
        wall_temp_k=tw,
        coolant_temp_k=t_cool,
        recovery_temp_k=taw,
        coolant_velocity_m_s=float(velocity),
        coolant_mass_flow_kg_s=float(mdot_cool),
        coolant_delta_t_k=float(t_cool[0] - coolant_inlet_k),
        coolant_delta_p_pa=float(delta_p),
        reynolds=float(re),
        h_coolant=float(h_cool),
        peak_heat_flux_w_m2=float(q[i_peak]),
        peak_wall_temp_k=peak_tw,
        peak_wall_z_m=float(z[int(np.argmax(tw))]),
        channel_diameter_m=float(d),
        warnings=warnings,
    )

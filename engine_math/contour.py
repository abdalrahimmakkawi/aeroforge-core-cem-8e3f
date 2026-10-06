"""Axisymmetric chamber and Rao-style bell contour.

The gas-side radius is a function of axial station: cylindrical chamber,
30° convergent cone, circular throat arcs, and a parabolic bell matched in
radius and slope. Length of the divergent section targets a chosen fraction
of a 15° reference cone (the usual 80% bell definition).
"""

from __future__ import annotations

from lazyimp import lazy

math = (lazy)("math")
dataclass = (lazy)("dataclasses", "dataclass")
np = (lazy)("numpy")
trapezoid = (lazy)("scipy.integrate", "trapezoid")


@dataclass
class GasContour:
    z_m: np.ndarray
    r_m: np.ndarray
    throat_z_m: float
    exit_z_m: float
    chamber_end_z_m: float
    gas_face_z_m: float
    bulkhead_m: float
    length_chamber_m: float
    length_nozzle_m: float
    lstar_actual_m: float
    chamber_volume_m3: float
    bell_fraction_actual: float
    entrance_angle_deg: float


def _divergent_length(theta_n: float, rt: float, re: float, rd: float, theta_e: float) -> float:
    z_n = rd * math.sin(theta_n)
    r_n = rt + rd * (1.0 - math.cos(theta_n))
    if re <= r_n * 1.002:
        return float("inf")
    dz = 2.0 * (re - r_n) / (math.tan(theta_n) + math.tan(theta_e))
    if dz <= 0:
        return float("inf")
    return z_n + dz


def _solve_entrance_angle(
    rt: float,
    re: float,
    rd: float,
    theta_e: float,
    target_length: float,
) -> tuple[float, float]:
    """Return (theta_n, achieved length) for the parabolic bell."""
    lo = theta_e + math.radians(0.8)
    hi = math.radians(48.0)
    # Keep the downstream arc inside the exit radius.
    while hi > lo + math.radians(1.0):
        r_n = rt + rd * (1.0 - math.cos(hi))
        if r_n < re * 0.97:
            break
        hi -= math.radians(2.0)

    l_long = _divergent_length(lo, rt, re, rd, theta_e)
    l_short = _divergent_length(hi, rt, re, rd, theta_e)
    if not math.isfinite(l_long):
        # Degenerate short nozzle: fall back to a shallow cone length.
        return lo, max(rd * math.sin(lo), 1e-4)
    if target_length >= l_long:
        return lo, l_long
    if not math.isfinite(l_short) or target_length <= l_short:
        length = l_short if math.isfinite(l_short) else l_long
        return hi, length

    for _ in range(48):
        mid = 0.5 * (lo + hi)
        length = _divergent_length(mid, rt, re, rd, theta_e)
        # Larger entrance angle shortens the bell.
        if length > target_length:
            lo = mid
        else:
            hi = mid
    theta_n = 0.5 * (lo + hi)
    return theta_n, _divergent_length(theta_n, rt, re, rd, theta_e)


def build_gas_contour(
    throat_radius_m: float,
    chamber_radius_m: float,
    exit_radius_m: float,
    lstar_m: float,
    *,
    exit_half_angle_deg: float = 10.0,
    bell_fraction: float = 0.80,
    convergent_half_angle_deg: float = 30.0,
    bulkhead_m: float = 0.010,
    n_points: int = 241,
) -> GasContour:
    """Sample the gas-side radius with the injector face at z = bulkhead."""
    rt = float(throat_radius_m)
    rc = float(chamber_radius_m)
    re = float(max(exit_radius_m, rt * 1.02))
    if rc < rt * 1.05:
        rc = rt * 1.05
    ru = 1.5 * rt
    rd = 0.382 * rt
    theta_e = math.radians(max(4.0, min(exit_half_angle_deg, 20.0)))
    theta_conv_max = math.radians(convergent_half_angle_deg)

    # Upstream arc can reach the chamber radius on its own when contraction is small.
    dr_arc_max = ru * (1.0 - math.cos(theta_conv_max))
    if rc - rt < dr_arc_max:
        theta_conv = math.acos(max(-1.0, min(1.0, 1.0 - (rc - rt) / ru)))
        z_junction = -ru * math.sin(theta_conv)  # throat at 0; no separate cone
        z_tangent = z_junction
        use_cone = False
    else:
        theta_conv = theta_conv_max
        z_tangent = -ru * math.sin(theta_conv)
        r_tangent = rt + ru * (1.0 - math.cos(theta_conv))
        z_junction = z_tangent - (rc - r_tangent) / math.tan(theta_conv)
        use_cone = True

    l15 = (re - rt) / math.tan(math.radians(15.0))
    target = max(0.55, bell_fraction) * l15
    theta_n, length_div = _solve_entrance_angle(rt, re, rd, theta_e, target)
    bell_actual = length_div / l15 if l15 > 0 else bell_fraction

    z_n = rd * math.sin(theta_n)
    r_n = rt + rd * (1.0 - math.cos(theta_n))
    s_n = math.tan(theta_n)
    s_e = math.tan(theta_e)
    dz_para = max(length_div - z_n, 1e-6)
    c_coef = (s_e - s_n) / (2.0 * dz_para)
    b_coef = s_n - 2.0 * c_coef * z_n
    a_coef = r_n - b_coef * z_n - c_coef * z_n * z_n
    z_exit_unshifted = z_n + dz_para

    def r_unshifted(z: np.ndarray) -> np.ndarray:
        z = np.asarray(z, dtype=float)
        r = np.empty_like(z)
        # Cylinder upstream of the convergent junction.
        cyl = z <= z_junction
        r[cyl] = rc
        if use_cone:
            cone = (z > z_junction) & (z < z_tangent)
            r[cone] = r_tangent + (z_tangent - z[cone]) * math.tan(theta_conv)
        arc_up = (z >= z_tangent) & (z < 0.0)
        if np.any(arc_up):
            th = np.arcsin(np.clip(-z[arc_up] / ru, 0.0, 1.0))
            r[arc_up] = rt + ru * (1.0 - np.cos(th))
        arc_dn = (z >= 0.0) & (z < z_n)
        if np.any(arc_dn):
            th = np.arcsin(np.clip(z[arc_dn] / max(rd, 1e-9), 0.0, 1.0))
            r[arc_dn] = rt + rd * (1.0 - np.cos(th))
        para = z >= z_n
        r[para] = a_coef + b_coef * z[para] + c_coef * z[para] ** 2
        throat = np.abs(z) < 1e-12
        r[throat] = rt
        return r

    z_conv = np.linspace(z_junction, 0.0, 400)
    vol_conv = float(trapezoid(math.pi * r_unshifted(z_conv) ** 2, z_conv))
    at = math.pi * rt * rt
    vol_target = max(lstar_m, 0.2) * at
    vol_cyl = vol_target - vol_conv
    lc = vol_cyl / (math.pi * rc * rc)
    min_lc = 0.35 * rc
    lc = max(lc, min_lc)

    z_gas_unshifted = z_junction - lc
    # Place the exterior injector face at z = 0 and the gas face at the bulkhead.
    shift = -z_gas_unshifted + bulkhead_m
    z_gas = bulkhead_m
    z_chamber_end = z_junction + shift
    z_throat = shift
    z_exit = z_exit_unshifted + shift

    # Cluster samples slightly toward the throat via a sine map around it.
    u = np.linspace(0.0, 1.0, n_points)
    z = z_gas + (z_exit - z_gas) * u
    # Evaluate radius in the unshifted frame.
    r = r_unshifted(z - shift)
    r = np.maximum(r, rt * 0.999)

    z_to_throat = np.linspace(z_gas, z_throat, 500)
    r_to_throat = r_unshifted(z_to_throat - shift)
    vol_chamber = float(trapezoid(math.pi * r_to_throat**2, z_to_throat))
    lstar_actual = vol_chamber / at

    return GasContour(
        z_m=z,
        r_m=r,
        throat_z_m=float(z_throat),
        exit_z_m=float(z_exit),
        chamber_end_z_m=float(z_chamber_end),
        gas_face_z_m=float(z_gas),
        bulkhead_m=float(bulkhead_m),
        length_chamber_m=float(z_throat - z_gas),
        length_nozzle_m=float(z_exit - z_throat),
        lstar_actual_m=float(lstar_actual),
        chamber_volume_m3=float(vol_chamber),
        bell_fraction_actual=float(bell_actual),
        entrance_angle_deg=float(math.degrees(theta_n)),
    )


def wall_angle(z_m: np.ndarray, r_m: np.ndarray) -> np.ndarray:
    """Local wall angle from the axis, radians, positive when radius grows downstream."""
    dr = np.gradient(r_m, z_m)
    return np.arctan(dr)


@dataclass
class WallContours:
    z_m: np.ndarray
    r_gas_m: np.ndarray
    r_liner_outer_m: np.ndarray
    r_channel_m: np.ndarray
    r_jacket_inner_m: np.ndarray
    r_jacket_outer_m: np.ndarray
    psi_rad: np.ndarray
    cos_psi: np.ndarray
    channel_diameter_m: float
    hot_wall_m: float
    closeout_m: float
    min_land_m: float
    tightest_radius_m: float


def build_wall(
    gas: GasContour,
    hot_wall_m: float,
    channel_diameter_m: float,
    closeout_m: float,
    channel_count: int,
) -> WallContours:
    """Stack liner, circular passages, and closeout on the gas contour.

    Thickness is applied radially and corrected by 1/cos(ψ) so the metal
    measured normal to the wall matches the requested hot-wall thickness.
    """
    psi = wall_angle(gas.z_m, gas.r_m)
    cos_psi = np.clip(np.cos(psi), 0.55, 1.0)
    t_hot = hot_wall_m / cos_psi
    d_rad = channel_diameter_m / cos_psi
    t_close = closeout_m / cos_psi

    r_liner = gas.r_m + t_hot
    r_channel = r_liner + 0.5 * d_rad
    r_jacket_in = r_liner + d_rad
    r_jacket_out = r_jacket_in + t_close

    # Land is tightest at the throat, where the circumference is smallest.
    i_th = int(np.argmin(np.abs(gas.z_m - gas.throat_z_m)))
    pitch = 2.0 * math.pi * float(r_channel[i_th]) / max(channel_count, 1)
    min_land = pitch - channel_diameter_m

    return WallContours(
        z_m=gas.z_m,
        r_gas_m=gas.r_m,
        r_liner_outer_m=r_liner,
        r_channel_m=r_channel,
        r_jacket_inner_m=r_jacket_in,
        r_jacket_outer_m=r_jacket_out,
        psi_rad=psi,
        cos_psi=cos_psi,
        channel_diameter_m=float(channel_diameter_m),
        hot_wall_m=float(hot_wall_m),
        closeout_m=float(closeout_m),
        min_land_m=float(min_land),
        tightest_radius_m=float(r_channel[i_th]),
    )

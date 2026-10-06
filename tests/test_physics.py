"""Isentropic sizing and the Bartz / coolant march."""

import math

import pytest

from engine_math.design import evaluate, reference_inputs
from engine_math.nozzle import (
    calculate_nozzle_parameters,
    expansion_ratio_from_mach,
    mach_from_expansion_ratio,
    thrust_coefficient,
)
from engine_math.presets import G0


def test_area_mach_roundtrip():
    gamma = 1.22
    for mach in (1.4, 2.2, 3.5):
        epsilon = expansion_ratio_from_mach(gamma, mach)
        recovered = mach_from_expansion_ratio(gamma, epsilon, supersonic=True)
        assert recovered == pytest.approx(mach, rel=1e-5)


def test_thrust_closure_and_isp_identity():
    nozzle = calculate_nozzle_parameters(25_000.0, 3.5e6, 101_325.0, gamma=1.2)
    cf = thrust_coefficient(
        nozzle["gamma"],
        nozzle["chamber_pressure_pa"],
        nozzle["exit_pressure_pa"],
        nozzle["ambient_pressure_pa"],
        nozzle["expansion_ratio"],
    )
    thrust = cf * nozzle["chamber_pressure_pa"] * nozzle["throat_area_m2"]
    assert thrust == pytest.approx(25_000.0, rel=1e-6)
    assert nozzle["isp_s"] == pytest.approx(nozzle["cf"] * nozzle["c_star_m_s"] / G0, rel=1e-6)
    assert nozzle["expansion_ratio"] > 1.5
    assert nozzle["exit_radius_mm"] > nozzle["throat_radius_mm"]
    assert 200.0 < nozzle["isp_s"] < 400.0
    assert 1500.0 < nozzle["c_star_m_s"] < 2200.0
    assert nozzle["adapted"] is True


def test_contour_is_a_bell_with_throat_at_the_minimum():
    nozzle = calculate_nozzle_parameters(25_000.0, 3.5e6, 101_325.0)
    gas = nozzle["contour"]
    i_throat = int(gas.r_m.argmin())
    assert gas.z_m[i_throat] == pytest.approx(gas.throat_z_m, abs=1e-3)
    assert gas.r_m[i_throat] == pytest.approx(nozzle["throat_radius_m"], rel=1e-3)
    assert gas.r_m[0] == pytest.approx(nozzle["chamber_radius_m"], rel=0.02)
    assert gas.r_m[-1] == pytest.approx(nozzle["exit_radius_m"], rel=0.02)
    downstream = gas.z_m >= gas.throat_z_m
    assert (gas.r_m[downstream][1:] >= gas.r_m[downstream][:-1] - 1e-6).all()
    # Requested L* is met by the cylindrical chamber, within the convergent contribution.
    assert nozzle["lstar_actual_m"] == pytest.approx(1.0, rel=0.08)


def test_vacuum_design_uses_requested_expansion():
    nozzle = calculate_nozzle_parameters(
        25_000.0,
        3.5e6,
        0.0,
        design_expansion_ratio=25.0,
    )
    assert nozzle["expansion_ratio"] == pytest.approx(25.0)
    assert nozzle["expansion_limited"] is True
    assert nozzle["exit_pressure_pa"] > 0.0
    assert nozzle["isp_s"] > nozzle["isp_sea_level_s"]


def test_heat_flux_peaks_at_the_throat_and_copper_runs_cooler_than_inconel():
    copper = evaluate(reference_inputs())
    field = copper["thermal"]
    assert field.peak_heat_flux_w_m2 > 5e6
    assert field.peak_heat_flux_w_m2 < 2e8
    i_peak = int(field.heat_flux_w_m2.argmax())
    assert abs(field.z_m[i_peak] - copper["gas"].throat_z_m) < 0.05 * copper["gas"].exit_z_m
    assert field.coolant_delta_t_k > 5.0
    assert field.wall_temp_k.min() > field.coolant_temp_k.min()

    inconel = evaluate(reference_inputs(material_key="inconel718"))
    assert inconel["thermal"].peak_wall_temp_k > copper["thermal"].peak_wall_temp_k + 80.0


def test_invalid_pressure_ratio_is_rejected():
    with pytest.raises(ValueError):
        calculate_nozzle_parameters(1000.0, 50_000.0, 101_325.0)


def test_reference_lengths_are_finite_and_printable_scale():
    design = evaluate(reference_inputs())
    nozzle = design["nozzle"]
    assert 10.0 < nozzle["throat_radius_mm"] < 150.0
    assert nozzle["nozzle_length_mm"] > nozzle["throat_radius_mm"]
    assert design["mass_kg"] > 0.2
    assert math.isfinite(design["thermal"].coolant_delta_p_pa)

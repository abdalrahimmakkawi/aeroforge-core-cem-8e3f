"""Representative propellants and liner alloys.

Gas properties are frozen-gamma idealizations for preliminary sizing, not
equilibrium combustion (CEA) results. Coolant properties are liquid-phase
values near a typical inlet state.
"""

from __future__ import annotations

PROPELLANTS: dict[str, dict] = {
    "lox_methane": {
        "label": "LOX / LCH4",
        "gamma": 1.20,
        "chamber_temp_k": 3550.0,
        "molar_mass": 21.0,
        "of_ratio": 3.4,
        "coolant_name": "Liquid methane",
        "coolant_rho": 422.0,
        "coolant_cp": 3480.0,
        "coolant_k": 0.19,
        "coolant_mu": 1.2e-4,
        "coolant_pr": 2.2,
        "coolant_inlet_k": 111.0,
    },
    "lox_rp1": {
        "label": "LOX / RP-1",
        "gamma": 1.22,
        "chamber_temp_k": 3670.0,
        "molar_mass": 23.5,
        "of_ratio": 2.30,
        "coolant_name": "RP-1",
        "coolant_rho": 810.0,
        "coolant_cp": 2000.0,
        "coolant_k": 0.13,
        "coolant_mu": 1.6e-3,
        "coolant_pr": 21.0,
        "coolant_inlet_k": 298.0,
    },
    "lox_lh2": {
        "label": "LOX / LH2",
        "gamma": 1.26,
        "chamber_temp_k": 3400.0,
        "molar_mass": 13.5,
        "of_ratio": 5.5,
        "coolant_name": "Liquid hydrogen",
        "coolant_rho": 70.8,
        "coolant_cp": 9700.0,
        "coolant_k": 0.10,
        "coolant_mu": 1.3e-5,
        "coolant_pr": 1.25,
        "coolant_inlet_k": 23.0,
    },
}

MATERIALS: dict[str, dict] = {
    "grcop42": {
        "label": "GRCop-42",
        "k": 300.0,
        "t_max_k": 850.0,
        "density": 8900.0,
        "note": "Copper-chrome-niobium liner alloy used in printed regen chambers.",
    },
    "cucrzr": {
        "label": "CuCrZr",
        "k": 320.0,
        "t_max_k": 800.0,
        "density": 8900.0,
        "note": "High-conductivity copper liner. Strength falls quickly above ~800 K.",
    },
    "inconel718": {
        "label": "Inconel 718",
        "k": 15.0,
        "t_max_k": 1000.0,
        "density": 8190.0,
        "note": "Structural nickel alloy. A poor liner unless the hot wall is very thin.",
    },
    "ss316l": {
        "label": "316L",
        "k": 16.0,
        "t_max_k": 900.0,
        "density": 8000.0,
        "note": "Printable stainless closeout / low-conductivity liner.",
    },
}

AMBIENTS: dict[str, dict] = {
    "sea_level": {"label": "Sea level", "pressure_pa": 101_325.0, "expansion": None},
    "10km": {"label": "10 km", "pressure_pa": 26_500.0, "expansion": None},
    "20km": {"label": "20 km", "pressure_pa": 5_529.0, "expansion": None},
    "vacuum": {"label": "Vacuum", "pressure_pa": 0.0, "expansion": 25.0},
}

G0 = 9.80665
R_UNIV = 8314.462618

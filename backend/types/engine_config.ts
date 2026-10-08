/**
 * EngineConfig — the ONLY JSON the LLM / Studio may emit for geometry.
 * Algorithm X (deterministic C#) consumes this; no mesh code crosses this boundary.
 */
export type CoolingType = "gyroid_tpms" | "helical" | "none";

export interface EngineConfig {
  /** Design thrust in kilonewtons (5–100). */
  thrust_kn: number;
  /** Chamber pressure in bar (10–150). */
  chamber_pressure_bar: number;
  /** Nozzle area ratio ε = A_e / A_t (optional; sized from Pc if omitted). */
  expansion_ratio?: number;
  /** Cooling architecture — Algorithm X defaults to TPMS gyroid. */
  cooling_type: CoolingType;
  /** Gyroid unit-cell size in mm (smaller → denser lattice). */
  gyroid_cell_size_mm: number;
  /** Murray-law manifold tip count. */
  manifold_branches: number;
  /** Coaxial swirler element count on the injector faceplate. */
  swirler_count: number;
  /** Hot-wall thickness in mm. */
  wall_thickness_mm: number;
  /** Optional gas / propellant overrides. */
  gamma?: number;
  chamber_temp_k?: number;
  molar_mass?: number;
  propellant?: "lox_methane" | "lox_rp1" | "lox_lh2";
  ambient_pressure_pa?: number;
  voxel_mm?: number;
}

/** Canonical defaults for a ~50 kN MethaLOX regenerative forge. */
export const DEFAULT_ENGINE_CONFIG: EngineConfig = {
  thrust_kn: 50,
  chamber_pressure_bar: 60,
  expansion_ratio: 18.5,
  cooling_type: "gyroid_tpms",
  gyroid_cell_size_mm: 3.2,
  manifold_branches: 64,
  swirler_count: 54,
  wall_thickness_mm: 3.5,
  propellant: "lox_methane",
  voxel_mm: 1.5,
};

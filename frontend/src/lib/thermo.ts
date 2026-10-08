/** Client-side isentropic sizing — keeps Thermodynamics stats live without waiting on the API. */

const G0 = 9.80665;
const R_UNIV = 8314.462618;

export type PropellantId = "lox_methane" | "lox_rp1" | "lox_lh2";

const PROPELLANTS: Record<
  PropellantId,
  { label: string; gamma: number; chamber_temp_k: number; molar_mass: number; of_ratio: number }
> = {
  lox_methane: {
    label: "LOX / LCH4 (MethaLOX)",
    gamma: 1.2,
    chamber_temp_k: 3550,
    molar_mass: 21,
    of_ratio: 3.4,
  },
  lox_rp1: {
    label: "LOX / RP-1",
    gamma: 1.22,
    chamber_temp_k: 3670,
    molar_mass: 23.5,
    of_ratio: 2.3,
  },
  lox_lh2: {
    label: "LOX / LH2",
    gamma: 1.26,
    chamber_temp_k: 3400,
    molar_mass: 14,
    of_ratio: 5.5,
  },
};

export type SizedThermo = {
  propellant: string;
  propellant_label: string;
  gamma: number;
  chamber_temp_k: number;
  molar_mass: number;
  of_ratio: number;
  c_star_m_s: number;
  cf: number;
  expansion_ratio: number;
  exit_mach: number;
  throat_area_m2: number;
  throat_radius_mm: number;
  exit_radius_mm: number;
  chamber_radius_mm: number;
  mass_flow_kg_s: number;
  isp_s: number;
  thrust_n: number;
  chamber_pressure_pa: number;
  ambient_pressure_pa: number;
  exit_pressure_pa: number;
  length_mm: number;
};

function characteristicVelocity(gamma: number, tc: number, molarMass: number): number {
  const r = R_UNIV / molarMass;
  const exp = (gamma + 1) / (2 * (gamma - 1));
  return Math.sqrt((r * tc) / gamma) * ((gamma + 1) / 2) ** exp;
}

function areaRatio(gamma: number, mach: number): number {
  const m = Math.max(mach, 1e-6);
  const core = (1 + 0.5 * (gamma - 1) * m * m) / ((gamma + 1) / 2);
  return (1 / m) * core ** ((gamma + 1) / (2 * (gamma - 1)));
}

function machFromEpsilon(gamma: number, epsilon: number): number {
  let lo = 1.0001;
  let hi = 2;
  for (let i = 0; i < 40; i++) {
    if (areaRatio(gamma, hi) >= epsilon) break;
    hi *= 1.4;
    if (hi > 50) return 50;
  }
  for (let i = 0; i < 60; i++) {
    const mid = 0.5 * (lo + hi);
    if (areaRatio(gamma, mid) > epsilon) hi = mid;
    else lo = mid;
  }
  return 0.5 * (lo + hi);
}

function expansionRatioFromPressure(gamma: number, pc: number, pa: number): [number, number] {
  if (pa <= 1) {
    const eps = 25;
    return [eps, machFromEpsilon(gamma, eps)];
  }
  const pe = pa;
  const ratio = (pc / pe) ** ((gamma - 1) / gamma);
  let me = Math.sqrt(Math.max((2 / (gamma - 1)) * (ratio - 1), 0));
  me = Math.max(me, 1.05);
  const eps = Math.min(areaRatio(gamma, me), 80);
  return [eps, me];
}

function thrustCoefficient(gamma: number, pePc: number, paPc: number, epsilon: number): number {
  const pe = Math.min(Math.max(pePc, 1e-12), 0.999999);
  const mom =
    ((2 * gamma * gamma) / (gamma - 1)) *
    (2 / (gamma + 1)) ** ((gamma + 1) / (gamma - 1)) *
    (1 - pe ** ((gamma - 1) / gamma));
  return Math.sqrt(Math.max(mom, 0)) + (pe - paPc) * epsilon;
}

/** Match backend GasDynamics / thermo.size_engine length scaling. */
function engineLengthMm(thrustkN: number, rt: number, re: number, rc: number, at: number): number {
  const lStar = Math.min(800, Math.max(400, 900));
  const vc = lStar * (at * 1e9);
  const Lconv = (rc - rt) / Math.tan((30 * Math.PI) / 180);
  const Lbell = 0.8 * (re - rt) / Math.tan((15 * Math.PI) / 180);
  const LcylRaw = vc / (Math.PI * rc * rc) - 0.5 * Lconv;
  const Lcyl = Math.min(Math.max(LcylRaw, rt * 1.6), rt * 4.2);
  const maxLen = Math.min(Math.max(150 + thrustkN * 3.4, 160), 420);
  const minLen = Math.min(Math.max(110 + thrustkN * 1.2, 120), 220);
  return Math.min(Math.max(Lcyl + Lconv + Lbell, minLen), maxLen);
}

export function sizeEngine(opts: {
  thrustKn: number;
  chamberPressureMpa: number;
  propellant: PropellantId;
  ambientPressurePa?: number;
}): SizedThermo {
  const thrustKn = Math.min(100, Math.max(5, opts.thrustKn));
  const pcMpa = Math.min(15, Math.max(1, opts.chamberPressureMpa));
  const gas = PROPELLANTS[opts.propellant] ?? PROPELLANTS.lox_methane;
  const thrustN = thrustKn * 1000;
  const pc = pcMpa * 1e6;
  const pa = opts.ambientPressurePa ?? 101325;
  const g = gas.gamma;
  const tc = gas.chamber_temp_k;
  const mm = gas.molar_mass;

  const cStar = characteristicVelocity(g, tc, mm);
  const [epsilon, me] = expansionRatioFromPressure(g, pc, pa);
  const pe =
    pa > 1
      ? pa
      : pc * (1 + 0.5 * (g - 1) * me * me) ** (-g / (g - 1));
  const cf = thrustCoefficient(g, pe / pc, pa / pc, epsilon);
  const at = thrustN / (pc * Math.max(cf, 0.05));
  const rtM = Math.sqrt(at / Math.PI);
  const reM = rtM * Math.sqrt(epsilon);
  const rt = rtM * 1000;
  const re = reM * 1000;
  const rc = rt * 2.35;
  const mdot = (pc * at) / cStar;
  const isp = thrustN / (mdot * G0);

  return {
    propellant: opts.propellant,
    propellant_label: gas.label,
    gamma: g,
    chamber_temp_k: tc,
    molar_mass: mm,
    of_ratio: gas.of_ratio,
    c_star_m_s: cStar,
    cf,
    expansion_ratio: epsilon,
    exit_mach: me,
    throat_area_m2: at,
    throat_radius_mm: rt,
    exit_radius_mm: re,
    chamber_radius_mm: rc,
    mass_flow_kg_s: mdot,
    isp_s: isp,
    thrust_n: thrustN,
    chamber_pressure_pa: pc,
    ambient_pressure_pa: pa,
    exit_pressure_pa: pe,
    length_mm: engineLengthMm(thrustKn, rt, re, rc, at),
  };
}

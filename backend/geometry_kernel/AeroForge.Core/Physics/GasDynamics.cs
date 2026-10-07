using System.Numerics;

namespace AeroForge.Core.Physics;

/// <summary>
/// First-principles isentropic nozzle sizing.
/// All linear results are millimetres unless noted.
/// </summary>
public sealed class GasDynamicsResult
{
    public float ThrustN { get; init; }
    public float ChamberPressurePa { get; init; }
    public float AmbientPressurePa { get; init; }
    public float Gamma { get; init; }
    public float ChamberTempK { get; init; }
    public float MolarMass { get; init; }
    public float CStar { get; init; }
    public float Cf { get; init; }
    public float ExpansionRatio { get; init; }
    public float ExitMach { get; init; }
    public float ThroatAreaM2 { get; init; }
    public float rThroat { get; init; }
    public float rExit { get; init; }
    public float rChamber { get; init; }
    public float rInjector { get; init; }
    public float LengthMm { get; init; }
    public float ChamberVolumeMm3 { get; init; }
    public float LStarMm { get; init; }
    public float IspS { get; init; }
    public float MassFlowKgS { get; init; }
}

/// <summary>Deterministic gas-dynamic sizing for regenerative chambers.</summary>
public static class GasDynamics
{
    private const float G0 = 9.80665f;
    private const float RUniv = 8314.462618f;

    /// <summary>
    /// Size a MethaLOX-class engine from thrust (kN) and chamber pressure (bar).
    /// </summary>
    public static GasDynamicsResult Calculate(
        float thrustkN,
        float chamberPressureBar,
        float ambientPressurePa = 101325f,
        float gamma = 1.20f,
        float chamberTempK = 3550f,
        float molarMass = 21f,
        float lStarMm = 900f)
    {
        float F = MathF.Max(thrustkN, 1f) * 1000f;           // N
        float Pc = MathF.Max(chamberPressureBar, 5f) * 1e5f; // Pa
        float Pa = MathF.Max(ambientPressurePa, 0f);
        float g = Math.Clamp(gamma, 1.05f, 1.67f);
        float Tc = MathF.Max(chamberTempK, 2000f);
        float mm = MathF.Max(molarMass, 8f);

        float cStar = CharacteristicVelocity(g, Tc, mm);
        var (eps, me) = ExpansionRatioFromPressure(g, Pc, Pa);
        float pe = Pa > 1f
            ? Pa
            : Pc * MathF.Pow(1f + 0.5f * (g - 1f) * me * me, -g / (g - 1f));
        float cf = ThrustCoefficient(g, pe / Pc, Pa / Pc, eps);

        // A_t = F / (P_c · C_f)
        float at = F / (Pc * MathF.Max(cf, 0.05f));
        float rtM = MathF.Sqrt(at / MathF.PI);
        float reM = rtM * MathF.Sqrt(eps);
        float rt = rtM * 1000f;
        float re = reM * 1000f;
        float rc = rt * 2.35f;
        // Compact L* keeps interactive STLs printable (full-scale L* blows voxel budgets).
        float lStar = Math.Clamp(lStarMm, 400f, 800f);
        // V_c = L* · A_t  (mm³)
        float vc = lStar * (at * 1e9f);
        // Length: chamber cylinder + convergent + ~80% Rao bell of 15° cone
        float Lconv = (rc - rt) / MathF.Tan(30f * MathF.PI / 180f);
        float Lbell = 0.80f * (re - rt) / MathF.Tan(15f * MathF.PI / 180f);
        float LcylRaw = vc / (MathF.PI * rc * rc) - 0.5f * Lconv;
        float Lcyl = Math.Clamp(LcylRaw, rt * 1.6f, rt * 3.8f);
        // Cap overall length so 50 kN forges stay under ~10–15 MB STL at 1.5–2.0 mm voxels.
        float length = Math.Clamp(Lcyl + Lconv + Lbell, 140f, 300f);

        float mdot = Pc * at / cStar;
        float isp = F / (mdot * G0);

        return new GasDynamicsResult
        {
            ThrustN = F,
            ChamberPressurePa = Pc,
            AmbientPressurePa = Pa,
            Gamma = g,
            ChamberTempK = Tc,
            MolarMass = mm,
            CStar = cStar,
            Cf = cf,
            ExpansionRatio = eps,
            ExitMach = me,
            ThroatAreaM2 = at,
            rThroat = rt,
            rExit = re,
            rChamber = rc,
            rInjector = rc,
            LengthMm = length,
            ChamberVolumeMm3 = vc,
            LStarMm = lStar,
            IspS = isp,
            MassFlowKgS = mdot,
        };
    }

    public static float CharacteristicVelocity(float gamma, float tc, float molarMass)
    {
        float r = RUniv / molarMass;
        float exp = (gamma + 1f) / (2f * (gamma - 1f));
        return MathF.Sqrt(r * tc / gamma) * MathF.Pow((gamma + 1f) / 2f, exp);
    }

    public static float ThrustCoefficient(float gamma, float pePc, float paPc, float epsilon)
    {
        pePc = Math.Clamp(pePc, 1e-12f, 0.999999f);
        float mom =
            (2f * gamma * gamma / (gamma - 1f))
            * MathF.Pow(2f / (gamma + 1f), (gamma + 1f) / (gamma - 1f))
            * (1f - MathF.Pow(pePc, (gamma - 1f) / gamma));
        return MathF.Sqrt(MathF.Max(mom, 0f)) + (pePc - paPc) * epsilon;
    }

    public static (float epsilon, float exitMach) ExpansionRatioFromPressure(
        float gamma, float pc, float pa)
    {
        if (pa <= 1f)
        {
            float epsVac = 25f;
            return (epsVac, MachFromEpsilon(gamma, epsVac));
        }
        float pe = pa;
        float ratio = MathF.Pow(pc / pe, (gamma - 1f) / gamma);
        float me = MathF.Sqrt(MathF.Max(2f / (gamma - 1f) * (ratio - 1f), 0f));
        me = MathF.Max(me, 1.05f);
        float eps = MathF.Min(AreaRatio(gamma, me), 80f);
        return (eps, me);
    }

    public static float AreaRatio(float gamma, float mach)
    {
        mach = MathF.Max(mach, 1e-6f);
        float core = (1f + 0.5f * (gamma - 1f) * mach * mach) / ((gamma + 1f) / 2f);
        return (1f / mach) * MathF.Pow(core, (gamma + 1f) / (2f * (gamma - 1f)));
    }

    public static float MachFromEpsilon(float gamma, float epsilon)
    {
        float lo = 1.0001f, hi = 2f;
        for (int i = 0; i < 40; i++)
        {
            if (AreaRatio(gamma, hi) >= epsilon) break;
            hi *= 1.4f;
            if (hi > 50f) return 50f;
        }
        for (int i = 0; i < 60; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (AreaRatio(gamma, mid) > epsilon) hi = mid;
            else lo = mid;
        }
        return 0.5f * (lo + hi);
    }
}

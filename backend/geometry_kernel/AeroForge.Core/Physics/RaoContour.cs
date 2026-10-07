using System.Numerics;

namespace AeroForge.Core.Physics;

/// <summary>
/// 2D parabolic Rao / CD nozzle contour as (Z, R) samples in millimetres.
/// Regions: chamber cylinder → convergent → throat arc → divergent bell.
/// </summary>
public static class RaoContour
{
    /// <summary>
    /// Dense gas-side profile spanning injector face to nozzle exit.
    /// Vector2.X = axial Z (mm), Vector2.Y = radius (mm).
    /// </summary>
    public static List<Vector2> GetEngineProfile(GasDynamicsResult physics, int samples = 96)
    {
        float rt = MathF.Max(physics.rThroat, 8f);
        float re = MathF.Max(physics.rExit, rt * 1.5f);
        float rc = MathF.Max(physics.rChamber, rt * 2.0f);
        float L = MathF.Max(physics.LengthMm, 140f);
        samples = Math.Clamp(samples, 48, 160);

        // Station fractions along axial length (smoothstep between control radii).
        // Contraction ~30°, throat circular fillet ~1.5·rt, Rao bell from ~0.8·rt onward.
        float throatT = 0.42f;
        var stations = new (float t, float r)[]
        {
            (0.00f, rc),                          // injector / chamber cylinder
            (0.14f, rc),
            (0.26f, rc * 0.97f),                  // start convergent (~15–45°)
            (0.34f, (rc + rt) * 0.55f),
            (throatT, rt),                        // circular throat
            (0.48f, rt * 1.05f),                  // immediate expansion (~0.8·rt station)
            (0.62f, rt + (re - rt) * 0.32f),      // Rao parabolic mid
            (0.80f, rt + (re - rt) * 0.72f),
            (1.00f, re),                          // exit angle θ_e
        };

        var profile = new List<Vector2>(samples + 1);
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            float r = SampleStations(stations, t);
            // Mild Rao parabola bias on the divergent side for exit angle softening.
            if (t > throatT)
            {
                float u = (t - throatT) / (1f - throatT);
                float parabolic = rt + (re - rt) * (1f - MathF.Pow(1f - u, 1.65f));
                float blend = Smoothstep(0f, 1f, u);
                r = r * (1f - 0.55f * blend) + parabolic * (0.55f * blend);
            }
            profile.Add(new Vector2(t * L, MathF.Max(r, rt * 0.98f)));
        }

        // Enforce monotonic Z and smooth throat minimum.
        EnforceThroatMinimum(profile, rt);
        return profile;
    }

    /// <summary>Outward wall-normal offset of a gas profile by wall thickness (mm).</summary>
    public static List<Vector2> OffsetProfile(List<Vector2> gas, float wallMm)
    {
        wallMm = MathF.Max(wallMm, 1.5f);
        if (gas.Count < 2)
            return gas.Select(p => new Vector2(p.X, p.Y + wallMm)).ToList();

        var outer = new List<Vector2>(gas.Count);
        for (int i = 0; i < gas.Count; i++)
        {
            Vector2 prev = gas[Math.Max(0, i - 1)];
            Vector2 next = gas[Math.Min(gas.Count - 1, i + 1)];
            Vector2 tangent = Vector2.Normalize(next - prev);
            Vector2 normal = new(-tangent.Y, tangent.X);
            if (normal.Y < 0) normal = -normal;
            Vector2 p = gas[i] + normal * wallMm;
            p.Y = MathF.Max(p.Y, gas[i].Y + wallMm * 0.85f);
            outer.Add(p);
        }
        for (int i = 1; i < outer.Count; i++)
        {
            if (outer[i].X <= outer[i - 1].X)
                outer[i] = new Vector2(outer[i - 1].X + 0.05f, outer[i].Y);
        }
        return outer;
    }

    public static float RadiusAtZ(List<Vector2> profile, float z)
    {
        if (profile.Count == 0) return 0f;
        if (z <= profile[0].X) return profile[0].Y;
        if (z >= profile[^1].X) return profile[^1].Y;
        for (int i = 1; i < profile.Count; i++)
        {
            if (z <= profile[i].X)
            {
                float z0 = profile[i - 1].X, z1 = profile[i].X;
                float u = (z - z0) / MathF.Max(z1 - z0, 1e-6f);
                return profile[i - 1].Y * (1f - u) + profile[i].Y * u;
            }
        }
        return profile[^1].Y;
    }

    private static void EnforceThroatMinimum(List<Vector2> profile, float rt)
    {
        int imin = 0;
        float rmin = float.MaxValue;
        for (int i = 0; i < profile.Count; i++)
        {
            if (profile[i].Y < rmin)
            {
                rmin = profile[i].Y;
                imin = i;
            }
        }
        // Soften neighbors toward throat.
        for (int i = Math.Max(0, imin - 2); i <= Math.Min(profile.Count - 1, imin + 2); i++)
        {
            float w = 1f - MathF.Abs(i - imin) / 3f;
            float r = profile[i].Y * (1f - w) + rt * w;
            profile[i] = new Vector2(profile[i].X, r);
        }
    }

    private static float SampleStations((float t, float r)[] stations, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        for (int i = 1; i < stations.Length; i++)
        {
            if (t <= stations[i].t)
            {
                float t0 = stations[i - 1].t, t1 = stations[i].t;
                float u = (t - t0) / MathF.Max(t1 - t0, 1e-6f);
                float s = Smoothstep(0f, 1f, u);
                return stations[i - 1].r * (1f - s) + stations[i].r * s;
            }
        }
        return stations[^1].r;
    }

    private static float Smoothstep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / MathF.Max(b - a, 1e-6f), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

using System.Numerics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Geometry;

/// <summary>
/// Rao / parabolic convergent–divergent gas-path profiles as (Z, R) samples.
/// </summary>
public static class ProfileHelper
{
    /// <summary>
    /// Build a chamber → convergent → throat → Rao/parabolic divergent profile.
    /// Vector2.X = axial Z (mm), Vector2.Y = gas-side radius (mm).
    /// </summary>
    public static List<Vector2> GetRaoProfile(float rThroat, float rExit, float length, float? rChamber = null)
    {
        float rt = MathF.Max(rThroat, 8f);
        float re = MathF.Max(rExit, rt * 1.5f);
        float L = MathF.Max(length, 120f);
        float rc = rChamber ?? rt * 2.35f;

        // Control radii at keyed stations (fraction of length).
        var stations = new (float t, float r)[]
        {
            (0.00f, rc),
            (0.16f, rc),
            (0.28f, rc * 0.95f),
            (0.36f, (rc + rt) * 0.55f),
            (0.42f, rt),                 // throat
            (0.50f, rt * 1.06f),
            (0.65f, rt + (re - rt) * 0.38f),
            (0.82f, rt + (re - rt) * 0.78f),
            (1.00f, re),
        };

        // Dense sample via cubic Hermite between stations.
        var profile = new List<Vector2>(96);
        const int samples = 80;
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            float r = SampleStations(stations, t);
            profile.Add(new Vector2(t * L, r));
        }
        return profile;
    }

    /// <summary>Normal-offset the gas profile outward by wall thickness (mm).</summary>
    public static List<Vector2> GetOffsetProfile(List<Vector2> gasPathProfile, float offsetMm)
    {
        offsetMm = MathF.Max(offsetMm, 1.5f);
        if (gasPathProfile.Count < 2)
            return gasPathProfile.Select(p => new Vector2(p.X, p.Y + offsetMm)).ToList();

        var outer = new List<Vector2>(gasPathProfile.Count);
        for (int i = 0; i < gasPathProfile.Count; i++)
        {
            Vector2 prev = gasPathProfile[Math.Max(0, i - 1)];
            Vector2 next = gasPathProfile[Math.Min(gasPathProfile.Count - 1, i + 1)];
            Vector2 tangent = Vector2.Normalize(next - prev);
            // Outward in RZ plane: rotate tangent +90° → (-ty, tx) pointing to +R for +Z flow.
            Vector2 normal = new(-tangent.Y, tangent.X);
            if (normal.Y < 0) normal = -normal; // prefer +R
            Vector2 p = gasPathProfile[i] + normal * offsetMm;
            // Keep monotonic Z and positive radius.
            p.Y = MathF.Max(p.Y, gasPathProfile[i].Y + offsetMm * 0.85f);
            outer.Add(p);
        }
        // Enforce strictly increasing Z.
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

    public static float Length(List<Vector2> profile) =>
        profile.Count == 0 ? 0f : profile[^1].X - profile[0].X;

    private static float SampleStations((float t, float r)[] stations, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        for (int i = 1; i < stations.Length; i++)
        {
            if (t <= stations[i].t)
            {
                float t0 = stations[i - 1].t, t1 = stations[i].t;
                float u = (t - t0) / MathF.Max(t1 - t0, 1e-6f);
                // Smoothstep for Rao-like curvature.
                float s = u * u * (3f - 2f * u);
                return stations[i - 1].r * (1f - s) + stations[i].r * s;
            }
        }
        return stations[^1].r;
    }
}

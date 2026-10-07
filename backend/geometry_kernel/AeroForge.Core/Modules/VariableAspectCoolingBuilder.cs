using System.Numerics;
using AeroForge.Core.Physics;
using PicoGK;

namespace AeroForge.Core.Modules;

/// <summary>
/// Conformal regenerative micro-channels with heat-flux-driven variable aspect ratio
/// and teardrop cross-sections (AM self-supporting).
/// </summary>
public static class VariableAspectCoolingBuilder
{
    /// <summary>
    /// Generate 64–128 helical conformal passages. Depth peaks at the throat (Q_max).
    /// Pass result to <c>voxWall.BoolSubtract(voxChannels)</c> for visible exterior ribs.
    /// </summary>
    public static Voxels GeneratePassages(
        List<Vector2> gasProfile,
        int channelCount = 72,
        bool teardropProfile = true,
        float wallMm = 3.2f,
        float helixTurns = 2.8f)
    {
        channelCount = Math.Clamp(channelCount, 64, 128);
        wallMm = MathF.Max(wallMm, 3.0f);
        helixTurns = MathF.Max(helixTurns, 2.0f);

        List<Vector2> outer = RaoContour.OffsetProfile(gasProfile, wallMm);
        float z0 = outer[0].X;
        float z1 = outer[^1].X;
        float L = MathF.Max(z1 - z0, 40f);

        // Throat station ≈ minimum gas radius → peak heat flux.
        float zThroat = gasProfile[0].X;
        float rMin = float.MaxValue;
        foreach (var p in gasProfile)
        {
            if (p.Y < rMin)
            {
                rMin = p.Y;
                zThroat = p.X;
            }
        }

        var lat = new Lattice();
        int samples = channelCount >= 96 ? 48 : 56;

        for (int i = 0; i < channelCount; i++)
        {
            float phase = 2f * MathF.PI * i / channelCount;
            Vector3? prevCore = null;
            Vector3? prevOuter = null;

            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                float z = z0 + t * L * 0.98f + L * 0.01f;
                float rOuter = RaoContour.RadiusAtZ(outer, z);

                // Heat-flux weight: 1 at throat, ~0.35 far away.
                float dist = MathF.Abs(z - zThroat) / MathF.Max(L * 0.35f, 1f);
                float q = Math.Clamp(1f - dist, 0.35f, 1f);
                q = q * q * (3f - 2f * q); // smoothstep-ish

                // High aspect at throat (narrow & deep); widen in divergent.
                float depth = Math.Clamp(1.6f + 1.6f * q, 1.5f, wallMm * 0.78f);
                float width = Math.Clamp(2.4f - 1.0f * q, 1.1f, 2.6f);

                float halfD = depth * 0.48f;
                float halfW = width * 0.45f;
                float rCenter = rOuter - halfD;
                float theta = phase + helixTurns * 2f * MathF.PI * t;

                var center = new Vector3(
                    rCenter * MathF.Cos(theta),
                    rCenter * MathF.Sin(theta),
                    z);

                // Radial unit (outward) for teardrop: fat side toward outer wall.
                var radial = Vector3.Normalize(new Vector3(center.X, center.Y, 0));
                if (radial.LengthSquared() < 1e-6f) radial = Vector3.UnitX;

                // Core beam (pointy toward gas / inward) + outer fat beam = teardrop.
                var core = center - radial * (halfD * 0.35f);
                var fat = center + radial * (halfD * 0.25f);

                float coreR = teardropProfile ? halfW * 0.55f : halfD;
                float fatR = teardropProfile ? halfW * 0.95f : halfD;

                if (prevCore is Vector3 pc)
                {
                    lat.AddBeam(pc, coreR, core, coreR, false);
                    lat.AddBeam(prevOuter!.Value, fatR, fat, fatR, false);
                    // Fill teardrop body
                    lat.AddBeam(pc, (coreR + fatR) * 0.35f, fat, (coreR + fatR) * 0.35f, false);
                }

                prevCore = core;
                prevOuter = fat;
            }
        }

        return new Voxels(lat);
    }
}

using System.Numerics;
using AeroForge.Geometry;
using PicoGK;

namespace AeroForge.Modules;

/// <summary>
/// Surface-visible helical regenerative cooling channels.
/// Channels are carved from the outer jacket so ribs remain between grooves.
/// </summary>
public static class CoolingChannelBuilder
{
    /// <summary>
    /// Build a lattice of helical cooling grooves conformal to the chamber outer wall.
    /// Pass the result to <c>chamber.BoolSubtract(channels)</c> for visible ribbing.
    /// </summary>
    /// <param name="chamber">Hollow chamber jacket (used for bounding / length inference).</param>
    /// <param name="count">Number of helical starts (32–64).</param>
    /// <param name="depthMm">Radial groove depth (mm).</param>
    /// <param name="gasProfile">Optional gas-side profile; derived from chamber bbox if null.</param>
    /// <param name="wallMm">Wall thickness used to place grooves on the outer face.</param>
    /// <param name="helixTurns">Helix turns along chamber length.</param>
    /// <param name="channelWidthMm">Approx. rectangular channel width (mm).</param>
    public static Voxels BuildHelical(
        Voxels chamber,
        int count = 48,
        float depthMm = 2.5f,
        List<Vector2>? gasProfile = null,
        float wallMm = 3.5f,
        float helixTurns = 2.6f,
        float channelWidthMm = 2.2f)
    {
        count = Math.Clamp(count, 32, 64);
        depthMm = Math.Clamp(depthMm, 1.2f, MathF.Max(wallMm * 0.75f, 1.5f));
        wallMm = MathF.Max(wallMm, 3.0f);
        helixTurns = MathF.Max(helixTurns, 1.8f);

        BBox3 bb = chamber.oCalculateBoundingBox();
        float z0 = bb.vecMin.Z;
        float z1 = bb.vecMax.Z;
        float L = MathF.Max(z1 - z0, 40f);

        // Prefer explicit gas profile; otherwise synthesize from throat/exit of bbox.
        List<Vector2> gas = gasProfile ?? SynthesizeProfileFromBounds(bb);
        List<Vector2> outer = ProfileHelper.GetOffsetProfile(gas, wallMm);

        // Rectangular channel ≈ dual parallel circular beams (width × depth).
        float halfW = Math.Clamp(channelWidthMm * 0.5f, 0.7f, depthMm * 0.9f);
        float halfD = depthMm * 0.48f;

        var lat = new Lattice();
        const int samples = 110;
        for (int i = 0; i < count; i++)
        {
            float phase = 2f * MathF.PI * i / count;
            Vector3? prevA = null;
            Vector3? prevB = null;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                float z = z0 + t * L * 0.98f + L * 0.01f;
                float rOuter = ProfileHelper.RadiusAtZ(outer, z);
                // Center of groove just inside the outer surface so carve opens outward.
                float rCenter = rOuter - halfD;
                float theta = phase + helixTurns * 2f * MathF.PI * t;

                // Tangential offset for rectangular width (two beam rails).
                float tx = -MathF.Sin(theta);
                float ty = MathF.Cos(theta);
                var center = new Vector3(rCenter * MathF.Cos(theta), rCenter * MathF.Sin(theta), z);
                var a = center + new Vector3(tx, ty, 0) * (halfW * 0.55f);
                var b = center - new Vector3(tx, ty, 0) * (halfW * 0.55f);

                if (prevA is Vector3 pa)
                {
                    lat.AddBeam(pa, halfD, a, halfD, false);
                    lat.AddBeam(prevB!.Value, halfD, b, halfD, false);
                    // Fill rectangle core
                    lat.AddBeam(pa, halfD * 0.85f, b, halfD * 0.85f, false);
                }
                prevA = a;
                prevB = b;
            }
        }

        return new Voxels(lat);
    }

    /// <summary>
    /// Convenience: build channels from explicit Rao dimensions (no chamber bbox needed).
    /// </summary>
    public static Voxels BuildHelicalFromProfile(
        List<Vector2> gasProfile,
        int count = 48,
        float depthMm = 2.5f,
        float wallMm = 3.5f,
        float helixTurns = 2.6f,
        float channelWidthMm = 2.2f)
    {
        // Dummy chamber bbox via outer revolve for API symmetry — use profile path directly.
        count = Math.Clamp(count, 32, 64);
        wallMm = MathF.Max(wallMm, 3.0f);
        depthMm = Math.Clamp(depthMm, 1.2f, wallMm * 0.75f);
        List<Vector2> outer = ProfileHelper.GetOffsetProfile(gasProfile, wallMm);
        float z0 = outer[0].X;
        float z1 = outer[^1].X;
        float L = MathF.Max(z1 - z0, 40f);
        float halfW = Math.Clamp(channelWidthMm * 0.5f, 0.7f, depthMm * 0.9f);
        float halfD = depthMm * 0.48f;

        var lat = new Lattice();
        const int samples = 110;
        for (int i = 0; i < count; i++)
        {
            float phase = 2f * MathF.PI * i / count;
            Vector3? prevA = null;
            Vector3? prevB = null;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                float z = z0 + t * L;
                float rOuter = ProfileHelper.RadiusAtZ(outer, z);
                float rCenter = rOuter - halfD;
                float theta = phase + helixTurns * 2f * MathF.PI * t;
                float tx = -MathF.Sin(theta);
                float ty = MathF.Cos(theta);
                var center = new Vector3(rCenter * MathF.Cos(theta), rCenter * MathF.Sin(theta), z);
                var a = center + new Vector3(tx, ty, 0) * (halfW * 0.55f);
                var b = center - new Vector3(tx, ty, 0) * (halfW * 0.55f);
                if (prevA is Vector3 pa)
                {
                    lat.AddBeam(pa, halfD, a, halfD, false);
                    lat.AddBeam(prevB!.Value, halfD, b, halfD, false);
                    lat.AddBeam(pa, halfD * 0.85f, b, halfD * 0.85f, false);
                }
                prevA = a;
                prevB = b;
            }
        }
        return new Voxels(lat);
    }

    private static List<Vector2> SynthesizeProfileFromBounds(BBox3 bb)
    {
        float z0 = bb.vecMin.Z;
        float z1 = bb.vecMax.Z;
        float rMax = 0.5f * MathF.Max(bb.vecMax.X - bb.vecMin.X, bb.vecMax.Y - bb.vecMin.Y);
        float rt = rMax * 0.42f;
        float re = rMax * 0.92f;
        return ProfileHelper.GetRaoProfile(rt, re, MathF.Max(z1 - z0, 100f), rMax * 0.85f);
    }
}

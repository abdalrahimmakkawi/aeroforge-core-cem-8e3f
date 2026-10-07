using System.Numerics;
using AeroForge.Core.Physics;
using PicoGK;

namespace AeroForge.Core.Modules;

/// <summary>
/// Conformal helical regenerative cooling channels carved into the outer wall.
/// </summary>
public static class CoolingBuilder
{
    /// <summary>
    /// Sweep 32–64 helical rectangular channels along the outer wall surface.
    /// Pass result to <c>voxEngineWall.BoolSubtract(voxChannels)</c>.
    /// </summary>
    public static Voxels BuildConformalChannels(
        List<Vector2> innerProfile,
        int channelCount = 48,
        float depthMm = 2.5f,
        float pitch = 2.6f,
        float wallMm = 3.5f,
        float channelWidthMm = 2.2f)
    {
        channelCount = Math.Clamp(channelCount, 32, 64);
        wallMm = MathF.Max(wallMm, 3.0f);
        depthMm = Math.Clamp(depthMm, 1.2f, wallMm * 0.72f);
        pitch = MathF.Max(pitch, 1.8f); // helix turns along length

        List<Vector2> outer = RaoContour.OffsetProfile(innerProfile, wallMm);
        float z0 = outer[0].X;
        float z1 = outer[^1].X;
        float L = MathF.Max(z1 - z0, 40f);

        float halfW = Math.Clamp(channelWidthMm * 0.5f, 0.7f, depthMm * 0.9f);
        float halfD = depthMm * 0.48f;

        var lat = new Lattice();
        const int samples = 100;
        for (int i = 0; i < channelCount; i++)
        {
            float phase = 2f * MathF.PI * i / channelCount;
            Vector3? prevA = null;
            Vector3? prevB = null;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                float z = z0 + t * L * 0.98f + L * 0.01f;
                float rOuter = RaoContour.RadiusAtZ(outer, z);
                float rCenter = rOuter - halfD;
                float theta = phase + pitch * 2f * MathF.PI * t;

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
}

using System.Numerics;
using PicoGK;

namespace AeroForge.Geometry;

/// <summary>Conformal helical regenerative cooling channel lattices.</summary>
public static class ChannelHelper
{
    /// <summary>
    /// Helical cooling channels that track the gas-path profile at mid-wall depth.
    /// Channel width is clamped so beams stay enclosed inside the jacket.
    /// </summary>
    public static Voxels CreateHelicalChannels(
        List<Vector2> gasPathProfile,
        int count = 48,
        float channelWidth = 2.0f,
        float wallMm = 3.5f,
        float helixTurns = 2.6f,
        float wallDepthFraction = 0.50f)
    {
        if (gasPathProfile == null || gasPathProfile.Count < 2)
            throw new ArgumentException("gasPathProfile required");

        count = Math.Clamp(count, 12, 64);
        float tubeR = Math.Min(Math.Max(channelWidth * 0.5f, 0.6f), wallMm * 0.32f);
        float z0 = gasPathProfile[0].X;
        float z1 = gasPathProfile[^1].X;
        float L = MathF.Max(z1 - z0, 1f);

        var lat = new Lattice();
        const int samples = 96;
        for (int i = 0; i < count; i++)
        {
            float phase = 2f * MathF.PI * i / count;
            Vector3? prev = null;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                float z = z0 + t * L;
                float rGas = ProfileHelper.RadiusAtZ(gasPathProfile, z);
                float theta = phase + helixTurns * 2f * MathF.PI * t;
                float rr = rGas + wallMm * wallDepthFraction;
                var p = new Vector3(rr * MathF.Cos(theta), rr * MathF.Sin(theta), z);
                if (prev is Vector3 pv)
                    lat.AddBeam(pv, tubeR, p, tubeR, false);
                prev = p;
            }
        }
        return new Voxels(lat);
    }
}

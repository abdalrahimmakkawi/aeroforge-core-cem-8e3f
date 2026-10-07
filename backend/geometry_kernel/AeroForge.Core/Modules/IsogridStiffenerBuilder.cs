using System.Numerics;
using AeroForge.Core.Physics;
using PicoGK;

namespace AeroForge.Core.Modules;

/// <summary>
/// External iso-grid / triangular stiffener cage fused to the nozzle outer wall
/// to carry axial thrust loads through the thin regenerative jacket.
/// </summary>
public static class IsogridStiffenerBuilder
{
    public static Voxels GenerateGrid(
        List<Vector2> gasProfile,
        float wallMm = 3.2f,
        float ribThickness = 1.35f,
        int axialCount = 18,
        int hoopCount = 9,
        bool triangular = true)
    {
        wallMm = MathF.Max(wallMm, 3.0f);
        ribThickness = Math.Clamp(ribThickness, 0.9f, 2.4f);
        axialCount = Math.Clamp(axialCount, 12, 24);
        hoopCount = Math.Clamp(hoopCount, 6, 14);

        List<Vector2> outer = RaoContour.OffsetProfile(gasProfile, wallMm);
        float z0 = outer[0].X + 8f;
        float z1 = outer[^1].X - 10f;
        float L = MathF.Max(z1 - z0, 40f);
        float beamR = ribThickness * 0.52f;

        var lat = new Lattice();

        // Axial stringers
        const int azSamples = 36;
        for (int i = 0; i < axialCount; i++)
        {
            float phase = 2f * MathF.PI * i / axialCount;
            Vector3? prev = null;
            for (int s = 0; s <= azSamples; s++)
            {
                float t = s / (float)azSamples;
                float z = z0 + t * L;
                float r = RaoContour.RadiusAtZ(outer, z) + beamR * 0.65f;
                var p = new Vector3(r * MathF.Cos(phase), r * MathF.Sin(phase), z);
                if (prev is Vector3 pv)
                    lat.AddBeam(pv, beamR, p, beamR, false);
                prev = p;
            }
        }

        // Hoop rings
        for (int h = 0; h < hoopCount; h++)
        {
            float t = (h + 0.5f) / hoopCount;
            float z = z0 + t * L;
            float r = RaoContour.RadiusAtZ(outer, z) + beamR * 0.6f;
            Vector3? prev = null;
            const int n = 48;
            for (int i = 0; i <= n; i++)
            {
                float a = 2f * MathF.PI * i / n;
                var p = new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), z);
                if (prev is Vector3 pv)
                    lat.AddBeam(pv, beamR * 0.9f, p, beamR * 0.9f, true);
                prev = p;
            }
        }

        // Triangular / helical diagonals (biomimetic iso-grid)
        if (triangular)
        {
            int diagCount = axialCount;
            for (int i = 0; i < diagCount; i++)
            {
                float phase = 2f * MathF.PI * i / diagCount;
                Vector3? prev = null;
                for (int s = 0; s <= azSamples; s++)
                {
                    float t = s / (float)azSamples;
                    float z = z0 + t * L;
                    float twist = phase + t * MathF.PI * 1.15f; // ~0.5 turn diagonal
                    float r = RaoContour.RadiusAtZ(outer, z) + beamR * 0.7f;
                    var p = new Vector3(r * MathF.Cos(twist), r * MathF.Sin(twist), z);
                    if (prev is Vector3 pv)
                        lat.AddBeam(pv, beamR * 0.75f, p, beamR * 0.75f, false);
                    prev = p;
                }
            }
            // Opposite-hand diagonals
            for (int i = 0; i < diagCount; i++)
            {
                float phase = 2f * MathF.PI * i / diagCount + MathF.PI / diagCount;
                Vector3? prev = null;
                for (int s = 0; s <= azSamples; s++)
                {
                    float t = s / (float)azSamples;
                    float z = z0 + t * L;
                    float twist = phase - t * MathF.PI * 1.15f;
                    float r = RaoContour.RadiusAtZ(outer, z) + beamR * 0.7f;
                    var p = new Vector3(r * MathF.Cos(twist), r * MathF.Sin(twist), z);
                    if (prev is Vector3 pv)
                        lat.AddBeam(pv, beamR * 0.7f, p, beamR * 0.7f, false);
                    prev = p;
                }
            }
        }

        return new Voxels(lat);
    }
}

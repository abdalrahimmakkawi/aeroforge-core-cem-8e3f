using System.Numerics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Geometry;

/// <summary>Injector head plate and bolted mounting flange.</summary>
public static class FlangeHelper
{
    /// <summary>
    /// Injector face + annular mounting flange with bolt bosses, fused as one body.
    /// </summary>
    public static Voxels CreateInjectorCap(float rInjector, int boltCount = 8, float thickness = 12f)
    {
        rInjector = MathF.Max(rInjector, 20f);
        boltCount = Math.Clamp(boltCount, 4, 16);
        thickness = Math.Clamp(thickness, 8f, 20f);

        float flangeOuter = rInjector + 18f;
        float flangeInner = MathF.Max(rInjector * 0.35f, 8f);

        // Face plate (disk with central dome clearance bore).
        var plate = new BasePipe(
            new LocalFrame(new Vector3(0, 0, -thickness * 0.15f)),
            thickness,
            flangeInner,
            flangeOuter);
        plate.SetLengthSteps(24);
        plate.SetPolarSteps(72);
        Voxels vox = plate.voxConstruct();

        // Outer bolt circle bosses.
        float boltCircle = rInjector + 10f;
        float bossR = 4.5f;
        var bosses = new Lattice();
        for (int i = 0; i < boltCount; i++)
        {
            float a = 2f * MathF.PI * i / boltCount;
            var c = new Vector3(boltCircle * MathF.Cos(a), boltCircle * MathF.Sin(a), thickness * 0.35f);
            var c0 = new Vector3(c.X, c.Y, -2f);
            var c1 = new Vector3(c.X, c.Y, thickness + 2f);
            bosses.AddBeam(c0, bossR, c1, bossR, true);
        }
        vox = vox.voxBoolAdd(new Voxels(bosses));

        // Bolt holes (subtract).
        var holes = new Lattice();
        float holeR = 2.2f;
        for (int i = 0; i < boltCount; i++)
        {
            float a = 2f * MathF.PI * i / boltCount;
            var c0 = new Vector3(boltCircle * MathF.Cos(a), boltCircle * MathF.Sin(a), -4f);
            var c1 = new Vector3(c0.X, c0.Y, thickness + 6f);
            holes.AddBeam(c0, holeR, c1, holeR, true);
        }
        vox = vox.voxBoolSubtract(new Voxels(holes));

        // Central injector orifice cluster (simplified showerhead).
        var orifices = new Lattice();
        int rings = 3;
        for (int ring = 1; ring <= rings; ring++)
        {
            float rr = flangeInner + (rInjector - flangeInner) * ring / (rings + 1f);
            int n = 6 * ring;
            for (int i = 0; i < n; i++)
            {
                float a = 2f * MathF.PI * i / n;
                var c0 = new Vector3(rr * MathF.Cos(a), rr * MathF.Sin(a), -3f);
                var c1 = new Vector3(c0.X, c0.Y, thickness + 4f);
                orifices.AddBeam(c0, 1.1f, c1, 1.1f, true);
            }
        }
        vox = vox.voxBoolSubtract(new Voxels(orifices));

        return vox;
    }
}

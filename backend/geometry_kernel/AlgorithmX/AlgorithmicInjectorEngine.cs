using System.Numerics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AlgorithmX.Core;

/// <summary>
/// High-density co-axial swirler faceplate with explicit LOX (upper) and CH₄ (lower)
/// plenums separated by an internal barrier.
/// </summary>
public static class AlgorithmicInjectorEngine
{
    public static Voxels GenerateSwirlers(
        int elementCount = 72,
        float faceRadius = 82f,
        float faceZ = 0f,
        int boltCount = 12)
    {
        elementCount = Math.Clamp(elementCount, 60, 120);
        faceRadius = MathF.Max(faceRadius, 40f);

        float plateThick = 16f;
        float flangeOuter = faceRadius + 18f;
        float igniterR = MathF.Max(faceRadius * 0.12f, 8f);

        // —— Lower CH₄ manifold plenum (below barrier) ——
        var ch4 = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ - 2f)),
            plateThick * 0.45f,
            igniterR * 0.8f,
            faceRadius * 0.92f);
        ch4.SetLengthSteps(20);
        ch4.SetPolarSteps(72);
        Voxels vox = ch4.voxConstruct();

        // —— Internal barrier wall ——
        var barrier = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ + plateThick * 0.38f)),
            2.2f,
            igniterR * 0.7f,
            faceRadius * 0.95f);
        barrier.SetLengthSteps(10);
        barrier.SetPolarSteps(64);
        vox.BoolAdd(barrier.voxConstruct());

        // —— Upper LOX plenum (above barrier) ——
        var lox = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ + plateThick * 0.48f)),
            plateThick * 0.42f,
            igniterR * 0.75f,
            faceRadius * 0.90f);
        lox.SetLengthSteps(18);
        lox.SetPolarSteps(72);
        vox.BoolAdd(lox.voxConstruct());

        // Outer mounting flange + bolt circle
        var flange = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ)),
            plateThick * 0.7f,
            faceRadius * 0.88f,
            flangeOuter);
        flange.SetLengthSteps(16);
        flange.SetPolarSteps(72);
        vox.BoolAdd(flange.voxConstruct());

        var bosses = new Lattice();
        var holes = new Lattice();
        float boltCircle = faceRadius + 10f;
        for (int i = 0; i < boltCount; i++)
        {
            float a = 2f * MathF.PI * i / boltCount;
            var c0 = new Vector3(boltCircle * MathF.Cos(a), boltCircle * MathF.Sin(a), faceZ - 3f);
            var c1 = new Vector3(c0.X, c0.Y, faceZ + plateThick + 2f);
            bosses.AddBeam(c0, 4.8f, c1, 4.8f, true);
            holes.AddBeam(c0, 2.2f, c1, 2.2f, true);
        }
        vox.BoolAdd(new Voxels(bosses));
        vox.BoolSubtract(new Voxels(holes));

        // Central igniter tube through both plenums
        var ign = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ - 4f)),
            plateThick + 12f,
            0f,
            igniterR);
        ign.SetLengthSteps(20);
        ign.SetPolarSteps(36);
        Voxels ignSolid = ign.voxConstruct();
        var ignBore = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ - 6f)),
            plateThick + 16f,
            0f,
            igniterR * 0.45f);
        ignBore.SetLengthSteps(16);
        ignBore.SetPolarSteps(28);
        ignSolid.BoolSubtract(ignBore.voxConstruct());
        vox.BoolAdd(ignSolid);

        // Nested co-axial swirler array (60–120)
        vox.BoolAdd(BuildSwirlerArray(faceRadius, igniterR, faceZ, plateThick, elementCount));

        // Hollow the plenums (leave outer walls + barrier + swirler posts)
        CarvePlenumCavity(vox, faceZ + 1.5f, plateThick * 0.32f, igniterR * 1.1f, faceRadius * 0.78f);
        CarvePlenumCavity(vox, faceZ + plateThick * 0.55f, plateThick * 0.28f, igniterR * 1.1f, faceRadius * 0.76f);

        return vox;
    }

    private static void CarvePlenumCavity(Voxels vox, float z, float h, float rIn, float rOut)
    {
        var cav = new BasePipe(new LocalFrame(new Vector3(0, 0, z)), h, rIn, rOut);
        cav.SetLengthSteps(12);
        cav.SetPolarSteps(48);
        vox.BoolSubtract(cav.voxConstruct());
    }

    private static Voxels BuildSwirlerArray(float faceR, float igniterR, float faceZ, float thick, int count)
    {
        var lat = new Lattice();
        int rings = count >= 90 ? 3 : 2;
        int placed = 0;
        for (int ring = 1; ring <= rings; ring++)
        {
            float rr = igniterR * 1.6f + (faceR * 0.78f - igniterR * 1.6f) * ring / (rings + 0.2f);
            int n = Math.Max(8, count / rings + (ring == rings ? count % rings : 0));
            for (int i = 0; i < n && placed < count; i++, placed++)
            {
                float a = 2f * MathF.PI * i / n + ring * 0.19f;
                var c = new Vector3(rr * MathF.Cos(a), rr * MathF.Sin(a), faceZ + thick * 0.4f);
                var o0 = c + new Vector3(0, 0, -thick * 0.55f);
                var o1 = c + new Vector3(0, 0, thick * 0.7f);
                // Outer coax + inner post
                lat.AddBeam(o0, 2.4f, o1, 2.4f, true);
                lat.AddBeam(o0, 1.05f, o1, 1.05f, true);
                // Tangential swirl vanes
                for (int s = 0; s < 4; s++)
                {
                    float sa = a + s * MathF.PI * 0.5f;
                    var t0 = c + new Vector3(1.6f * MathF.Cos(sa), 1.6f * MathF.Sin(sa), -1f);
                    var t1 = c + new Vector3(3.2f * MathF.Cos(sa + 0.4f), 3.2f * MathF.Sin(sa + 0.4f), 2.5f);
                    lat.AddBeam(t0, 0.5f, t1, 0.5f, true);
                }
            }
        }

        Voxels solid = new(lat);
        // Through-bores for propellant
        var bores = new Lattice();
        placed = 0;
        for (int ring = 1; ring <= rings; ring++)
        {
            float rr = igniterR * 1.6f + (faceR * 0.78f - igniterR * 1.6f) * ring / (rings + 0.2f);
            int n = Math.Max(8, count / rings);
            for (int i = 0; i < n && placed < count; i++, placed++)
            {
                float a = 2f * MathF.PI * i / n + ring * 0.19f;
                var c0 = new Vector3(rr * MathF.Cos(a), rr * MathF.Sin(a), faceZ - thick);
                var c1 = new Vector3(c0.X, c0.Y, faceZ + thick * 1.5f);
                bores.AddBeam(c0, 0.65f, c1, 0.65f, true);
            }
        }
        solid.BoolSubtract(new Voxels(bores));
        return solid;
    }
}

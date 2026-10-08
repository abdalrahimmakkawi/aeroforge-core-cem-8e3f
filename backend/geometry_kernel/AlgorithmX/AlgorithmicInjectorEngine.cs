using System.Numerics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AlgorithmX.Core;

/// <summary>
/// Dense co-axial swirler faceplate with LOX (upper) and fuel (lower) plenums.
/// Target: 50+ detailed swirler elements readable in section cut.
/// </summary>
public static class AlgorithmicInjectorEngine
{
    public static Voxels GenerateSwirlers(
        int elementCount = 72,
        float faceRadius = 82f,
        float faceZ = 0f,
        int boltCount = 14)
    {
        elementCount = Math.Clamp(elementCount, 50, 120);
        faceRadius = MathF.Max(faceRadius, 40f);

        float plateThick = 18f;
        float flangeOuter = faceRadius + 20f;
        float igniterR = MathF.Max(faceRadius * 0.11f, 7.5f);

        // —— Lower fuel manifold plenum ——
        var fuel = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ - 2f)),
            plateThick * 0.45f,
            igniterR * 0.75f,
            faceRadius * 0.93f);
        fuel.SetLengthSteps(22);
        fuel.SetPolarSteps(80);
        Voxels vox = fuel.voxConstruct();

        // —— Internal barrier wall ——
        var barrier = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ + plateThick * 0.40f)),
            2.0f,
            igniterR * 0.65f,
            faceRadius * 0.96f);
        barrier.SetLengthSteps(10);
        barrier.SetPolarSteps(72);
        vox.BoolAdd(barrier.voxConstruct());

        // —— Upper LOX plenum ——
        var lox = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ + plateThick * 0.50f)),
            plateThick * 0.40f,
            igniterR * 0.7f,
            faceRadius * 0.91f);
        lox.SetLengthSteps(20);
        lox.SetPolarSteps(80);
        vox.BoolAdd(lox.voxConstruct());

        // Outer mounting flange + bolt circle
        var flange = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ)),
            plateThick * 0.72f,
            faceRadius * 0.88f,
            flangeOuter);
        flange.SetLengthSteps(18);
        flange.SetPolarSteps(80);
        vox.BoolAdd(flange.voxConstruct());

        var bosses = new Lattice();
        var holes = new Lattice();
        float boltCircle = faceRadius + 11f;
        for (int i = 0; i < boltCount; i++)
        {
            float a = 2f * MathF.PI * i / boltCount;
            var c0 = new Vector3(boltCircle * MathF.Cos(a), boltCircle * MathF.Sin(a), faceZ - 3f);
            var c1 = new Vector3(c0.X, c0.Y, faceZ + plateThick + 2f);
            bosses.AddBeam(c0, 4.6f, c1, 4.6f, true);
            holes.AddBeam(c0, 2.1f, c1, 2.1f, true);
        }
        vox.BoolAdd(new Voxels(bosses));
        vox.BoolSubtract(new Voxels(holes));

        // Central igniter tube through both plenums
        var ign = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ - 4f)),
            plateThick + 14f,
            0f,
            igniterR);
        ign.SetLengthSteps(22);
        ign.SetPolarSteps(40);
        Voxels ignSolid = ign.voxConstruct();
        var ignBore = new BasePipe(
            new LocalFrame(new Vector3(0, 0, faceZ - 6f)),
            plateThick + 18f,
            0f,
            igniterR * 0.42f);
        ignBore.SetLengthSteps(16);
        ignBore.SetPolarSteps(32);
        ignSolid.BoolSubtract(ignBore.voxConstruct());
        vox.BoolAdd(ignSolid);

        vox.BoolAdd(BuildSwirlerArray(faceRadius, igniterR, faceZ, plateThick, elementCount));

        CarvePlenumCavity(vox, faceZ + 1.5f, plateThick * 0.30f, igniterR * 1.15f, faceRadius * 0.80f);
        CarvePlenumCavity(vox, faceZ + plateThick * 0.55f, plateThick * 0.26f, igniterR * 1.15f, faceRadius * 0.78f);

        return vox;
    }

    private static void CarvePlenumCavity(Voxels vox, float z, float h, float rIn, float rOut)
    {
        var cav = new BasePipe(new LocalFrame(new Vector3(0, 0, z)), h, rIn, rOut);
        cav.SetLengthSteps(14);
        cav.SetPolarSteps(56);
        vox.BoolSubtract(cav.voxConstruct());
    }

    private static Voxels BuildSwirlerArray(float faceR, float igniterR, float faceZ, float thick, int count)
    {
        var lat = new Lattice();
        int rings = count >= 80 ? 4 : (count >= 54 ? 3 : 2);
        int placed = 0;
        int[] perRing = Distribute(count, rings);

        for (int ring = 1; ring <= rings; ring++)
        {
            float t = ring / (rings + 0.15f);
            float rr = igniterR * 1.55f + (faceR * 0.80f - igniterR * 1.55f) * t;
            int n = perRing[ring - 1];
            for (int i = 0; i < n && placed < count; i++, placed++)
            {
                float a = 2f * MathF.PI * i / n + ring * 0.21f;
                var c = new Vector3(rr * MathF.Cos(a), rr * MathF.Sin(a), faceZ + thick * 0.42f);
                var o0 = c + new Vector3(0, 0, -thick * 0.58f);
                var o1 = c + new Vector3(0, 0, thick * 0.72f);
                // Outer coax sleeve + inner pintle post
                lat.AddBeam(o0, 2.55f, o1, 2.55f, true);
                lat.AddBeam(o0, 1.0f, o1, 1.0f, true);
                // Tangential swirl vanes (6) — readable in section cut
                for (int s = 0; s < 6; s++)
                {
                    float sa = a + s * (MathF.PI / 3f);
                    var t0 = c + new Vector3(1.5f * MathF.Cos(sa), 1.5f * MathF.Sin(sa), -1.2f);
                    var t1 = c + new Vector3(3.4f * MathF.Cos(sa + 0.45f), 3.4f * MathF.Sin(sa + 0.45f), 2.8f);
                    lat.AddBeam(t0, 0.45f, t1, 0.42f, true);
                }
            }
        }

        Voxels solid = new(lat);
        var bores = new Lattice();
        placed = 0;
        for (int ring = 1; ring <= rings; ring++)
        {
            float t = ring / (rings + 0.15f);
            float rr = igniterR * 1.55f + (faceR * 0.80f - igniterR * 1.55f) * t;
            int n = perRing[ring - 1];
            for (int i = 0; i < n && placed < count; i++, placed++)
            {
                float a = 2f * MathF.PI * i / n + ring * 0.21f;
                var c0 = new Vector3(rr * MathF.Cos(a), rr * MathF.Sin(a), faceZ - thick);
                var c1 = new Vector3(c0.X, c0.Y, faceZ + thick * 1.55f);
                bores.AddBeam(c0, 0.55f, c1, 0.55f, true);
            }
        }
        solid.BoolSubtract(new Voxels(bores));
        return solid;
    }

    private static int[] Distribute(int total, int rings)
    {
        var arr = new int[rings];
        int baseN = total / rings;
        int rem = total % rings;
        for (int i = 0; i < rings; i++)
            arr[i] = Math.Max(8, baseN + (i < rem ? 1 : 0));
        return arr;
    }
}

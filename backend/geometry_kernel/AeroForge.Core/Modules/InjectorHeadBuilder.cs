using System.Numerics;
using AeroForge.Core.Physics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Core.Modules;

/// <summary>
/// Co-axial swirler injector head: domed faceplate, bolt flange, igniter stub,
/// radial swirler element array, and near-wall film-cooling orifices.
/// </summary>
public static class InjectorHeadBuilder
{
    public static Voxels GenerateHead(GasDynamicsResult physics, int elementCount = 36, int boltCount = 12)
    {
        float rInj = MathF.Max(physics.rInjector, 30f);
        elementCount = Math.Clamp(elementCount, 18, 48);
        boltCount = Math.Clamp(boltCount, 8, 16);

        float thickness = 14f;
        float flangeOuter = rInj + 20f;
        float flangeInner = MathF.Max(rInj * 0.22f, 10f);

        // Domed faceplate (thick annular disk + shallow dome lattice).
        var plate = new BasePipe(
            new LocalFrame(new Vector3(0, 0, -thickness * 0.2f)),
            thickness,
            flangeInner * 0.55f,
            flangeOuter);
        plate.SetLengthSteps(28);
        plate.SetPolarSteps(80);
        Voxels vox = plate.voxConstruct();

        // Dome crown over the faceplate.
        var dome = new Lattice();
        float domeR = rInj * 0.92f;
        for (int i = 0; i < 48; i++)
        {
            float a0 = 2f * MathF.PI * i / 48f;
            float a1 = 2f * MathF.PI * (i + 1) / 48f;
            var p0 = new Vector3(domeR * 0.55f * MathF.Cos(a0), domeR * 0.55f * MathF.Sin(a0), thickness * 0.55f);
            var p1 = new Vector3(domeR * 0.55f * MathF.Cos(a1), domeR * 0.55f * MathF.Sin(a1), thickness * 0.55f);
            var apex = new Vector3(0, 0, thickness * 1.05f);
            dome.AddBeam(p0, 3.2f, apex, 2.4f, true);
            dome.AddBeam(p0, 2.8f, p1, 2.8f, true);
        }
        vox.BoolAdd(new Voxels(dome));

        // Outer bolt circle bosses + through holes.
        float boltCircle = rInj + 11f;
        var bosses = new Lattice();
        var holes = new Lattice();
        for (int i = 0; i < boltCount; i++)
        {
            float a = 2f * MathF.PI * i / boltCount;
            var c0 = new Vector3(boltCircle * MathF.Cos(a), boltCircle * MathF.Sin(a), -3f);
            var c1 = new Vector3(c0.X, c0.Y, thickness + 3f);
            bosses.AddBeam(c0, 5.0f, c1, 5.0f, true);
            holes.AddBeam(c0 + new Vector3(0, 0, -2f), 2.3f, c1 + new Vector3(0, 0, 2f), 2.3f, true);
        }
        vox.BoolAdd(new Voxels(bosses));
        vox.BoolSubtract(new Voxels(holes));

        // Central igniter port thread stub (hollow boss).
        var igniter = new BasePipe(
            new LocalFrame(new Vector3(0, 0, -2f)),
            thickness + 10f,
            0f,
            MathF.Max(flangeInner * 0.45f, 6f));
        igniter.SetLengthSteps(20);
        igniter.SetPolarSteps(36);
        Voxels ignSolid = igniter.voxConstruct();
        var ignBore = new BasePipe(
            new LocalFrame(new Vector3(0, 0, -4f)),
            thickness + 14f,
            0f,
            MathF.Max(flangeInner * 0.22f, 3.2f));
        ignBore.SetLengthSteps(16);
        ignBore.SetPolarSteps(28);
        ignSolid.BoolSubtract(ignBore.voxConstruct());
        vox.BoolAdd(ignSolid);

        // Co-axial swirler elements — 2D radial pattern embedded in faceplate.
        vox.BoolAdd(BuildCoaxialSwirlers(rInj, flangeInner, thickness, elementCount));

        // Film-cooling orifices near chamber wall boundary (ring of small bores).
        float filmR = rInj * 0.88f;
        int filmN = Math.Max(24, elementCount);
        var film = new Lattice();
        for (int i = 0; i < filmN; i++)
        {
            float a = 2f * MathF.PI * i / filmN;
            var c0 = new Vector3(filmR * MathF.Cos(a), filmR * MathF.Sin(a), -4f);
            var c1 = new Vector3(c0.X, c0.Y, thickness + 5f);
            film.AddBeam(c0, 0.85f, c1, 0.85f, true);
        }
        vox.BoolSubtract(new Voxels(film));

        return vox;
    }

    private static Voxels BuildCoaxialSwirlers(float rInj, float flangeInner, float thickness, int count)
    {
        var lat = new Lattice();
        // Two radial rings of coaxial elements.
        int rings = 2;
        int placed = 0;
        for (int ring = 1; ring <= rings; ring++)
        {
            float rr = flangeInner + (rInj * 0.78f - flangeInner) * ring / (rings + 0.35f);
            int n = Math.Max(6, count / rings + (ring == rings ? count % rings : 0));
            for (int i = 0; i < n && placed < count; i++, placed++)
            {
                float a = 2f * MathF.PI * i / n + (ring * 0.17f);
                var center = new Vector3(rr * MathF.Cos(a), rr * MathF.Sin(a), thickness * 0.35f);
                // Outer coax shell
                var o0 = center + new Vector3(0, 0, -thickness * 0.55f);
                var o1 = center + new Vector3(0, 0, thickness * 0.65f);
                lat.AddBeam(o0, 2.6f, o1, 2.6f, true);
                // Inner coax post
                lat.AddBeam(o0, 1.15f, o1, 1.15f, true);
                // Tangential swirl slots (4 small beams)
                for (int s = 0; s < 4; s++)
                {
                    float sa = a + s * MathF.PI * 0.5f;
                    var t0 = center + new Vector3(1.8f * MathF.Cos(sa), 1.8f * MathF.Sin(sa), -1f);
                    var t1 = center + new Vector3(3.4f * MathF.Cos(sa + 0.35f), 3.4f * MathF.Sin(sa + 0.35f), 2f);
                    lat.AddBeam(t0, 0.55f, t1, 0.55f, true);
                }
            }
        }

        Voxels solid = new(lat);
        // Bore through each coax to leave annular passage (approximate by subtracting thin center).
        var bores = new Lattice();
        placed = 0;
        for (int ring = 1; ring <= rings; ring++)
        {
            float rr = flangeInner + (rInj * 0.78f - flangeInner) * ring / (rings + 0.35f);
            int n = Math.Max(6, count / rings);
            for (int i = 0; i < n && placed < count; i++, placed++)
            {
                float a = 2f * MathF.PI * i / n + (ring * 0.17f);
                var c0 = new Vector3(rr * MathF.Cos(a), rr * MathF.Sin(a), -thickness);
                var c1 = new Vector3(c0.X, c0.Y, thickness * 1.4f);
                bores.AddBeam(c0, 0.7f, c1, 0.7f, true);
            }
        }
        solid.BoolSubtract(new Voxels(bores));
        return solid;
    }
}

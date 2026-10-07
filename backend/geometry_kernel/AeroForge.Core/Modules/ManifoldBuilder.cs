using System.Numerics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Core.Modules;

/// <summary>
/// Upper / lower toroidal propellant distribution rings with pipe stubs and fittings.
/// </summary>
public static class ManifoldBuilder
{
    /// <summary>
    /// Build inlet (injector) and exit toroids with radial NPT/AN-style pipe ports.
    /// </summary>
    public static Voxels BuildRings(
        float rInjector,
        float rExit,
        float lengthMm = 280f,
        float wallMm = 3.5f,
        float pipeDiameterMm = 18f)
    {
        float rTop = MathF.Max(rInjector, 40f) + wallMm * 0.4f;
        float rBot = MathF.Max(rExit, rTop * 0.85f) + wallMm * 0.35f;
        float pipeR = Math.Clamp(pipeDiameterMm * 0.5f, 5f, 14f);
        float ringPipeR = Math.Clamp(pipeR * 0.72f, 6f, 12f);

        float zTop = MathF.Max(lengthMm * 0.07f, 12f);
        float zBot = MathF.Max(lengthMm - lengthMm * 0.06f, zTop + 40f);

        Voxels vox = CreateToroid(rTop, ringPipeR, zTop);
        vox.BoolAdd(CreateToroid(rBot, ringPipeR * 0.9f, zBot));

        vox.BoolAdd(CreatePipeStub(rTop, zTop, pipeR, yawDeg: 0f, lengthMm: 28f));
        vox.BoolAdd(CreatePipeStub(rTop, zTop, pipeR * 0.9f, yawDeg: 180f, lengthMm: 24f));
        vox.BoolAdd(CreatePipeStub(rBot, zBot, pipeR * 0.85f, yawDeg: 90f, lengthMm: 26f));
        vox.BoolAdd(CreatePipeStub(rBot, zBot, pipeR * 0.8f, yawDeg: 270f, lengthMm: 22f));

        vox.BoolAdd(CreateHexPort(rTop + 28f, zTop, yawDeg: 0f, size: pipeR * 1.15f));
        vox.BoolAdd(CreateHexPort(rTop + 24f, zTop, yawDeg: 180f, size: pipeR));
        vox.BoolAdd(CreateHexPort(rBot + 26f, zBot, yawDeg: 90f, size: pipeR * 0.95f));
        vox.BoolAdd(CreateHexPort(rBot + 22f, zBot, yawDeg: 270f, size: pipeR * 0.9f));

        return vox;
    }

    private static Voxels CreateToroid(float majorR, float pipeR, float z)
    {
        var ring = new BaseRing(new LocalFrame(new Vector3(0, 0, z)), majorR, pipeR);
        ring.SetPolarSteps(64);
        ring.SetRadialSteps(32);
        return ring.voxConstruct();
    }

    private static Voxels CreatePipeStub(float majorR, float z, float pipeR, float yawDeg, float lengthMm)
    {
        float yaw = yawDeg * MathF.PI / 180f;
        var dir = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        var origin = new Vector3(majorR * MathF.Cos(yaw), majorR * MathF.Sin(yaw), z);
        var zAxis = Vector3.Normalize(dir);
        var frame = new LocalFrame(origin - zAxis * 2f, zAxis);
        var pipe = new BasePipe(frame, lengthMm, 0f, pipeR);
        pipe.SetLengthSteps(20);
        pipe.SetPolarSteps(28);
        Voxels solid = pipe.voxConstruct();
        var bore = new BasePipe(frame, lengthMm + 4f, 0f, MathF.Max(pipeR * 0.55f, 2.5f));
        bore.SetLengthSteps(16);
        bore.SetPolarSteps(24);
        solid.BoolSubtract(bore.voxConstruct());
        return solid;
    }

    private static Voxels CreateHexPort(float radialDist, float z, float yawDeg, float size)
    {
        float yaw = yawDeg * MathF.PI / 180f;
        var center = new Vector3(radialDist * MathF.Cos(yaw), radialDist * MathF.Sin(yaw), z);
        var lat = new Lattice();
        float R = MathF.Max(size, 4f);
        float h = R * 1.1f;
        for (int i = 0; i < 6; i++)
        {
            float a0 = yaw + 2f * MathF.PI * i / 6f;
            float a1 = yaw + 2f * MathF.PI * (i + 1) / 6f;
            var p0 = center + new Vector3(R * MathF.Cos(a0), R * MathF.Sin(a0), -h * 0.3f);
            var p1 = center + new Vector3(R * MathF.Cos(a1), R * MathF.Sin(a1), -h * 0.3f);
            var q0 = center + new Vector3(R * MathF.Cos(a0), R * MathF.Sin(a0), h * 0.3f);
            var q1 = center + new Vector3(R * MathF.Cos(a1), R * MathF.Sin(a1), h * 0.3f);
            lat.AddBeam(p0, R * 0.35f, p1, R * 0.35f, true);
            lat.AddBeam(q0, R * 0.35f, q1, R * 0.35f, true);
            lat.AddBeam(p0, R * 0.3f, q0, R * 0.3f, true);
        }
        var inDir = Vector3.Normalize(new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0));
        var h0 = center - inDir * (R * 0.2f) - inDir * 2f;
        var h1 = center + inDir * (R * 1.2f);
        Voxels boss = new(lat);
        var hole = new Lattice();
        hole.AddBeam(h0, R * 0.28f, h1, R * 0.28f, true);
        boss.BoolSubtract(new Voxels(hole));
        return boss;
    }
}

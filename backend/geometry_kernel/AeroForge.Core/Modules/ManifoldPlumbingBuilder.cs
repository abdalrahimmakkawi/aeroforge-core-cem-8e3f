using System.Numerics;
using AeroForge.Core.Physics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Core.Modules;

/// <summary>
/// Toroidal propellant manifolds with bifurcated AN/NPT feed pipes and sensor bosses.
/// </summary>
public static class ManifoldPlumbingBuilder
{
    public static Voxels GenerateRingsAndPorts(GasDynamicsResult physics, float wallMm = 3.2f, float pipeDiameterMm = 18f)
    {
        float rTop = MathF.Max(physics.rInjector, 40f) + wallMm * 0.5f;
        float rBot = MathF.Max(physics.rExit, rTop * 0.85f) + wallMm * 0.4f;
        float length = MathF.Max(physics.LengthMm, 140f);
        float pipeR = Math.Clamp(pipeDiameterMm * 0.5f, 5f, 14f);
        float ringPipeR = Math.Clamp(pipeR * 0.75f, 6f, 12f);

        float zTop = MathF.Max(length * 0.06f, 14f);
        float zBot = MathF.Max(length - length * 0.05f, zTop + 50f);

        // Sculptured upper (fuel) + lower (oxidizer / coolant return) toroids.
        Voxels vox = CreateToroid(rTop, ringPipeR, zTop);
        vox.BoolAdd(CreateToroid(rBot, ringPipeR * 0.92f, zBot));

        // Dual bifurcated feed pipes at 45° with thick-walled fittings.
        vox.BoolAdd(CreateAngledFeed(rTop, zTop, pipeR, yawDeg: 45f, pitchDeg: 18f, lengthMm: 34f));
        vox.BoolAdd(CreateAngledFeed(rTop, zTop, pipeR * 0.92f, yawDeg: 225f, pitchDeg: 18f, lengthMm: 32f));
        vox.BoolAdd(CreateAngledFeed(rBot, zBot, pipeR * 0.88f, yawDeg: 135f, pitchDeg: -12f, lengthMm: 30f));
        vox.BoolAdd(CreateAngledFeed(rBot, zBot, pipeR * 0.85f, yawDeg: 315f, pitchDeg: -12f, lengthMm: 28f));

        // Hex AN/NPT style fitting ports at pipe tips.
        vox.BoolAdd(CreateHexFitting(rTop + 30f, zTop + 10f, yawDeg: 45f, size: pipeR * 1.2f));
        vox.BoolAdd(CreateHexFitting(rTop + 28f, zTop + 9f, yawDeg: 225f, size: pipeR));
        vox.BoolAdd(CreateHexFitting(rBot + 28f, zBot - 8f, yawDeg: 135f, size: pipeR));
        vox.BoolAdd(CreateHexFitting(rBot + 26f, zBot - 7f, yawDeg: 315f, size: pipeR * 0.95f));

        // Sensor boss stubs (P / T) along manifold bodies.
        vox.BoolAdd(CreateSensorBoss(rTop, zTop, yawDeg: 0f, size: 4.5f));
        vox.BoolAdd(CreateSensorBoss(rTop, zTop, yawDeg: 90f, size: 4.2f));
        vox.BoolAdd(CreateSensorBoss(rBot, zBot, yawDeg: 45f, size: 4.0f));
        vox.BoolAdd(CreateSensorBoss(rBot, zBot, yawDeg: 180f, size: 4.0f));

        return vox;
    }

    private static Voxels CreateToroid(float majorR, float pipeR, float z)
    {
        var ring = new BaseRing(new LocalFrame(new Vector3(0, 0, z)), majorR, pipeR);
        ring.SetPolarSteps(72);
        ring.SetRadialSteps(36);
        return ring.voxConstruct();
    }

    private static Voxels CreateAngledFeed(
        float majorR, float z, float pipeR, float yawDeg, float pitchDeg, float lengthMm)
    {
        float yaw = yawDeg * MathF.PI / 180f;
        float pitch = pitchDeg * MathF.PI / 180f;
        var dir = Vector3.Normalize(new Vector3(
            MathF.Cos(yaw) * MathF.Cos(pitch),
            MathF.Sin(yaw) * MathF.Cos(pitch),
            MathF.Sin(pitch)));
        var origin = new Vector3(majorR * MathF.Cos(yaw), majorR * MathF.Sin(yaw), z);
        var frame = new LocalFrame(origin - dir * 3f, dir);

        var pipe = new BasePipe(frame, lengthMm, 0f, pipeR);
        pipe.SetLengthSteps(24);
        pipe.SetPolarSteps(32);
        Voxels solid = pipe.voxConstruct();

        var bore = new BasePipe(frame, lengthMm + 5f, 0f, MathF.Max(pipeR * 0.52f, 2.8f));
        bore.SetLengthSteps(18);
        bore.SetPolarSteps(28);
        solid.BoolSubtract(bore.voxConstruct());
        return solid;
    }

    private static Voxels CreateHexFitting(float radialDist, float z, float yawDeg, float size)
    {
        float yaw = yawDeg * MathF.PI / 180f;
        var center = new Vector3(radialDist * MathF.Cos(yaw), radialDist * MathF.Sin(yaw), z);
        var lat = new Lattice();
        float R = MathF.Max(size, 4f);
        float h = R * 1.15f;
        for (int i = 0; i < 6; i++)
        {
            float a0 = yaw + 2f * MathF.PI * i / 6f;
            float a1 = yaw + 2f * MathF.PI * (i + 1) / 6f;
            var p0 = center + new Vector3(R * MathF.Cos(a0), R * MathF.Sin(a0), -h * 0.35f);
            var p1 = center + new Vector3(R * MathF.Cos(a1), R * MathF.Sin(a1), -h * 0.35f);
            var q0 = center + new Vector3(R * MathF.Cos(a0), R * MathF.Sin(a0), h * 0.35f);
            var q1 = center + new Vector3(R * MathF.Cos(a1), R * MathF.Sin(a1), h * 0.35f);
            lat.AddBeam(p0, R * 0.32f, p1, R * 0.32f, true);
            lat.AddBeam(q0, R * 0.32f, q1, R * 0.32f, true);
            lat.AddBeam(p0, R * 0.28f, q0, R * 0.28f, true);
        }
        var inDir = Vector3.Normalize(new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0));
        Voxels boss = new(lat);
        var hole = new Lattice();
        hole.AddBeam(center - inDir * 2f, R * 0.26f, center + inDir * (R * 1.3f), R * 0.26f, true);
        boss.BoolSubtract(new Voxels(hole));
        return boss;
    }

    private static Voxels CreateSensorBoss(float majorR, float z, float yawDeg, float size)
    {
        float yaw = yawDeg * MathF.PI / 180f;
        var dir = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        var origin = new Vector3(majorR * MathF.Cos(yaw), majorR * MathF.Sin(yaw), z);
        var frame = new LocalFrame(origin, dir);
        var boss = new BasePipe(frame, 12f, 0f, size);
        boss.SetLengthSteps(12);
        boss.SetPolarSteps(20);
        Voxels solid = boss.voxConstruct();
        var bore = new BasePipe(frame, 14f, 0f, size * 0.4f);
        bore.SetLengthSteps(10);
        bore.SetPolarSteps(16);
        solid.BoolSubtract(bore.voxConstruct());
        return solid;
    }
}

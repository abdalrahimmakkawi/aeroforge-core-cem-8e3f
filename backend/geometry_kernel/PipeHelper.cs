using System.Numerics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Geometry;

/// <summary>Toroidal propellant manifolds fused onto the engine jacket.</summary>
public static class PipeHelper
{
    /// <summary>
    /// Create a toroidal manifold centered on the Z axis at the given major radius.
    /// </summary>
    public static Voxels CreateToroidManifold(float majorRadius, float pipeRadius, float z = 0f)
    {
        majorRadius = MathF.Max(majorRadius, 10f);
        pipeRadius = Math.Clamp(pipeRadius, 4f, 16f);

        var frame = new LocalFrame(new Vector3(0, 0, z));
        var ring = new BaseRing(frame, majorRadius, pipeRadius);
        ring.SetPolarSteps(72);
        ring.SetRadialSteps(36);
        return ring.voxConstruct();
    }

    /// <summary>
    /// Lattice fallback torus when a lighter manifold is preferred.
    /// </summary>
    public static Voxels CreateToroidManifoldLattice(float majorRadius, float pipeRadius, float z = 0f)
    {
        majorRadius = MathF.Max(majorRadius, 10f);
        pipeRadius = Math.Clamp(pipeRadius, 3f, 14f);
        var lat = new Lattice();
        const int n = 64;
        for (int i = 0; i < n; i++)
        {
            float a0 = 2f * MathF.PI * i / n;
            float a1 = 2f * MathF.PI * (i + 1) / n;
            var p0 = new Vector3(majorRadius * MathF.Cos(a0), majorRadius * MathF.Sin(a0), z);
            var p1 = new Vector3(majorRadius * MathF.Cos(a1), majorRadius * MathF.Sin(a1), z);
            lat.AddBeam(p0, pipeRadius, p1, pipeRadius, true);
        }
        return new Voxels(lat);
    }
}

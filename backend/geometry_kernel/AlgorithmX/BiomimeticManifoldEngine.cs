using System.Numerics;
using PicoGK;

namespace AlgorithmX.Core;

/// <summary>
/// Murray's-law biomimetic propellant feed tree — fractal branching conduits
/// wrapping the injector faceplate (support-free AM pathways).
/// r_parent³ = Σ r_child³
/// </summary>
public static class BiomimeticManifoldEngine
{
    public static Voxels GenerateBranchingPipes(
        float inletDiameter = 22.0f,
        int branches = 64,
        float faceZ = 0f,
        float faceRadius = 80f,
        int depth = 4)
    {
        branches = Math.Clamp(branches, 16, 96);
        depth = Math.Clamp(depth, 3, 5);
        float r0 = Math.Clamp(inletDiameter * 0.5f, 6f, 14f);
        faceRadius = MathF.Max(faceRadius, 40f);

        var lat = new Lattice();

        // Dual trunk inlets (fuel + oxidizer) approaching the faceplate from +Z.
        var trunkA = new Vector3(faceRadius * 0.15f, 0, faceZ + 55f);
        var trunkB = new Vector3(-faceRadius * 0.15f, 0, faceZ + 55f);
        var rootA = new Vector3(faceRadius * 0.35f, 0, faceZ + 8f);
        var rootB = new Vector3(-faceRadius * 0.35f, 0, faceZ + 8f);
        lat.AddBeam(trunkA, r0, rootA, r0 * 0.92f, false);
        lat.AddBeam(trunkB, r0 * 0.95f, rootB, r0 * 0.88f, false);

        // Recursively bifurcate onto the faceplate disk.
        int tips = 0;
        Branch(lat, rootA, r0 * 0.9f, depth, 0f, MathF.PI * 0.55f, faceZ, faceRadius, ref tips, branches / 2);
        Branch(lat, rootB, r0 * 0.85f, depth, MathF.PI, MathF.PI * 0.55f, faceZ, faceRadius, ref tips, branches / 2);

        // Soft fill between sibling tips for a continuous organic manifold skin.
        Voxels tree = new(lat);
        try { tree = tree.voxOffset(0.6f).voxOffset(-0.35f); } catch { /* optional */ }
        return tree;
    }

    private static void Branch(
        Lattice lat,
        Vector3 parent,
        float rParent,
        int depthLeft,
        float yaw,
        float spread,
        float faceZ,
        float faceRadius,
        ref int tips,
        int tipBudget)
    {
        if (depthLeft <= 0 || tips >= tipBudget || rParent < 1.1f)
        {
            // Terminal micro-inlet into faceplate.
            var tip = new Vector3(
                Math.Clamp(parent.X, -faceRadius * 0.92f, faceRadius * 0.92f),
                Math.Clamp(parent.Y, -faceRadius * 0.92f, faceRadius * 0.92f),
                faceZ - 2f);
            lat.AddBeam(parent, rParent, tip, MathF.Max(rParent * 0.7f, 0.9f), false);
            tips++;
            return;
        }

        // Murray: r_p^3 = 2 * r_c^3  →  r_c = r_p / 2^(1/3)
        float rChild = rParent / MathF.Pow(2f, 1f / 3f);
        float step = 10f + depthLeft * 3.5f;

        for (int i = 0; i < 2; i++)
        {
            float side = (i == 0) ? -1f : 1f;
            float a = yaw + side * (spread * 0.5f);
            float radialOut = 0.55f + 0.12f * (4 - depthLeft);
            var child = parent + new Vector3(
                step * MathF.Cos(a) * radialOut,
                step * MathF.Sin(a) * radialOut,
                -step * (0.35f + 0.08f * depthLeft));

            // Keep tips on the faceplate disk.
            float rr = MathF.Sqrt(child.X * child.X + child.Y * child.Y);
            if (rr > faceRadius * 0.95f)
            {
                float s = (faceRadius * 0.92f) / rr;
                child = new Vector3(child.X * s, child.Y * s, child.Z);
            }
            child = new Vector3(child.X, child.Y, MathF.Max(child.Z, faceZ - 1f));

            lat.AddBeam(parent, rParent * 0.95f, child, rChild, false);
            Branch(lat, child, rChild, depthLeft - 1, a, spread * 0.78f, faceZ, faceRadius, ref tips, tipBudget);
        }
    }
}

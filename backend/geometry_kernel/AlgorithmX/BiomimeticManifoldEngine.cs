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
        int depth = 5)
    {
        branches = Math.Clamp(branches, 32, 128);
        depth = Math.Clamp(depth, 4, 6);
        float r0 = Math.Clamp(inletDiameter * 0.5f, 6f, 14f);
        faceRadius = MathF.Max(faceRadius, 40f);

        var lat = new Lattice();

        // Dual trunk inlets (fuel + oxidizer) approaching the faceplate from +Z.
        var trunkA = new Vector3(faceRadius * 0.18f, faceRadius * 0.05f, faceZ + 62f);
        var trunkB = new Vector3(-faceRadius * 0.18f, -faceRadius * 0.05f, faceZ + 62f);
        var rootA = new Vector3(faceRadius * 0.38f, faceRadius * 0.08f, faceZ + 10f);
        var rootB = new Vector3(-faceRadius * 0.38f, -faceRadius * 0.08f, faceZ + 10f);
        lat.AddBeam(trunkA, r0, rootA, r0 * 0.92f, false);
        lat.AddBeam(trunkB, r0 * 0.95f, rootB, r0 * 0.88f, false);

        // Cross-bridge for organic continuity between trunks.
        lat.AddBeam(rootA, r0 * 0.55f, rootB, r0 * 0.55f, false);

        int tips = 0;
        int budgetA = branches / 2;
        int budgetB = branches - budgetA;
        Branch(lat, rootA, r0 * 0.9f, depth, 0.15f, MathF.PI * 0.62f, faceZ, faceRadius, ref tips, budgetA, seed: 11);
        Branch(lat, rootB, r0 * 0.85f, depth, MathF.PI + 0.15f, MathF.PI * 0.62f, faceZ, faceRadius, ref tips, budgetB, seed: 29);

        // Helical wrap rings around the face — fractal “grown” feeders.
        int rings = Math.Clamp(branches / 16, 3, 6);
        for (int ring = 0; ring < rings; ring++)
        {
            float z = faceZ + 6f + ring * 7.5f;
            float rr = faceRadius * (0.55f + 0.08f * ring);
            float tube = MathF.Max(1.1f, r0 * (0.35f - 0.04f * ring));
            int segs = 28 + ring * 4;
            Vector3? prev = null;
            for (int s = 0; s <= segs; s++)
            {
                float t = s / (float)segs;
                float a = t * MathF.PI * 2f * (1.15f + 0.2f * ring) + ring * 0.7f;
                var p = new Vector3(rr * MathF.Cos(a), rr * MathF.Sin(a), z + 3f * MathF.Sin(a * 2f));
                if (prev is Vector3 pv)
                    lat.AddBeam(pv, tube, p, tube * 0.95f, false);
                prev = p;
            }
        }

        Voxels tree = new(lat);
        // Soften into an organically grown continuous manifold skin.
        try { tree = tree.voxOffset(0.85f).voxOffset(-0.45f); } catch { /* optional */ }
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
        int tipBudget,
        int seed)
    {
        if (depthLeft <= 0 || tips >= tipBudget || rParent < 1.0f)
        {
            var tip = new Vector3(
                Math.Clamp(parent.X, -faceRadius * 0.94f, faceRadius * 0.94f),
                Math.Clamp(parent.Y, -faceRadius * 0.94f, faceRadius * 0.94f),
                faceZ - 2.5f);
            lat.AddBeam(parent, rParent, tip, MathF.Max(rParent * 0.65f, 0.85f), false);
            tips++;
            return;
        }

        // Murray: r_p^3 = 2 * r_c^3  →  r_c = r_p / 2^(1/3)
        float rChild = rParent / MathF.Pow(2f, 1f / 3f);
        float step = 9f + depthLeft * 3.8f;
        // Deterministic pseudo-organic jitter from seed + depth (no RNG).
        float wobble = 0.12f * MathF.Sin(seed * 0.37f + depthLeft * 1.7f);

        int forks = depthLeft >= 4 ? 3 : 2; // trifurcate near root for denser fractal look
        for (int i = 0; i < forks; i++)
        {
            if (tips >= tipBudget) break;
            float side = forks == 2
                ? ((i == 0) ? -1f : 1f)
                : (i - 1f);
            float a = yaw + side * (spread * 0.42f) + wobble;
            float radialOut = 0.50f + 0.14f * (5 - depthLeft);
            var child = parent + new Vector3(
                step * MathF.Cos(a) * radialOut,
                step * MathF.Sin(a) * radialOut,
                -step * (0.32f + 0.09f * depthLeft));

            float rr = MathF.Sqrt(child.X * child.X + child.Y * child.Y);
            if (rr > faceRadius * 0.96f)
            {
                float s = (faceRadius * 0.93f) / rr;
                child = new Vector3(child.X * s, child.Y * s, child.Z);
            }
            child = new Vector3(child.X, child.Y, MathF.Max(child.Z, faceZ - 1.5f));

            float rOut = forks == 3 ? rChild * 0.92f : rChild;
            lat.AddBeam(parent, rParent * 0.94f, child, rOut, false);
            Branch(lat, child, rOut, depthLeft - 1, a, spread * 0.76f, faceZ, faceRadius, ref tips, tipBudget, seed + i * 17 + depthLeft);
        }
    }
}

using System.Numerics;
using AeroForge.Geometry;
using Leap71.LatticeLibrary;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Modules;

/// <summary>
/// External structural jackets: axial/hoop Iso-Grid ribs or Gyroid TPMS shell.
/// </summary>
public static class StructuralLatticeBuilder
{
    /// <summary>
    /// Build an Iso-Grid of axial + hoop ribs on the outer chamber surface.
    /// </summary>
    public static Voxels BuildIsoGrid(
        Voxels chamber,
        float ribThickness = 1.5f,
        List<Vector2>? gasProfile = null,
        float wallMm = 3.5f,
        int axialCount = 16,
        int hoopCount = 10)
    {
        ribThickness = Math.Clamp(ribThickness, 0.8f, 3.0f);
        wallMm = MathF.Max(wallMm, 3.0f);
        axialCount = Math.Clamp(axialCount, 8, 24);
        hoopCount = Math.Clamp(hoopCount, 5, 16);

        BBox3 bb = chamber.oCalculateBoundingBox();
        float z0 = bb.vecMin.Z + 4f;
        float z1 = bb.vecMax.Z - 8f;
        float L = MathF.Max(z1 - z0, 40f);

        List<Vector2> gas = gasProfile ?? CoolingChannelBuilderFallbackProfile(bb);
        List<Vector2> outer = ProfileHelper.GetOffsetProfile(gas, wallMm);

        float beamR = ribThickness * 0.55f;
        var lat = new Lattice();

        // Axial stringers
        const int azSamples = 40;
        for (int i = 0; i < axialCount; i++)
        {
            float phase = 2f * MathF.PI * i / axialCount;
            Vector3? prev = null;
            for (int s = 0; s <= azSamples; s++)
            {
                float t = s / (float)azSamples;
                float z = z0 + t * L;
                float r = ProfileHelper.RadiusAtZ(outer, z) + beamR * 0.6f;
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
            float r = ProfileHelper.RadiusAtZ(outer, z) + beamR * 0.55f;
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

        return new Voxels(lat);
    }

    /// <summary>
    /// Thin Gyroid TPMS structural jacket in an annular shell outside the chamber.
    /// </summary>
    public static Voxels BuildGyroidJacket(
        Voxels chamber,
        float shellMm = 6f,
        float unitSizeMm = 12f,
        float wallThickness = 1.2f)
    {
        shellMm = Math.Clamp(shellMm, 4f, 14f);
        BBox3 bb = chamber.oCalculateBoundingBox();
        // Outer grow
        Voxels outer = chamber.voxOffset(shellMm);
        // Inner keep-out slightly outside chamber
        Voxels keepOut = chamber.voxOffset(1.2f);
        Voxels shell = outer.voxBoolSubtract(keepOut);

        var gyroid = new ImplicitRadialGyroid(
            nUnitsPerRound: 10,
            fUnitSizeInZ: unitSizeMm,
            fWallThickness: wallThickness);

        return shell.voxIntersectImplicit(gyroid);
    }

    private static List<Vector2> CoolingChannelBuilderFallbackProfile(BBox3 bb)
    {
        float z0 = bb.vecMin.Z;
        float z1 = bb.vecMax.Z;
        float rMax = 0.5f * MathF.Max(bb.vecMax.X - bb.vecMin.X, bb.vecMax.Y - bb.vecMin.Y);
        return ProfileHelper.GetRaoProfile(rMax * 0.42f, rMax * 0.9f, MathF.Max(z1 - z0, 100f), rMax * 0.8f);
    }
}

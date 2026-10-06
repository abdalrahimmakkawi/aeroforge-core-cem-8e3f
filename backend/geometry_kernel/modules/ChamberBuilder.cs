using System.Numerics;
using AeroForge.Geometry;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Modules;

/// <summary>
/// Deterministic Rao / parabolic regenerative chamber–nozzle wall (hollow shell).
/// </summary>
public static class ChamberBuilder
{
    /// <summary>
    /// Build a hollow combustion chamber + convergent–divergent nozzle wall.
    /// Returns the structural jacket only (gas path already subtracted).
    /// </summary>
    public static Voxels BuildRaoBell(float rThroat, float rExit, float length, float wallMm = 3.5f)
    {
        float rt = MathF.Max(rThroat, 12f);
        float re = MathF.Max(rExit, rt * 1.5f);
        float L = MathF.Max(length, 140f);
        float wall = MathF.Max(wallMm, 3.0f);
        float rChamber = rt * 2.35f;

        List<Vector2> gas = ProfileHelper.GetRaoProfile(rt, re, L, rChamber);
        List<Vector2> outer = ProfileHelper.GetOffsetProfile(gas, offsetMm: wall);

        Voxels voxGas = Sh.voxRevolveContour(gas);
        Voxels voxWall = Sh.voxRevolveContour(outer);
        voxWall.BoolSubtract(voxGas);
        return voxWall;
    }

    /// <summary>Gas-side (Z,R) profile used by cooling / manifold conformal helpers.</summary>
    public static List<Vector2> GetGasProfile(float rThroat, float rExit, float length)
    {
        float rt = MathF.Max(rThroat, 12f);
        return ProfileHelper.GetRaoProfile(rt, MathF.Max(rExit, rt * 1.5f), MathF.Max(length, 140f), rt * 2.35f);
    }

    public static List<Vector2> GetOuterProfile(List<Vector2> gas, float wallMm) =>
        ProfileHelper.GetOffsetProfile(gas, MathF.Max(wallMm, 3.0f));
}

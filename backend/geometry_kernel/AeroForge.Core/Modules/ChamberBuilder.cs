using System.Numerics;
using AeroForge.Core.Physics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Core.Modules;

/// <summary>
/// Revolves a Rao gas contour into a hollow structural chamber wall.
/// </summary>
public static class ChamberBuilder
{
    /// <summary>
    /// Build hollow combustion chamber + nozzle jacket from a 2D gas profile.
    /// </summary>
    public static Voxels BuildHollowWall(List<Vector2> gasProfile, float wallThicknessMm = 3.5f)
    {
        if (gasProfile == null || gasProfile.Count < 4)
            throw new ArgumentException("Gas profile requires ≥4 (Z,R) samples.");

        float wall = MathF.Max(wallThicknessMm, 3.0f);
        List<Vector2> outer = RaoContour.OffsetProfile(gasProfile, wall);

        Voxels voxInnerGasPath = Sh.voxRevolveContour(gasProfile);
        Voxels voxOuterShell = Sh.voxRevolveContour(outer);
        voxOuterShell.BoolSubtract(voxInnerGasPath);
        return voxOuterShell;
    }
}

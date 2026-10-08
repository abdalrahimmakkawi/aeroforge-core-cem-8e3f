using System.Numerics;
using PicoGK;

namespace AlgorithmX.Core;

/// <summary>
/// Conformal TPMS gyroid cooling matrix intersected with the regenerative jacket shell.
/// Returns the channel void field (subtract from the solid shell to carve passages).
/// </summary>
public static class GyroidHeatExchangerEngine
{
    /// <summary>
    /// Build a conformal gyroid void network in an annular shell around the gas core.
    /// Frequency and wall thickness spike near the throat (Q_max).
    /// </summary>
    public static Voxels GenerateConformalMatrix(
        Voxels voxGasCore,
        float baseCellSize = 3.5f,
        float throatDensityFactor = 2.5f,
        float shellMm = 6.5f,
        float keepOutMm = 1.0f,
        float? throatZ = null,
        float? lengthMm = null)
    {
        BBox3 bb = voxGasCore.oCalculateBoundingBox();
        float z0 = bb.vecMin.Z;
        float z1 = bb.vecMax.Z;
        float L = MathF.Max(z1 - z0, 80f);
        float zThroat = throatZ ?? (z0 + L * 0.42f);

        // Annular jacket: outer offset minus keep-out around gas.
        Voxels outer = voxGasCore.voxOffset(Math.Clamp(shellMm, 4.5f, 12f));
        Voxels keepOut = voxGasCore.voxOffset(Math.Clamp(keepOutMm, 0.6f, 2.0f));
        Voxels shell = outer.voxBoolSubtract(keepOut);

        var gyroid = new ImplicitDensityGyroid(
            throatZ: zThroat,
            baseCellSizeMm: baseCellSize,
            throatDensityFactor: throatDensityFactor,
            wallThicknessMm: 1.1f,
            lengthMm: lengthMm ?? L);

        // Gyroid walls inside the jacket shell.
        Voxels gyroidWalls = shell.voxIntersectImplicit(gyroid);

        // Channel voids = shell material not occupied by gyroid walls.
        // Master pipeline BoolSubtracts this from the solid offset shell.
        return shell.voxBoolSubtract(gyroidWalls);
    }
}

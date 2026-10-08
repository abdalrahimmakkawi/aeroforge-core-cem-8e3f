using System.Numerics;
using PicoGK;

namespace AlgorithmX.Core;

/// <summary>
/// Conformal TPMS gyroid cooling matrix intersected with the regenerative jacket shell.
/// Returns the channel void field (subtract from the solid shell to carve passages).
/// f = sin(X)cos(Y) + sin(Y)cos(Z) + sin(Z)cos(X)
/// </summary>
public static class GyroidHeatExchangerEngine
{
    /// <summary>
    /// Dense conformal gyroid void network in an annular shell around the gas core.
    /// Cell frequency spikes near the throat (Q_max) for organically packed AM lattices.
    /// </summary>
    public static Voxels GenerateConformalMatrix(
        Voxels voxGasCore,
        float baseCellSize = 3.2f,
        float throatDensityFactor = 3.2f,
        float shellMm = 7.5f,
        float keepOutMm = 0.75f,
        float? throatZ = null,
        float? lengthMm = null)
    {
        BBox3 bb = voxGasCore.oCalculateBoundingBox();
        float z0 = bb.vecMin.Z;
        float z1 = bb.vecMax.Z;
        float L = MathF.Max(z1 - z0, 80f);
        float zThroat = throatZ ?? (z0 + L * 0.42f);

        // Thick annular jacket so section cuts reveal many gyroid cells.
        float shell = Math.Clamp(shellMm, 5.5f, 14f);
        Voxels outer = voxGasCore.voxOffset(shell);
        Voxels keepOut = voxGasCore.voxOffset(Math.Clamp(keepOutMm, 0.5f, 1.8f));
        Voxels jacket = outer.voxBoolSubtract(keepOut);

        var gyroid = new ImplicitDensityGyroid(
            throatZ: zThroat,
            baseCellSizeMm: Math.Clamp(baseCellSize, 2.2f, 5.5f),
            throatDensityFactor: Math.Clamp(throatDensityFactor, 2.0f, 4.0f),
            wallThicknessMm: 0.85f,
            lengthMm: lengthMm ?? L);

        // Gyroid sheet walls inside the jacket.
        Voxels gyroidWalls = jacket.voxIntersectImplicit(gyroid);

        // Channel voids = jacket not occupied by gyroid walls — BoolSubtracted by master.
        return jacket.voxBoolSubtract(gyroidWalls);
    }
}

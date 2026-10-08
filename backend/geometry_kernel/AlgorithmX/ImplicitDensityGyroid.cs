using System.Numerics;
using PicoGK;

namespace AlgorithmX.Core;

/// <summary>
/// Implicit gyroid TPMS with spatially varying frequency — denser near the throat
/// for maximum heat-transfer surface area.
/// f = sin(X)cos(Y) + sin(Y)cos(Z) + sin(Z)cos(X)
/// </summary>
public sealed class ImplicitDensityGyroid : IImplicit
{
    private readonly float _throatZ;
    private readonly float _baseCellMm;
    private readonly float _throatDensityFactor;
    private readonly float _wallThickness;
    private readonly float _sigmaZ;

    public ImplicitDensityGyroid(
        float throatZ,
        float baseCellSizeMm,
        float throatDensityFactor,
        float wallThicknessMm,
        float lengthMm)
    {
        _throatZ = throatZ;
        _baseCellMm = MathF.Max(baseCellSizeMm, 2.0f);
        _throatDensityFactor = Math.Clamp(throatDensityFactor, 1.0f, 4.0f);
        _wallThickness = Math.Clamp(wallThicknessMm, 0.6f, 2.5f);
        _sigmaZ = MathF.Max(lengthMm * 0.18f, 25f);
    }

    public float fSignedDistance(in Vector3 vecPt)
    {
        float dz = (vecPt.Z - _throatZ) / _sigmaZ;
        float w = MathF.Exp(-dz * dz); // 1 at throat
        float dens = 1f + (_throatDensityFactor - 1f) * w;
        float cell = _baseCellMm / dens;
        float f = (2f * MathF.PI) / cell;

        float X = f * vecPt.X;
        float Y = f * vecPt.Y;
        float Z = f * vecPt.Z;

        // Classic Schoen gyroid
        float g =
            MathF.Sin(X) * MathF.Cos(Y) +
            MathF.Sin(Y) * MathF.Cos(Z) +
            MathF.Sin(Z) * MathF.Cos(X);

        // Wall thickness scales mildly with density (thinner sheets at high density).
        float halfW = 0.5f * (_wallThickness / MathF.Sqrt(dens));
        return MathF.Abs(g) - halfW;
    }
}

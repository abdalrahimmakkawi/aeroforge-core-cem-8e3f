using System.Numerics;
using AeroForge.Core;
using AeroForge.Core.Physics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AlgorithmX.Core;

/// <summary>
/// First-principles gas-path solid for Algorithm X (Rao CD contour → revolved SDF body).
/// </summary>
public static class PhysicsEngine
{
    public static GasDynamicsResult Calculate(EngineSpecs specs) =>
        GasDynamics.Calculate(
            specs.ThrustkN,
            specs.ChamberPressureBar,
            specs.AmbientPressurePa,
            specs.Gamma);

    /// <summary>Solid of revolution of the gas-side Rao contour (hot-gas volume).</summary>
    public static Voxels GetGasPath(EngineSpecs specs)
    {
        var physics = Calculate(specs);
        List<Vector2> profile = RaoContour.GetProfile(physics);
        return Sh.voxRevolveContour(profile);
    }

    public static (Voxels gas, GasDynamicsResult physics, List<Vector2> profile) GetGasPathWithPhysics(EngineSpecs specs)
    {
        var physics = Calculate(specs);
        List<Vector2> profile = RaoContour.GetProfile(physics);
        return (Sh.voxRevolveContour(profile), physics, profile);
    }
}

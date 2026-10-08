using System.Numerics;
using AeroForge.Core;
using AeroForge.Core.Physics;
using PicoGK;

namespace AlgorithmX.Core;

/// <summary>
/// Algorithm X master synthesis — deterministic field CEM.
/// LLM / Studio provide <see cref="EngineConfig"/> JSON only; this class owns all geometry.
/// </summary>
public static class AlgorithmXMaster
{
    /// <summary>Primary entry — EngineConfig JSON → monolithic voxel engine.</summary>
    public static Voxels Execute(EngineConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return GenerateEngine(config.ToEngineSpecs(), config);
    }

    public static Voxels GenerateEngine(EngineSpecs specs) =>
        GenerateEngine(specs, null);

    public static Voxels GenerateEngine(EngineSpecs specs, EngineConfig? config)
    {
        float wall = MathF.Max(config?.WallThicknessMm ?? specs.WallThicknessMm, 3.2f);
        float cell = Math.Clamp(config?.GyroidCellSizeMm ?? specs.GyroidCellSizeMm, 2.2f, 5.5f);
        int branches = Math.Clamp(config?.ManifoldBranches ?? specs.ManifoldBranches, 48, 128);
        int swirlers = Math.Clamp(
            config?.SwirlerCount ?? (specs.InjectorElementCount > 0 ? specs.InjectorElementCount : 72),
            50, 120);
        string cooling = (config?.CoolingType ?? specs.CoolingType ?? "gyroid_tpms").ToLowerInvariant();

        // 1. First-principles Rao gas core
        var (voxGasCore, physics, profile) = PhysicsEngine.GetGasPathWithPhysics(specs);
        float faceR = physics.rInjector;
        float zFace = profile.Count > 0 ? profile[0].X : 0f;
        float zThroat = profile.Count > 0
            ? profile.OrderBy(p => p.Y).First().X
            : physics.LengthMm * 0.42f;

        // 2. Conformal gyroid TPMS cooling jacket (void field) — dense, organic lattice
        Voxels? voxGyroidCooling = null;
        if (cooling is "gyroid_tpms" or "gyroid" or "tpms")
        {
            voxGyroidCooling = GyroidHeatExchangerEngine.GenerateConformalMatrix(
                voxGasCore,
                baseCellSize: cell,
                throatDensityFactor: 3.2f,
                shellMm: wall + 4.2f,
                keepOutMm: 0.75f,
                throatZ: zThroat,
                lengthMm: physics.LengthMm);
        }

        // 3. Biomimetic Murray-law propellant distribution tree (fractal)
        Voxels voxFluidTree = BiomimeticManifoldEngine.GenerateBranchingPipes(
            inletDiameter: MathF.Max(specs.PipeDiameterMm, 18f),
            branches: branches,
            faceZ: zFace,
            faceRadius: faceR,
            depth: 5);

        // 4. High-density coax swirler injector with LOX / CH₄ plenums
        Voxels voxInjectorArray = AlgorithmicInjectorEngine.GenerateSwirlers(
            elementCount: swirlers,
            faceRadius: faceR,
            faceZ: zFace - 2f);

        // 5. Monolithic field union
        Voxels voxEngine = voxGasCore.voxOffset(wall);
        voxEngine.BoolSubtract(voxGasCore);           // hollow hot-gas path
        if (voxGyroidCooling is not null)
            voxEngine.BoolSubtract(voxGyroidCooling); // carve TPMS cooling channels
        voxEngine.BoolAdd(voxFluidTree);              // organic manifolds
        voxEngine.BoolAdd(voxInjectorArray);          // swirler dome + plenums

        try { voxEngine.Fillet(0.25f); } catch { /* optional */ }
        return voxEngine;
    }

    public static Mesh GenerateMesh(EngineSpecs specs) => GenerateEngine(specs).mshAsMesh();

    public static Mesh ExecuteMesh(EngineConfig config) => Execute(config).mshAsMesh();

    public static (Mesh mesh, GasDynamicsResult physics) GenerateWithPhysics(EngineSpecs specs)
    {
        var physics = PhysicsEngine.Calculate(specs);
        return (GenerateMesh(specs), physics);
    }

    public static (Mesh mesh, GasDynamicsResult physics) ExecuteWithPhysics(EngineConfig config)
    {
        var specs = config.ToEngineSpecs();
        var physics = PhysicsEngine.Calculate(specs);
        return (ExecuteMesh(config), physics);
    }
}

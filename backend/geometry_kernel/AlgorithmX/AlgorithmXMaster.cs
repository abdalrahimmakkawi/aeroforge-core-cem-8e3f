using System.Numerics;
using AeroForge.Core;
using AeroForge.Core.Physics;
using PicoGK;

namespace AlgorithmX.Core;

/// <summary>
/// Algorithm X orchestration — implicit-field generative propulsion synthesis.
/// Geometry is derived from physics gradients and TPMS / Murray / swirler field engines,
/// not from static CAD revolved primitives alone.
/// </summary>
public static class AlgorithmXMaster
{
    public static Voxels GenerateEngine(EngineSpecs specs)
    {
        float wall = MathF.Max(specs.WallThicknessMm, 3.2f);
        int swirlers = Math.Clamp(specs.InjectorElementCount <= 0 ? 72 : specs.InjectorElementCount, 60, 120);
        // Lift legacy low counts into Algorithm X density band.
        if (swirlers < 60) swirlers = 72;

        // 1. First-principles Rao gas core
        var (voxGasCore, physics, profile) = PhysicsEngine.GetGasPathWithPhysics(specs);
        float faceR = physics.rInjector;
        float zThroat = profile.Count > 0
            ? profile.OrderBy(p => p.Y).First().X
            : physics.LengthMm * 0.42f;

        // 2. Conformal gyroid TPMS cooling jacket (void field)
        Voxels voxGyroidCooling = GyroidHeatExchangerEngine.GenerateConformalMatrix(
            voxGasCore,
            baseCellSize: 3.5f,
            throatDensityFactor: 2.5f,
            shellMm: wall + 3.5f,
            keepOutMm: 0.9f,
            throatZ: zThroat,
            lengthMm: physics.LengthMm);

        // 3. Biomimetic Murray-law propellant distribution tree
        Voxels voxFluidTree = BiomimeticManifoldEngine.GenerateBranchingPipes(
            inletDiameter: MathF.Max(specs.PipeDiameterMm, 18f),
            branches: 64,
            faceZ: profile.Count > 0 ? profile[0].X : 0f,
            faceRadius: faceR);

        // 4. High-density coax swirler injector with LOX / CH₄ plenums
        Voxels voxInjectorArray = AlgorithmicInjectorEngine.GenerateSwirlers(
            elementCount: swirlers,
            faceRadius: faceR,
            faceZ: profile.Count > 0 ? profile[0].X - 2f : -2f);

        // 5. Monolithic field union & isosurface extraction
        // Solid shell = gas offset (wall envelope), then carve gas + gyroid voids.
        Voxels voxEngine = voxGasCore.voxOffset(wall);
        voxEngine.BoolSubtract(voxGasCore);           // hollow hot-gas path
        voxEngine.BoolSubtract(voxGyroidCooling);     // carve TPMS cooling channels
        voxEngine.BoolAdd(voxFluidTree);              // organic manifolds
        voxEngine.BoolAdd(voxInjectorArray);          // swirler dome + plenums

        try { voxEngine.Fillet(0.3f); } catch { /* optional */ }
        return voxEngine;
    }

    public static Mesh GenerateMesh(EngineSpecs specs) => GenerateEngine(specs).mshAsMesh();

    public static (Mesh mesh, GasDynamicsResult physics) GenerateWithPhysics(EngineSpecs specs)
    {
        var physics = PhysicsEngine.Calculate(specs);
        return (GenerateMesh(specs), physics);
    }
}

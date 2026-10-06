using AeroForge.Geometry;
using PicoGK;

namespace AeroForge.Modules;

/// <summary>
/// Deterministic modular engine assembler — the only geometry path the LLM should call.
/// </summary>
public static class CustomEngine
{
    /// <summary>
    /// Assemble a regenerative rocket engine from pre-built CEM modules.
    /// </summary>
    public static Voxels Generate(EngineSpecs specs)
    {
        float wall = MathF.Max(specs.wallMm, 3.5f);

        int channelCount = Math.Clamp(specs.channelCount <= 0 ? 48 : specs.channelCount, 32, 64);
        float channelDepth = Math.Clamp(
            specs.channelDepthMm > 0 ? specs.channelDepthMm : 2.5f,
            1.2f,
            wall * 0.7f);
        float pipeDiameter = specs.pipeDiameterMm > 0 ? specs.pipeDiameterMm : 18.0f;
        float ribThickness = specs.ribThicknessMm > 0 ? specs.ribThicknessMm : 1.5f;

        // 1. Base chamber (hollow Rao bell)
        List<System.Numerics.Vector2> gas = ChamberBuilder.GetGasProfile(specs.rThroat, specs.rExit, specs.length);
        Voxels chamber = ChamberBuilder.BuildRaoBell(specs.rThroat, specs.rExit, specs.length, wall);

        // 2. Visible surface cooling channels (ribbed outer grooves)
        Voxels channels = CoolingChannelBuilder.BuildHelical(
            chamber,
            count: channelCount,
            depthMm: channelDepth,
            gasProfile: gas,
            wallMm: wall,
            helixTurns: 2.6f,
            channelWidthMm: 2.2f);
        chamber.BoolSubtract(channels);

        // 3. Distribution manifolds & fuel ports
        Voxels manifolds = ManifoldBuilder.BuildToroidalRings(
            specs.rInjector,
            pipeDiameter: pipeDiameter,
            rExit: specs.rExit,
            length: specs.length,
            wallMm: wall);
        chamber.BoolAdd(manifolds);

        // 4. Structural Iso-Grid jacket
        Voxels latticeJacket = StructuralLatticeBuilder.BuildIsoGrid(
            chamber,
            ribThickness: ribThickness,
            gasProfile: gas,
            wallMm: wall);
        chamber.BoolAdd(latticeJacket);

        // Injector flange for a complete assembly
        chamber.BoolAdd(FlangeHelper.CreateInjectorCap(specs.rInjector, boltCount: 8));

        try { chamber.Fillet(0.45f); } catch { /* optional */ }
        return chamber;
    }

    public static Mesh GenerateMesh(EngineSpecs specs) => Generate(specs).mshAsMesh();

    public static Mesh GenerateFromEngineSpec(EngineSpec spec)
    {
        var specs = EngineSpecs.FromGeometry(
            spec.ThroatRadiusMm,
            spec.ExitRadiusMm,
            spec.LengthMm,
            spec.ExpansionRatio,
            MathF.Max(spec.WallMm, 3.5f),
            channelCount: Math.Clamp(spec.ChannelCount <= 16 ? 48 : spec.ChannelCount, 32, 64));
        return GenerateMesh(specs);
    }
}

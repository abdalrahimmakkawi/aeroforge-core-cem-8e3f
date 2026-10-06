using PicoGK;

namespace AeroForge.Geometry;

/// <summary>
/// Thin façade that routes regenerative engines through the modular CEM framework
/// (<see cref="AeroForge.Modules.CustomEngine"/>).
/// </summary>
public static class EngineAssembly
{
    public static Voxels VoxCreateEngine(EngineParams p)
    {
        var specs = new AeroForge.Modules.EngineSpecs
        {
            rThroat = p.rThroat,
            rExit = p.rExit,
            rInjector = p.rInjector,
            length = p.length,
            wallMm = MathF.Max(p.wallMm, 3.5f),
            expansionRatio = p.expansionRatio,
        };
        return AeroForge.Modules.CustomEngine.Generate(specs);
    }

    public static Mesh MshCreateEngine(EngineParams p) => VoxCreateEngine(p).mshAsMesh();
}

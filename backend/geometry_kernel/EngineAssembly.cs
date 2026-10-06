using System.Numerics;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Geometry;

/// <summary>
/// Multi-component functionally integrated rocket engine assembly.
/// Every regenerative engine follows this mandatory 6-step pipeline.
/// </summary>
public static class EngineAssembly
{
    /// <summary>
    /// 6-step PicoGK / ShapeKernel assembly:
    /// 1) inner gas path void · 2) structural wall · 3) toroidal manifolds ·
    /// 4) conformal helical cooling · 5) injector flange · 6) monolithic merge.
    /// </summary>
    public static Voxels VoxCreateEngine(EngineParams p)
    {
        // STEP 1: INNER GAS PATH (void where hot gases flow)
        List<Vector2> gasPathProfile = ProfileHelper.GetRaoProfile(p.rThroat, p.rExit, p.length, p.rInjector);
        Voxels voxGasPath = Sh.voxRevolveContour(gasPathProfile);

        // STEP 2: COMBUSTION CHAMBER & NOZZLE WALL (structural shell)
        float wall = MathF.Max(p.wallMm, 3.0f);
        List<Vector2> outerProfile = ProfileHelper.GetOffsetProfile(gasPathProfile, offsetMm: wall);
        Voxels voxEngineWall = Sh.voxRevolveContour(outerProfile);
        voxEngineWall.BoolSubtract(voxGasPath); // Hollow out the inside

        // STEP 3: TOROIDAL PROPELLANT MANIFOLDS (inlet & outlet rings)
        float zTop = gasPathProfile[0].X + MathF.Max(p.length * 0.06f, 8f);
        float zBot = gasPathProfile[^1].X - MathF.Max(p.length * 0.04f, 6f);
        float rTop = ProfileHelper.RadiusAtZ(gasPathProfile, zTop) + wall * 0.55f;
        float rBot = ProfileHelper.RadiusAtZ(gasPathProfile, zBot) + wall * 0.55f;

        Voxels voxTopManifold = PipeHelper.CreateToroidManifold(rTop, pipeRadius: p.topManifoldPipeR, z: zTop);
        Voxels voxBottomManifold = PipeHelper.CreateToroidManifold(rBot, pipeRadius: p.bottomManifoldPipeR, z: zBot);

        // STEP 4: CONFORMAL HELICAL COOLING CHANNELS
        Voxels voxChannels = ChannelHelper.CreateHelicalChannels(
            gasPathProfile,
            count: p.channelCount,
            channelWidth: p.channelWidth,
            wallMm: wall,
            helixTurns: p.helixTurns);

        // Carve channels into the wall (enclosed — width clamped in ChannelHelper)
        voxEngineWall.BoolSubtract(voxChannels);

        // STEP 5: INJECTOR HEAD & MOUNTING FLANGE
        Voxels voxInjectorHead = FlangeHelper.CreateInjectorCap(p.rInjector, boltCount: p.boltCount);

        // STEP 6: MONOLITHIC MERGE (single watertight output)
        Voxels voxFinalEngine = voxEngineWall;
        voxFinalEngine.BoolAdd(voxTopManifold);
        voxFinalEngine.BoolAdd(voxBottomManifold);
        voxFinalEngine.BoolAdd(voxInjectorHead);

        try { voxFinalEngine.Fillet(0.5f); } catch { /* optional cosmetics */ }

        return voxFinalEngine;
    }

    public static Mesh MshCreateEngine(EngineParams p) => VoxCreateEngine(p).mshAsMesh();
}

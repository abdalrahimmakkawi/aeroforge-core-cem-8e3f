using System.Numerics;
using System.Text.Json;
using AeroForge.Core.Modules;
using AeroForge.Core.Physics;
using PicoGK;

namespace AeroForge.Core;

/// <summary>
/// JSON configuration payload emitted by the LLM / UI — never raw geometry loops.
/// </summary>
public sealed class EngineSpecs
{
    public float ThrustkN { get; set; } = 50.0f;
    public float ChamberPressureBar { get; set; } = 60.0f;
    public int CoolingChannelCount { get; set; } = 48;
    public float WallThicknessMm { get; set; } = 3.5f;
    public float ChannelDepthMm { get; set; } = 2.5f;
    public float HelixTurns { get; set; } = 2.6f;
    public float PipeDiameterMm { get; set; } = 18.0f;
    public float AmbientPressurePa { get; set; } = 101325f;
    public float Gamma { get; set; } = 1.20f;
    public float VoxelMm { get; set; } = 1.5f;

    public static EngineSpecs FromJson(JsonElement root)
    {
        float GetF(string a, string b, float d)
        {
            if (root.TryGetProperty(a, out var va) && va.ValueKind == JsonValueKind.Number)
                return va.GetSingle();
            if (root.TryGetProperty(b, out var vb) && vb.ValueKind == JsonValueKind.Number)
                return vb.GetSingle();
            return d;
        }
        int GetI(string a, string b, int d)
        {
            if (root.TryGetProperty(a, out var va) && va.ValueKind == JsonValueKind.Number)
                return va.GetInt32();
            if (root.TryGetProperty(b, out var vb) && vb.ValueKind == JsonValueKind.Number)
                return vb.GetInt32();
            return d;
        }

        // Prefer nested engine_specs object when present.
        JsonElement src = root;
        if (root.TryGetProperty("engine_specs", out var nested) && nested.ValueKind == JsonValueKind.Object)
            src = nested;

        float thrust = GetF("ThrustkN", "thrust_kn", 50f);
        // Accept MPa from UI and convert, or bar directly.
        float bar = 60f;
        if (src.TryGetProperty("ChamberPressureBar", out var cpb) && cpb.ValueKind == JsonValueKind.Number)
            bar = cpb.GetSingle();
        else if (src.TryGetProperty("chamber_pressure_bar", out var cpb2) && cpb2.ValueKind == JsonValueKind.Number)
            bar = cpb2.GetSingle();
        else if (src.TryGetProperty("chamber_pressure_mpa", out var cpm) && cpm.ValueKind == JsonValueKind.Number)
            bar = cpm.GetSingle() * 10f;
        else if (root.TryGetProperty("chamber_pressure_mpa", out var cpm2) && cpm2.ValueKind == JsonValueKind.Number)
            bar = cpm2.GetSingle() * 10f;

        if (root.TryGetProperty("thrust_kn", out var tk) && tk.ValueKind == JsonValueKind.Number)
            thrust = tk.GetSingle();

        return new EngineSpecs
        {
            ThrustkN = Math.Clamp(thrust, 5f, 100f),
            ChamberPressureBar = Math.Clamp(bar, 10f, 150f),
            CoolingChannelCount = Math.Clamp(GetI("CoolingChannelCount", "channel_count", 48), 32, 64),
            WallThicknessMm = Math.Clamp(GetF("WallThicknessMm", "wall_mm", 3.5f), 3.0f, 8f),
            ChannelDepthMm = Math.Clamp(GetF("ChannelDepthMm", "channel_depth_mm", 2.5f), 1.2f, 4f),
            HelixTurns = Math.Clamp(GetF("HelixTurns", "helix_turns", 2.6f), 1.8f, 4f),
            PipeDiameterMm = Math.Clamp(GetF("PipeDiameterMm", "pipe_diameter_mm", 18f), 10f, 28f),
            AmbientPressurePa = GetF("AmbientPressurePa", "ambient_pressure_pa", 101325f),
            Gamma = GetF("Gamma", "gamma", 1.20f),
            VoxelMm = Math.Clamp(GetF("VoxelMm", "voxel_mm", 1.5f), 0.8f, 2.0f),
        };
    }
}

/// <summary>
/// Master CEM pipeline — thermodynamics → Rao contour → hollow wall → cooling → manifolds → injector.
/// </summary>
public static class AeroForgeEngine
{
    public static Voxels Build(EngineSpecs specs)
    {
        // 1. First-principles gas dynamics
        var physics = GasDynamics.Calculate(
            specs.ThrustkN,
            specs.ChamberPressureBar,
            specs.AmbientPressurePa,
            specs.Gamma);

        // 2. Parabolic Rao curve
        List<Vector2> profile = RaoContour.GetEngineProfile(physics);

        // 3. Hollow chamber wall
        Voxels voxChamber = ChamberBuilder.BuildHollowWall(profile, specs.WallThicknessMm);

        // 4. Conformal cooling channels (visible ribbed outer grooves)
        Voxels voxChannels = CoolingBuilder.BuildConformalChannels(
            profile,
            channelCount: specs.CoolingChannelCount,
            depthMm: specs.ChannelDepthMm,
            pitch: specs.HelixTurns,
            wallMm: specs.WallThicknessMm);
        voxChamber.BoolSubtract(voxChannels);

        // 5. Toroidal manifolds & pipe ports
        Voxels voxManifolds = ManifoldBuilder.BuildRings(
            physics.rInjector,
            physics.rExit,
            lengthMm: physics.LengthMm,
            wallMm: specs.WallThicknessMm,
            pipeDiameterMm: specs.PipeDiameterMm);
        voxChamber.BoolAdd(voxManifolds);

        // 6. Injector cap
        Voxels voxCap = InjectorBuilder.BuildFlange(physics.rInjector);
        voxChamber.BoolAdd(voxCap);

        try { voxChamber.Fillet(0.4f); } catch { /* optional */ }
        return voxChamber;
    }

    public static Mesh BuildMesh(EngineSpecs specs) => Build(specs).mshAsMesh();

    /// <summary>Run Core pipeline and return physics used (for API metrics).</summary>
    public static (Mesh mesh, GasDynamicsResult physics) BuildWithPhysics(EngineSpecs specs)
    {
        var physics = GasDynamics.Calculate(
            specs.ThrustkN,
            specs.ChamberPressureBar,
            specs.AmbientPressurePa,
            specs.Gamma);
        List<Vector2> profile = RaoContour.GetEngineProfile(physics);
        Voxels vox = ChamberBuilder.BuildHollowWall(profile, specs.WallThicknessMm);
        vox.BoolSubtract(CoolingBuilder.BuildConformalChannels(
            profile, specs.CoolingChannelCount, specs.ChannelDepthMm, specs.HelixTurns, specs.WallThicknessMm));
        vox.BoolAdd(ManifoldBuilder.BuildRings(
            physics.rInjector, physics.rExit, physics.LengthMm, specs.WallThicknessMm, specs.PipeDiameterMm));
        vox.BoolAdd(InjectorBuilder.BuildFlange(physics.rInjector));
        try { vox.Fillet(0.4f); } catch { /* optional */ }
        return (vox.mshAsMesh(), physics);
    }
}

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
    public int CoolingChannelCount { get; set; } = 72;
    public float WallThicknessMm { get; set; } = 3.2f;
    public float ChannelDepthMm { get; set; } = 2.5f;
    public float HelixTurns { get; set; } = 2.8f;
    public float PipeDiameterMm { get; set; } = 18.0f;
    public int InjectorElementCount { get; set; } = 36;
    public float AmbientPressurePa { get; set; } = 101325f;
    public float Gamma { get; set; } = 1.20f;
    public float ChamberTempK { get; set; } = 3550f;
    public float MolarMass { get; set; } = 21f;
    public string Propellant { get; set; } = "lox_methane";
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

        JsonElement src = root;
        if (root.TryGetProperty("engine_specs", out var nested) && nested.ValueKind == JsonValueKind.Object)
            src = nested;

        float thrust = GetF("ThrustkN", "thrust_kn", 50f);
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

        int channels = GetI("CoolingChannelCount", "channel_count", 72);
        // Flight-grade cooling band is 64–128; lift legacy 32–48 requests into band.
        if (channels < 64) channels = 72;

        return new EngineSpecs
        {
            ThrustkN = Math.Clamp(thrust, 5f, 100f),
            ChamberPressureBar = Math.Clamp(bar, 10f, 150f),
            CoolingChannelCount = Math.Clamp(channels, 64, 128),
            WallThicknessMm = Math.Clamp(GetF("WallThicknessMm", "wall_mm", 3.2f), 3.0f, 8f),
            ChannelDepthMm = Math.Clamp(GetF("ChannelDepthMm", "channel_depth_mm", 2.5f), 1.2f, 4f),
            HelixTurns = Math.Clamp(GetF("HelixTurns", "helix_turns", 2.8f), 1.8f, 4f),
            PipeDiameterMm = Math.Clamp(GetF("PipeDiameterMm", "pipe_diameter_mm", 18f), 10f, 28f),
            InjectorElementCount = Math.Clamp(GetI("InjectorElementCount", "injector_elements", 72), 18, 120),
            AmbientPressurePa = GetF("AmbientPressurePa", "ambient_pressure_pa", 101325f),
            Gamma = GetF("Gamma", "gamma", 1.20f),
            ChamberTempK = GetF("ChamberTempK", "chamber_temp_k", 3550f),
            MolarMass = GetF("MolarMass", "molar_mass", 21f),
            Propellant = root.TryGetProperty("propellant", out var prop) && prop.ValueKind == JsonValueKind.String
                ? (prop.GetString() ?? "lox_methane")
                : (src.TryGetProperty("Propellant", out var prop2) && prop2.ValueKind == JsonValueKind.String
                    ? (prop2.GetString() ?? "lox_methane")
                    : "lox_methane"),
            VoxelMm = Math.Clamp(GetF("VoxelMm", "voxel_mm", 1.5f), 0.8f, 2.0f),
        };
    }
}

/// <summary>
/// Master flight-grade CEM — multi-component monolithic voxel assembly.
/// Not a single revolved solid: every feature is a fluid path, thermal boundary, or load path.
/// </summary>
public static class AeroForgeEngine
{
    /// <summary>Primary entry — flight-grade multi-physics synthesis.</summary>
    public static Voxels Build(EngineSpecs specs) => BuildFlightGradeEngine(specs);

    public static Voxels BuildFlightGradeEngine(EngineSpecs specs)
    {
        float wall = MathF.Max(specs.WallThicknessMm, 3.2f);
        int channels = Math.Clamp(specs.CoolingChannelCount <= 0 ? 72 : specs.CoolingChannelCount, 64, 128);
        int elements = Math.Clamp(specs.InjectorElementCount <= 0 ? 36 : specs.InjectorElementCount, 18, 48);

        // 1. Thermodynamics & Rao contour
        var physics = GasDynamics.Calculate(
            specs.ThrustkN,
            specs.ChamberPressureBar,
            specs.AmbientPressurePa,
            specs.Gamma,
            specs.ChamberTempK,
            specs.MolarMass);
        List<Vector2> contour = RaoContour.GetProfile(physics);

        // 2. Hollow chamber wall shell
        Voxels voxChamber = ChamberBuilder.BuildHollowShell(contour, wallThickness: wall);

        // 3. Variable high-aspect conformal cooling (teardrop, AM self-supporting)
        Voxels voxCoolingPassages = VariableAspectCoolingBuilder.GeneratePassages(
            contour,
            channelCount: channels,
            teardropProfile: true,
            wallMm: wall,
            helixTurns: specs.HelixTurns);
        voxChamber.BoolSubtract(voxCoolingPassages);

        // 4. Toroidal manifolds & plumbing stubs
        Voxels voxManifolds = ManifoldPlumbingBuilder.GenerateRingsAndPorts(
            physics,
            wallMm: wall,
            pipeDiameterMm: specs.PipeDiameterMm);
        voxChamber.BoolAdd(voxManifolds);

        // 5. Co-axial swirler injector head
        Voxels voxInjector = InjectorHeadBuilder.GenerateHead(physics, elementCount: elements);
        voxChamber.BoolAdd(voxInjector);

        // 6. Outer structural iso-grid stiffeners
        Voxels voxStructuralRibs = IsogridStiffenerBuilder.GenerateGrid(contour, wallMm: wall);
        voxChamber.BoolAdd(voxStructuralRibs);

        try { voxChamber.Fillet(0.35f); } catch { /* optional */ }
        return voxChamber;
    }

    public static Mesh BuildMesh(EngineSpecs specs) => BuildFlightGradeEngine(specs).mshAsMesh();

    public static (Mesh mesh, GasDynamicsResult physics) BuildWithPhysics(EngineSpecs specs)
    {
        var physics = GasDynamics.Calculate(
            specs.ThrustkN,
            specs.ChamberPressureBar,
            specs.AmbientPressurePa,
            specs.Gamma,
            specs.ChamberTempK,
            specs.MolarMass);
        return (BuildFlightGradeEngine(specs).mshAsMesh(), physics);
    }
}

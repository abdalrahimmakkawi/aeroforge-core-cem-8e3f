using System.Text.Json;
using AeroForge.Core;

namespace AlgorithmX.Core;

/// <summary>
/// Sole JSON contract between the LLM/orchestrator and Algorithm X.
/// The model must never emit mesh code — only this configuration.
/// </summary>
public sealed class EngineConfig
{
    public float ThrustkN { get; set; } = 50f;
    public float ChamberPressureBar { get; set; } = 60f;
    public float ExpansionRatio { get; set; } = 18.5f;
    public string CoolingType { get; set; } = "gyroid_tpms";
    public float GyroidCellSizeMm { get; set; } = 3.2f;
    public int ManifoldBranches { get; set; } = 64;
    public int SwirlerCount { get; set; } = 54;
    public float WallThicknessMm { get; set; } = 3.5f;
    public float PipeDiameterMm { get; set; } = 18f;
    public float AmbientPressurePa { get; set; } = 101325f;
    public float Gamma { get; set; } = 1.20f;
    public float ChamberTempK { get; set; } = 3550f;
    public float MolarMass { get; set; } = 21f;
    public string Propellant { get; set; } = "lox_methane";
    public float VoxelMm { get; set; } = 1.5f;

    public static EngineConfig FromJson(JsonElement root)
    {
        float GetF(params string[] names)
        {
            foreach (var n in names)
            {
                if (root.TryGetProperty(n, out var el) && el.ValueKind == JsonValueKind.Number)
                    return el.GetSingle();
                if (root.TryGetProperty("engine_config", out var nested) &&
                    nested.ValueKind == JsonValueKind.Object &&
                    nested.TryGetProperty(n, out var el2) && el2.ValueKind == JsonValueKind.Number)
                    return el2.GetSingle();
                if (root.TryGetProperty("engine_specs", out var specs) &&
                    specs.ValueKind == JsonValueKind.Object &&
                    specs.TryGetProperty(n, out var el3) && el3.ValueKind == JsonValueKind.Number)
                    return el3.GetSingle();
            }
            return float.NaN;
        }

        int GetI(params string[] names)
        {
            float v = GetF(names);
            return float.IsNaN(v) ? int.MinValue : (int)MathF.Round(v);
        }

        string GetS(params string[] names)
        {
            foreach (var n in names)
            {
                if (root.TryGetProperty(n, out var el) && el.ValueKind == JsonValueKind.String)
                    return el.GetString() ?? "";
                if (root.TryGetProperty("engine_config", out var nested) &&
                    nested.ValueKind == JsonValueKind.Object &&
                    nested.TryGetProperty(n, out var el2) && el2.ValueKind == JsonValueKind.String)
                    return el2.GetString() ?? "";
            }
            return "";
        }

        float thrust = GetF("thrust_kn", "ThrustkN");
        float bar = GetF("chamber_pressure_bar", "ChamberPressureBar");
        if (float.IsNaN(bar))
        {
            float mpa = GetF("chamber_pressure_mpa");
            if (!float.IsNaN(mpa)) bar = mpa * 10f;
        }

        var cfg = new EngineConfig
        {
            ThrustkN = Math.Clamp(float.IsNaN(thrust) ? 50f : thrust, 5f, 100f),
            ChamberPressureBar = Math.Clamp(float.IsNaN(bar) ? 60f : bar, 10f, 150f),
            ExpansionRatio = Math.Clamp(
                float.IsNaN(GetF("expansion_ratio", "ExpansionRatio"))
                    ? 18.5f
                    : GetF("expansion_ratio", "ExpansionRatio"),
                4f, 80f),
            CoolingType = string.IsNullOrWhiteSpace(GetS("cooling_type", "CoolingType"))
                ? "gyroid_tpms"
                : GetS("cooling_type", "CoolingType"),
            GyroidCellSizeMm = Math.Clamp(
                float.IsNaN(GetF("gyroid_cell_size_mm", "GyroidCellSizeMm"))
                    ? 3.2f
                    : GetF("gyroid_cell_size_mm", "GyroidCellSizeMm"),
                2.0f, 6.0f),
            ManifoldBranches = Math.Clamp(
                GetI("manifold_branches", "ManifoldBranches") == int.MinValue
                    ? 64
                    : GetI("manifold_branches", "ManifoldBranches"),
                32, 128),
            SwirlerCount = Math.Clamp(
                GetI("swirler_count", "SwirlerCount", "InjectorElementCount") == int.MinValue
                    ? 54
                    : GetI("swirler_count", "SwirlerCount", "InjectorElementCount"),
                36, 120),
            WallThicknessMm = Math.Clamp(
                float.IsNaN(GetF("wall_thickness_mm", "WallThicknessMm", "wall_mm"))
                    ? 3.5f
                    : GetF("wall_thickness_mm", "WallThicknessMm", "wall_mm"),
                3.0f, 8f),
            PipeDiameterMm = Math.Clamp(
                float.IsNaN(GetF("PipeDiameterMm", "pipe_diameter_mm"))
                    ? 18f
                    : GetF("PipeDiameterMm", "pipe_diameter_mm"),
                10f, 28f),
            AmbientPressurePa = float.IsNaN(GetF("AmbientPressurePa", "ambient_pressure_pa"))
                ? 101325f
                : GetF("AmbientPressurePa", "ambient_pressure_pa"),
            Gamma = float.IsNaN(GetF("Gamma", "gamma")) ? 1.20f : GetF("Gamma", "gamma"),
            ChamberTempK = float.IsNaN(GetF("ChamberTempK", "chamber_temp_k"))
                ? 3550f
                : GetF("ChamberTempK", "chamber_temp_k"),
            MolarMass = float.IsNaN(GetF("MolarMass", "molar_mass"))
                ? 21f
                : GetF("MolarMass", "molar_mass"),
            VoxelMm = Math.Clamp(
                float.IsNaN(GetF("VoxelMm", "voxel_mm")) ? 1.5f : GetF("VoxelMm", "voxel_mm"),
                0.8f, 2.0f),
        };

        string prop = GetS("propellant", "Propellant");
        if (!string.IsNullOrWhiteSpace(prop)) cfg.Propellant = prop;
        return cfg;
    }

    /// <summary>Bridge to legacy EngineSpecs for GasDynamics / RaoContour.</summary>
    public EngineSpecs ToEngineSpecs() => new()
    {
        ThrustkN = ThrustkN,
        ChamberPressureBar = ChamberPressureBar,
        CoolingChannelCount = 72,
        WallThicknessMm = WallThicknessMm,
        ChannelDepthMm = 2.5f,
        HelixTurns = 2.8f,
        PipeDiameterMm = PipeDiameterMm,
        InjectorElementCount = SwirlerCount,
        AmbientPressurePa = AmbientPressurePa,
        Gamma = Gamma,
        ChamberTempK = ChamberTempK,
        MolarMass = MolarMass,
        Propellant = Propellant,
        VoxelMm = VoxelMm,
        GyroidCellSizeMm = GyroidCellSizeMm,
        ManifoldBranches = ManifoldBranches,
        CoolingType = CoolingType,
    };
}

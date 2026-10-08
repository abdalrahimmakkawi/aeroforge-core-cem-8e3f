using System.Text.Json;
using AeroForge.Core;
using AeroForge.Core.Physics;
using AlgorithmX.Core;
using PicoGK;

namespace AeroForge.Geometry;

/// <summary>
/// CLI entry: EngineSpecs JSON → Algorithm X / AeroForge.Core → STL.
/// Modes:
///   {"mode":"algorithmx"|"core", ...}  ← Algorithm X generative (default regenerative)
///   {"mode":"flight", ...}             ← AeroForge.Core flight-grade CEM
///   {"mode":"script","csharp":"..."}   ← legacy Roslyn
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            string json = args.Length > 0 && File.Exists(args[0])
                ? File.ReadAllText(args[0])
                : (args.Length > 0 ? args[0] : throw new ArgumentException("Pass a JSON file path or JSON string."));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string mode = root.TryGetProperty("mode", out var m) ? m.GetString() ?? "algorithmx" : "algorithmx";
            string output = root.TryGetProperty("output", out var o)
                ? o.GetString() ?? "engine_output.stl"
                : "engine_output.stl";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

            float voxelMm = root.TryGetProperty("voxel_mm", out var v) ? v.GetSingle() : 1.5f;
            voxelMm = Math.Clamp(voxelMm, 0.5f, 2.0f);

            if (mode == "script")
            {
                string csharp = root.GetProperty("csharp").GetString()
                    ?? throw new InvalidOperationException("script mode requires csharp");
                PicoRunner.CompileAndRun(csharp, output, voxelMm);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    ok = true,
                    mode = "script",
                    output = Path.GetFullPath(output),
                    bytes = new FileInfo(output).Length
                }));
                return 0;
            }

            var specs = EngineSpecs.FromJson(root);
            specs.VoxelMm = voxelMm;
            string preset = root.TryGetProperty("preset", out var p) ? p.GetString() ?? "regenerative" : "regenerative";
            object? physicsSummary = null;
            long bytes;
            string resolvedMode = mode;

            {
                using var lib = new Library(voxelMm);
                Library.RegisterGlobalLibrary(lib);
                try
                {
                    Mesh mesh;
                    if (preset is "aerospike" or "swirler")
                    {
                        var legacy = EngineSpec.FromJson(root);
                        mesh = EngineBuilders.Build(lib, legacy);
                        resolvedMode = "builder";
                        // Report thermo-driven extents so the UI matches the forged mesh.
                        var phys = GasDynamics.Calculate(
                            specs.ThrustkN,
                            specs.ChamberPressureBar,
                            specs.AmbientPressurePa,
                            specs.Gamma,
                            specs.ChamberTempK,
                            specs.MolarMass);
                        physicsSummary = PhysicsSummary(phys, specs, preset);
                    }
                    else if (mode is "flight" or "core_flight")
                    {
                        var (msh, physics) = AeroForgeEngine.BuildWithPhysics(specs);
                        mesh = msh;
                        resolvedMode = "flight";
                        physicsSummary = PhysicsSummary(physics, specs, "flight");
                    }
                    else
                    {
                        // Default regenerative path: Algorithm X field-based synthesis.
                        var (msh, physics) = AlgorithmXMaster.GenerateWithPhysics(specs);
                        mesh = msh;
                        resolvedMode = "algorithmx";
                        physicsSummary = PhysicsSummary(physics, specs, "algorithmx");
                    }

                    mesh.SaveToStlFile(output);
                    bytes = new FileInfo(output).Length;
                }
                finally
                {
                    Library.UnregisterGlobalLibrary();
                }
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ok = true,
                mode = resolvedMode,
                preset,
                output = Path.GetFullPath(output),
                bytes,
                physics = physicsSummary,
            }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
            return 1;
        }
    }

    private static object PhysicsSummary(GasDynamicsResult physics, EngineSpecs specs, string engine)
    {
        return new
        {
            engine,
            throat_radius_mm = physics.rThroat,
            exit_radius_mm = physics.rExit,
            chamber_radius_mm = physics.rChamber,
            length_mm = physics.LengthMm,
            expansion_ratio = physics.ExpansionRatio,
            cf = physics.Cf,
            isp_s = physics.IspS,
            throat_area_m2 = physics.ThroatAreaM2,
            cooling_channels = specs.CoolingChannelCount,
            injector_elements = specs.InjectorElementCount,
        };
    }
}

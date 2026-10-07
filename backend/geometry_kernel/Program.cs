using System.Text.Json;
using AeroForge.Core;
using PicoGK;

namespace AeroForge.Geometry;

/// <summary>
/// CLI entry: reads EngineSpecs JSON, runs deterministic AeroForge.Core CEM, writes STL.
/// Modes:
///   {"mode":"core"|"builder", ...EngineSpecs fields..., "output":"..."}  ← preferred
///   {"mode":"script","csharp":"...","output":"..."}                     ← legacy Roslyn
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
            string mode = root.TryGetProperty("mode", out var m) ? m.GetString() ?? "core" : "core";
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

            // Primary path: deterministic AeroForge.Core CEM from EngineSpecs JSON.
            var specs = EngineSpecs.FromJson(root);
            specs.VoxelMm = voxelMm;
            string preset = root.TryGetProperty("preset", out var p) ? p.GetString() ?? "regenerative" : "regenerative";
            object? physicsSummary = null;
            long bytes;

            {
                using var lib = new Library(voxelMm);
                Library.RegisterGlobalLibrary(lib);
                try
                {
                    // Legacy aerospike / swirler presets still route through EngineBuilders.
                    Mesh mesh;
                    if (preset is "aerospike" or "swirler")
                    {
                        var legacy = EngineSpec.FromJson(root);
                        mesh = EngineBuilders.Build(lib, legacy);
                    }
                    else
                    {
                        var (msh, physics) = AeroForgeEngine.BuildWithPhysics(specs);
                        mesh = msh;
                        physicsSummary = new
                        {
                            throat_radius_mm = physics.rThroat,
                            exit_radius_mm = physics.rExit,
                            chamber_radius_mm = physics.rChamber,
                            length_mm = physics.LengthMm,
                            expansion_ratio = physics.ExpansionRatio,
                            cf = physics.Cf,
                            isp_s = physics.IspS,
                            throat_area_m2 = physics.ThroatAreaM2,
                            cooling_channels = specs.CoolingChannelCount,
                        };
                    }

                    mesh.SaveToStlFile(output);
                    bytes = new FileInfo(output).Length;
                }
                finally
                {
                    Library.UnregisterGlobalLibrary();
                }
            } // Library disposed here (may log to stdout)

            // Emit result JSON LAST so Python API parsers see it as the final line.
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ok = true,
                mode = "core",
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
}

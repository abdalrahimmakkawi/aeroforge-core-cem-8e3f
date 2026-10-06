using System.Text.Json;
using PicoGK;

namespace AeroForge.Geometry;

/// <summary>
/// CLI entry: reads a JSON payload, builds voxels with PicoGK + ShapeKernel, writes STL.
/// Modes:
///   {"mode":"builder","preset":"regenerative", ...geometry fields..., "output":"..."}
///   {"mode":"script","csharp":"...","output":"..."}
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
            string mode = root.TryGetProperty("mode", out var m) ? m.GetString() ?? "builder" : "builder";
            string output = root.TryGetProperty("output", out var o)
                ? o.GetString() ?? "engine_output.stl"
                : "engine_output.stl";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

            float voxelMm = root.TryGetProperty("voxel_mm", out var v) ? v.GetSingle() : 1.0f;
            voxelMm = Math.Clamp(voxelMm, 0.5f, 2.0f);

            if (mode == "script")
            {
                string csharp = root.GetProperty("csharp").GetString()
                    ?? throw new InvalidOperationException("script mode requires csharp");
                PicoRunner.CompileAndRun(csharp, output, voxelMm);
            }
            else
            {
                var spec = EngineSpec.FromJson(root);
                // LEAP 71 Library.Go-equivalent: register global library so
                // ShapeKernel BasePipe / Lattice / Voxels constructors resolve.
                using var lib = new Library(voxelMm);
                Library.RegisterGlobalLibrary(lib);
                try
                {
                    var mesh = EngineBuilders.Build(lib, spec);
                    mesh.SaveToStlFile(output);
                }
                finally
                {
                    Library.UnregisterGlobalLibrary();
                }
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ok = true,
                output = Path.GetFullPath(output),
                bytes = new FileInfo(output).Length
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

using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PicoGK;

namespace AeroForge.Geometry;

/// <summary>
/// Dynamically compiles AI-authored PicoGK / ShapeKernel C# with Roslyn
/// and executes it against the local OpenVDB runtime.
/// </summary>
public static class PicoRunner
{
    public interface IGeometryScript
    {
        Mesh Build(Library lib);
    }

    public static void CompileAndRun(string userCode, string outputStl, float voxelMm)
    {
        string wrapped = WrapUserCode(userCode);
        var compilation = CSharpCompilation.Create(
            assemblyName: "AeroForgeDynamic_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(wrapped) },
            references: GetReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithAllowUnsafe(false));

        using var pe = new MemoryStream();
        var result = compilation.Emit(pe);
        if (!result.Success)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Roslyn compile failed:");
            foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                var loc = d.Location.GetLineSpan();
                sb.AppendLine($"  L{loc.StartLinePosition.Line + 1}: {d.Id} {d.GetMessage()}");
            }
            throw new InvalidOperationException(sb.ToString());
        }

        pe.Position = 0;
        var alc = new AssemblyLoadContext("aeroforge-dyn", isCollectible: true);
        Assembly asm = alc.LoadFromStream(pe);
        Type type = asm.GetType("AeroForge.Dynamic.GeneratedScript")
            ?? throw new InvalidOperationException("GeneratedScript type missing");
        var instance = (IGeometryScript)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Could not construct GeneratedScript"));

        // Mirror LEAP 71 Library.Go pattern: register global library for ShapeKernel
        // constructors that use parameterless Voxels/Lattice/Mesh APIs.
        using var lib = new Library(Math.Clamp(voxelMm, 0.5f, 2.0f));
        Library.RegisterGlobalLibrary(lib);
        try
        {
            Mesh mesh = instance.Build(lib);
            mesh.SaveToStlFile(outputStl);
        }
        finally
        {
            Library.UnregisterGlobalLibrary();
            alc.Unload();
        }
    }

    private static string WrapUserCode(string userCode)
    {
        return $$"""
            using System;
            using System.Numerics;
            using System.Collections.Generic;
            using System.Linq;
            using PicoGK;
            using Leap71.ShapeKernel;
            using Leap71.LatticeLibrary;
            using AeroForge.Geometry;

            namespace AeroForge.Dynamic
            {
                public sealed class GeneratedScript : PicoRunner.IGeometryScript
                {
                    public Mesh Build(Library lib)
                    {
            {{Indent(userCode, 24)}}
                    }
                }
            }
            """;
    }

    private static string Indent(string code, int spaces)
    {
        string pad = new string(' ', spaces);
        var lines = code.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Select(l => pad + l));
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var list = new List<MetadataReference>();
        void AddAsm(Assembly a)
        {
            if (!string.IsNullOrEmpty(a.Location))
                list.Add(MetadataReference.CreateFromFile(a.Location));
        }

        AddAsm(typeof(object).Assembly);
        AddAsm(typeof(Console).Assembly);
        AddAsm(typeof(Enumerable).Assembly);
        AddAsm(typeof(List<>).Assembly);
        AddAsm(typeof(Vector3).Assembly);
        AddAsm(typeof(Library).Assembly);
        AddAsm(typeof(Leap71.ShapeKernel.BasePipe).Assembly);
        AddAsm(typeof(PicoRunner).Assembly);

        string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (tpa is not null)
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name is "System.Runtime" or "netstandard" or "System.Collections"
                    or "System.Linq" or "System.Console" or "System.Private.CoreLib"
                    or "System.Numerics" or "System.Numerics.Vectors" or "System.Collections.Concurrent")
                {
                    list.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }
        return list.ToImmutableArray();
    }
}

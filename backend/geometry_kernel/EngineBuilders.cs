using System.Numerics;
using System.Text.Json;
using PicoGK;

namespace AeroForge.Geometry;

public sealed class EngineSpec
{
    public string Preset { get; init; } = "aerospike";
    public float ThrustKN { get; init; } = 20f;
    public float ChamberPressureMPa { get; init; } = 3.5f;
    public float ThroatRadiusMm { get; init; } = 35f;
    public float ExitRadiusMm { get; init; } = 90f;
    public float LengthMm { get; init; } = 220f;
    public float ExpansionRatio { get; init; } = 8f;
    public int ChannelCount { get; init; } = 18;
    public float ChannelRadiusMm { get; init; } = 1.4f;
    public float WallMm { get; init; } = 2.2f;
    public float HelixTurns { get; init; } = 2.5f;

    public static EngineSpec FromJson(JsonElement root)
    {
        float GetF(string name, float fallback) =>
            root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
                ? el.GetSingle()
                : fallback;

        int GetI(string name, int fallback) =>
            root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
                ? el.GetInt32()
                : fallback;

        string preset = root.TryGetProperty("preset", out var p) ? p.GetString() ?? "aerospike" : "aerospike";
        return new EngineSpec
        {
            Preset = preset,
            ThrustKN = GetF("thrust_kn", 20f),
            ChamberPressureMPa = GetF("chamber_pressure_mpa", 3.5f),
            ThroatRadiusMm = GetF("throat_radius_mm", 35f),
            ExitRadiusMm = GetF("exit_radius_mm", 90f),
            LengthMm = GetF("length_mm", 220f),
            ExpansionRatio = GetF("expansion_ratio", 8f),
            ChannelCount = GetI("channel_count", 18),
            ChannelRadiusMm = GetF("channel_radius_mm", 1.4f),
            WallMm = GetF("wall_mm", 2.2f),
            HelixTurns = GetF("helix_turns", 2.5f),
        };
    }
}

public delegate float SdfEval(in Vector3 vec);

/// <summary>SDF field with an explicit bounding box for PicoGK voxelization.</summary>
public sealed class SdfField : IBoundedImplicit
{
    private readonly SdfEval _sdf;
    public BBox3 oBounds { get; }

    public SdfField(BBox3 bounds, SdfEval sdf)
    {
        oBounds = bounds;
        _sdf = sdf;
    }

    public float fSignedDistance(in Vector3 vec) => _sdf(in vec);
}

public static class EngineBuilders
{
    public static Mesh Build(Library lib, EngineSpec spec) =>
        spec.Preset.ToLowerInvariant() switch
        {
            "swirler" => BuildSwirler(lib, spec),
            "regenerative" or "regen" => BuildRegenBell(lib, spec),
            _ => BuildAerospike(lib, spec),
        };

    public static Mesh BuildAerospike(Library lib, EngineSpec spec)
    {
        float L = MathF.Max(spec.LengthMm, 80f);
        float rt = MathF.Max(spec.ThroatRadiusMm, 12f);
        float re = MathF.Max(spec.ExitRadiusMm, rt * 1.6f);
        float wall = MathF.Max(spec.WallMm, 1.4f);

        var plug = new SdfField(
            new BBox3(new Vector3(-re - wall, -re - wall, -wall), new Vector3(re + wall, re + wall, L + wall)),
            (in Vector3 vec) =>
            {
                float z = vec.Z;
                float r = MathF.Sqrt(vec.X * vec.X + vec.Y * vec.Y);
                float t = Math.Clamp(z / L, 0f, 1f);
                float rPlug = re * (1f - t * t) * 0.92f + rt * 0.08f;
                float undulate = 1.8f * MathF.Sin(6f * MathF.Atan2(vec.Y, vec.X) + 3f * t);
                rPlug = MathF.Max(rPlug + undulate, rt * 0.35f);
                float sdf = r - rPlug;
                float core = (rt * 0.28f) - r;
                float body = MathF.Max(sdf, -core);
                float zShell = MathF.Max(-z - wall * 0.2f, z - L);
                return MathF.Max(body, zShell);
            });

        Voxels vox = new(lib, plug);

        int n = Math.Clamp(spec.ChannelCount, 8, 36);
        float tubeR = MathF.Max(spec.ChannelRadiusMm, 0.8f);
        for (int i = 0; i < n; i++)
        {
            float phase = 2f * MathF.PI * i / n;
            var lat = new Lattice(lib);
            Vector3? prev = null;
            const int samples = 40;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                float z = t * L * 0.96f + 2f;
                float rPlug = re * (1f - t * t) * 0.92f + rt * 0.08f;
                float theta = phase + spec.HelixTurns * 2f * MathF.PI * t;
                float rr = MathF.Max(rPlug - wall * 0.55f, tubeR * 2f);
                var p = new Vector3(rr * MathF.Cos(theta), rr * MathF.Sin(theta), z);
                if (prev is Vector3 pv)
                    lat.AddBeam(pv, p, tubeR, tubeR, false);
                prev = p;
            }
            vox.BoolSubtract(new Voxels(lat));
        }

        var collar = new SdfField(
            new BBox3(new Vector3(-re - wall * 2f, -re - wall * 2f, -2f), new Vector3(re + wall * 2f, re + wall * 2f, 28f)),
            (in Vector3 vec) =>
            {
                float r = MathF.Sqrt(vec.X * vec.X + vec.Y * vec.Y);
                float ring = MathF.Abs(r - (re * 0.75f)) - wall * 1.2f;
                float gy = MathF.Sin(vec.X * 0.35f) * MathF.Cos(vec.Y * 0.35f)
                         + MathF.Sin(vec.Y * 0.35f) * MathF.Cos(vec.Z * 0.35f)
                         + MathF.Sin(vec.Z * 0.35f) * MathF.Cos(vec.X * 0.35f);
                float cell = MathF.Abs(gy) - 0.35f;
                float zBand = MathF.Max(-vec.Z - 1f, vec.Z - 26f);
                return MathF.Max(MathF.Max(ring, cell), zBand);
            });
        vox.BoolAdd(new Voxels(lib, collar));
        return vox.mshAsMesh();
    }

    public static Mesh BuildRegenBell(Library lib, EngineSpec spec)
    {
        float L = MathF.Max(spec.LengthMm, 120f);
        float rt = MathF.Max(spec.ThroatRadiusMm, 15f);
        float re = MathF.Max(spec.ExitRadiusMm, rt * MathF.Sqrt(MathF.Max(spec.ExpansionRatio, 4f)));
        float wall = MathF.Max(spec.WallMm, 1.6f);
        float rc = rt * 2.4f;

        var shell = new SdfField(
            new BBox3(new Vector3(-re - wall * 2, -re - wall * 2, -5), new Vector3(re + wall * 2, re + wall * 2, L + 5)),
            (in Vector3 vec) =>
            {
                float z = vec.Z;
                float r = MathF.Sqrt(vec.X * vec.X + vec.Y * vec.Y);
                float t = Math.Clamp(z / L, 0f, 1f);
                float rGas;
                if (t < 0.28f)
                    rGas = rc;
                else if (t < 0.42f)
                {
                    float u = (t - 0.28f) / 0.14f;
                    rGas = rc + (rt - rc) * (3 * u * u - 2 * u * u * u);
                }
                else
                {
                    float u = (t - 0.42f) / 0.58f;
                    rGas = rt + (re - rt) * (u * u);
                }
                float inner = rGas - r;
                float outer = r - (rGas + wall);
                float body = MathF.Max(inner, outer);
                float zShell = MathF.Max(-z, z - L);
                float ribs = 0.9f * MathF.Sin(10f * MathF.Atan2(vec.Y, vec.X) + 2f * t);
                return MathF.Max(body - MathF.Max(ribs, 0f) * 0.15f, zShell);
            });

        Voxels vox = new(lib, shell);
        int n = Math.Clamp(spec.ChannelCount, 10, 40);
        float tubeR = MathF.Max(spec.ChannelRadiusMm, 0.9f);
        for (int i = 0; i < n; i++)
        {
            float phase = 2f * MathF.PI * i / n;
            var lat = new Lattice(lib);
            Vector3? prev = null;
            for (int s = 0; s <= 48; s++)
            {
                float t = s / 48f;
                float z = t * L * 0.98f;
                float rGas;
                if (t < 0.28f) rGas = rc;
                else if (t < 0.42f)
                {
                    float u = (t - 0.28f) / 0.14f;
                    rGas = rc + (rt - rc) * (3 * u * u - 2 * u * u * u);
                }
                else
                {
                    float u = (t - 0.42f) / 0.58f;
                    rGas = rt + (re - rt) * (u * u);
                }
                float theta = phase + spec.HelixTurns * 2f * MathF.PI * t;
                float rr = rGas + wall * 0.5f;
                var p = new Vector3(rr * MathF.Cos(theta), rr * MathF.Sin(theta), z);
                if (prev is Vector3 pv)
                    lat.AddBeam(pv, p, tubeR, tubeR, false);
                prev = p;
            }
            vox.BoolSubtract(new Voxels(lat));
        }
        return vox.mshAsMesh();
    }

    public static Mesh BuildSwirler(Library lib, EngineSpec spec)
    {
        float R = MathF.Max(spec.ThroatRadiusMm * 1.8f, 40f);
        float H = MathF.Max(spec.LengthMm * 0.45f, 60f);
        float wall = MathF.Max(spec.WallMm, 1.5f);
        var body = new SdfField(
            new BBox3(new Vector3(-R - 10, -R - 10, -5), new Vector3(R + 10, R + 10, H + 5)),
            (in Vector3 vec) =>
            {
                float r = MathF.Sqrt(vec.X * vec.X + vec.Y * vec.Y);
                float cyl = MathF.Max(r - R, (R - wall * 3f) - r);
                float zShell = MathF.Max(-vec.Z, vec.Z - H);
                return MathF.Max(cyl, zShell);
            });
        Voxels vox = new(lib, body);
        int blades = Math.Clamp(spec.ChannelCount / 2, 6, 16);
        for (int i = 0; i < blades; i++)
        {
            float phase = 2f * MathF.PI * i / blades;
            var lat = new Lattice(lib);
            Vector3? prev = null;
            for (int s = 0; s <= 36; s++)
            {
                float t = s / 36f;
                float z = t * H;
                float theta = phase + 1.4f * t * MathF.PI;
                float rr = R - wall * 1.5f;
                var p = new Vector3(rr * MathF.Cos(theta), rr * MathF.Sin(theta), z);
                if (prev is Vector3 pv)
                    lat.AddBeam(pv, p, wall * 0.55f, wall * 0.35f, false);
                prev = p;
            }
            vox.BoolSubtract(new Voxels(lat));
        }
        return vox.mshAsMesh();
    }
}

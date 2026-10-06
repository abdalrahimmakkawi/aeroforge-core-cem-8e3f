using System.Numerics;
using System.Text.Json;
using Leap71.ShapeKernel;
using PicoGK;

namespace AeroForge.Geometry;

public sealed class EngineSpec
{
    public string Preset { get; init; } = "regenerative";
    public float ThrustKN { get; init; } = 50f;
    public float ChamberPressureMPa { get; init; } = 3.5f;
    public float ThroatRadiusMm { get; init; } = 35f;
    public float ExitRadiusMm { get; init; } = 90f;
    public float LengthMm { get; init; } = 280f;
    public float ExpansionRatio { get; init; } = 8f;
    public int ChannelCount { get; init; } = 18;
    public float ChannelRadiusMm { get; init; } = 1.4f;
    public float WallMm { get; init; } = 2.4f;
    public float HelixTurns { get; init; } = 2.8f;

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

        string preset = root.TryGetProperty("preset", out var p) ? p.GetString() ?? "regenerative" : "regenerative";
        return new EngineSpec
        {
            Preset = preset,
            ThrustKN = GetF("thrust_kn", 50f),
            ChamberPressureMPa = GetF("chamber_pressure_mpa", 3.5f),
            ThroatRadiusMm = GetF("throat_radius_mm", 35f),
            ExitRadiusMm = GetF("exit_radius_mm", 90f),
            LengthMm = GetF("length_mm", 280f),
            ExpansionRatio = GetF("expansion_ratio", 8f),
            ChannelCount = GetI("channel_count", 18),
            ChannelRadiusMm = GetF("channel_radius_mm", 1.4f),
            WallMm = GetF("wall_mm", 2.4f),
            HelixTurns = GetF("helix_turns", 2.8f),
        };
    }
}

/// <summary>
/// Production builders using LEAP 71 ShapeKernel BasePipe / LatticePipe /
/// ControlPointSpline contours — continuous watertight engine bodies.
/// </summary>
public static class EngineBuilders
{
    public static Mesh Build(Library lib, EngineSpec spec) =>
        spec.Preset.ToLowerInvariant() switch
        {
            "swirler" => BuildSwirler(lib, spec),
            "aerospike" => BuildAerospike(lib, spec),
            _ => BuildRegenBell(lib, spec),
        };

    /// <summary>
    /// Continuous injector → chamber → throat → Rao/parabolic nozzle with
    /// conformal helical regenerative cooling channels carved from the wall.
    /// </summary>
    public static Mesh BuildRegenBell(Library lib, EngineSpec spec)
    {
        float L = MathF.Max(spec.LengthMm, 160f);
        float rt = MathF.Max(spec.ThroatRadiusMm, 12f);
        float re = MathF.Max(spec.ExitRadiusMm, rt * MathF.Sqrt(MathF.Max(spec.ExpansionRatio, 4f)));
        float wall = MathF.Max(spec.WallMm, 1.8f);
        float rc = rt * 2.35f; // chamber radius

        // B-spline control points for gas-side wall radius vs length ratio.
        // z: (injector) chamber → convergent → throat → divergent exit
        var gasControls = new List<Vector3>
        {
            new(0f, rc, 0f),
            new(0.18f, rc, 0f),
            new(0.30f, rc * 0.92f, 0f),
            new(0.38f, (rc + rt) * 0.55f, 0f),
            new(0.44f, rt, 0f),                 // throat
            new(0.52f, rt * 1.08f, 0f),
            new(0.70f, rt + (re - rt) * 0.45f, 0f),
            new(0.88f, rt + (re - rt) * 0.82f, 0f),
            new(1.00f, re, 0f),
        };
        var gasSpline = new ControlPointSpline(gasControls, nDegree: 3);

        float GasRadius(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            // Sample spline at parameter t via control interpolation helper
            var pts = gasSpline.aGetPoints(64u);
            float idx = t * (pts.Count - 1);
            int i0 = Math.Clamp((int)MathF.Floor(idx), 0, pts.Count - 2);
            float u = idx - i0;
            return pts[i0].Y * (1f - u) + pts[i0 + 1].Y * u;
        }

        var frame = new LocalFrame(new Vector3(0, 0, 0));
        var pipe = new BasePipe(frame, L, rt * 0.5f, rt + wall);
        pipe.SetLengthSteps(160);
        pipe.SetPolarSteps(72);
        pipe.SetRadialSteps(5);
        pipe.SetRadius(
            new SurfaceModulation(new LineModulation(t => MathF.Max(GasRadius(t) - 0.15f, 1.0f))),
            new SurfaceModulation(new LineModulation(t => GasRadius(t) + wall))
        );

        Voxels vox = pipe.voxConstruct();

        // Conformal helical cooling channels — follow outer wall mid-plane.
        // Keep tube diameter well under wall thickness so channels stay enclosed
        // (oversize beams punch through and look like disconnected rings).
        int n = Math.Clamp(spec.ChannelCount, 10, 28);
        float tubeR = Math.Min(Math.Max(spec.ChannelRadiusMm, 0.6f), wall * 0.32f);
        var channelLattice = new Lattice();
        for (int i = 0; i < n; i++)
        {
            float phase = 2f * MathF.PI * i / n;
            Vector3? prev = null;
            const int samples = 80;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                float z = t * L;
                float rGas = GasRadius(t);
                float theta = phase + spec.HelixTurns * 2f * MathF.PI * t;
                float rr = rGas + wall * 0.50f;
                var p = new Vector3(rr * MathF.Cos(theta), rr * MathF.Sin(theta), z);
                if (prev is Vector3 pv)
                    channelLattice.AddBeam(pv, tubeR, p, tubeR, false);
                prev = p;
            }
        }
        vox = vox.voxBoolSubtract(new Voxels(channelLattice));

        // Light outer fillet for a continuous print-ready jacket
        try { vox.Fillet(0.6f); } catch { /* optional */ }

        // Inlet manifold ring — fused to chamber OD (not floating)
        float manR = GasRadius(0.05f) + wall * 0.50f;
        float manBeam = Math.Min(tubeR * 1.35f, wall * 0.38f);
        var manifold = new BaseRing(
            new LocalFrame(new Vector3(0, 0, L * 0.04f)),
            manR,
            manBeam);
        manifold.SetPolarSteps(64);
        manifold.SetRadialSteps(40);
        vox = vox.voxBoolAdd(manifold.voxConstruct());

        return vox.mshAsMesh();
    }

    /// <summary>
    /// Truncated aerospike plug with continuous ContourPointSpline radius and
    /// conformal spiral cooling passages.
    /// </summary>
    public static Mesh BuildAerospike(Library lib, EngineSpec spec)
    {
        float L = MathF.Max(spec.LengthMm, 140f);
        float rt = MathF.Max(spec.ThroatRadiusMm, 12f);
        float re = MathF.Max(spec.ExitRadiusMm, rt * 1.7f);
        float wall = MathF.Max(spec.WallMm, 1.8f);

        // Spike radius from base (re) tapering toward tip (near 0) — continuous spline.
        var plugControls = new List<Vector3>
        {
            new(0f, re, 0f),
            new(0.15f, re * 0.92f, 0f),
            new(0.35f, re * 0.68f, 0f),
            new(0.55f, re * 0.42f, 0f),
            new(0.75f, rt * 0.55f, 0f),
            new(0.90f, rt * 0.28f, 0f),
            new(1.00f, rt * 0.12f, 0f),
        };
        var plugSpline = new ControlPointSpline(plugControls, nDegree: 3);

        float PlugRadius(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            var pts = plugSpline.aGetPoints(48u);
            float idx = t * (pts.Count - 1);
            int i0 = Math.Clamp((int)MathF.Floor(idx), 0, pts.Count - 2);
            float u = idx - i0;
            return MathF.Max(pts[i0].Y * (1f - u) + pts[i0 + 1].Y * u, 1.2f);
        }

        // Solid-ish plug as pipe with tiny inner bore for printability / cooling feed.
        var frame = new LocalFrame();
        var pipe = new BasePipe(frame, L, 0.8f, re);
        pipe.SetLengthSteps(140);
        pipe.SetPolarSteps(72);
        pipe.SetRadius(
            new SurfaceModulation(new LineModulation(_ => 0.8f)),
            new SurfaceModulation(new LineModulation(t => PlugRadius(t)))
        );
        Voxels vox = pipe.voxConstruct();

        int n = Math.Clamp(spec.ChannelCount, 8, 24);
        float tubeR = Math.Min(Math.Max(spec.ChannelRadiusMm, 0.6f), wall * 0.32f);
        var lat = new Lattice();
        for (int i = 0; i < n; i++)
        {
            float phase = 2f * MathF.PI * i / n;
            Vector3? prev = null;
            const int samples = 64;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                float z = t * L * 0.97f;
                float rPlug = PlugRadius(t);
                float theta = phase + spec.HelixTurns * 2f * MathF.PI * t;
                float rr = MathF.Max(rPlug - wall * 0.50f, tubeR * 2.5f);
                var p = new Vector3(rr * MathF.Cos(theta), rr * MathF.Sin(theta), z);
                if (prev is Vector3 pv)
                    lat.AddBeam(pv, tubeR, p, tubeR, false);
                prev = p;
            }
        }
        vox = vox.voxBoolSubtract(new Voxels(lat));
        try { vox.Fillet(0.5f); } catch { /* optional */ }
        return vox.mshAsMesh();
    }

    public static Mesh BuildSwirler(Library lib, EngineSpec spec)
    {
        float R = MathF.Max(spec.ThroatRadiusMm * 1.9f, 36f);
        float H = MathF.Max(spec.LengthMm * 0.4f, 55f);
        float wall = MathF.Max(spec.WallMm, 1.6f);

        var frame = new LocalFrame();
        var pipe = new BasePipe(frame, H, R - wall * 2.8f, R);
        pipe.SetLengthSteps(80);
        pipe.SetPolarSteps(72);
        Voxels vox = pipe.voxConstruct();

        int blades = Math.Clamp(spec.ChannelCount / 2, 6, 16);
        var lat = new Lattice();
        for (int i = 0; i < blades; i++)
        {
            float phase = 2f * MathF.PI * i / blades;
            Vector3? prev = null;
            for (int s = 0; s <= 40; s++)
            {
                float t = s / 40f;
                float z = t * H;
                float theta = phase + 1.35f * t * MathF.PI;
                float rr = R - wall * 1.4f;
                var p = new Vector3(rr * MathF.Cos(theta), rr * MathF.Sin(theta), z);
                if (prev is Vector3 pv)
                    lat.AddBeam(pv, wall * 0.5f, p, wall * 0.35f, false);
                prev = p;
            }
        }
        vox = vox.voxBoolSubtract(new Voxels(lat));
        return vox.mshAsMesh();
    }
}

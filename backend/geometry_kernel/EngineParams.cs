using System.Numerics;

namespace AeroForge.Geometry;

/// <summary>
/// Sizing inputs for the 6-step multi-component engine assembly pipeline.
/// </summary>
public sealed class EngineParams
{
    public float rThroat { get; init; } = 35f;
    public float rExit { get; init; } = 90f;
    public float rInjector { get; init; } = 82f;
    public float length { get; init; } = 280f;
    public float wallMm { get; init; } = 3.5f;
    public int channelCount { get; init; } = 48;
    public float channelWidth { get; init; } = 2.0f;
    public float helixTurns { get; init; } = 2.6f;
    public float topManifoldPipeR { get; init; } = 12f;
    public float bottomManifoldPipeR { get; init; } = 10f;
    public int boltCount { get; init; } = 8;
    public float expansionRatio { get; init; } = 8f;

    public static EngineParams FromSpec(EngineSpec spec)
    {
        float rt = MathF.Max(spec.ThroatRadiusMm, 12f);
        float re = MathF.Max(spec.ExitRadiusMm, rt * MathF.Sqrt(MathF.Max(spec.ExpansionRatio, 4f)));
        float L = MathF.Max(spec.LengthMm, 160f);
        float wall = MathF.Max(spec.WallMm, 3.0f);
        // Prefer structural 3.5 mm jacket when caller left a thin default.
        if (wall < 3.2f) wall = 3.5f;
        float rInj = rt * 2.35f;
        int channels = Math.Clamp(spec.ChannelCount * 2, 24, 48);
        float chW = Math.Min(Math.Max(spec.ChannelRadiusMm * 1.4f, 1.2f), wall * 0.55f);

        return new EngineParams
        {
            rThroat = rt,
            rExit = re,
            rInjector = rInj,
            length = L,
            wallMm = wall,
            channelCount = channels,
            channelWidth = chW,
            helixTurns = MathF.Max(spec.HelixTurns, 2.2f),
            topManifoldPipeR = Math.Clamp(wall * 3.2f, 8f, 14f),
            bottomManifoldPipeR = Math.Clamp(wall * 2.6f, 7f, 12f),
            boltCount = 8,
            expansionRatio = MathF.Max(spec.ExpansionRatio, 4f),
        };
    }
}

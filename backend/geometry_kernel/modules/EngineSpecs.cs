using System.Numerics;

namespace AeroForge.Modules;

/// <summary>
/// Clean sizing pack consumed by modular CEM builders (LLM assembler output).
/// </summary>
public sealed class EngineSpecs
{
    public float rThroat { get; init; } = 35f;
    public float rExit { get; init; } = 90f;
    public float rInjector { get; init; } = 82f;
    public float length { get; init; } = 280f;
    public float wallMm { get; init; } = 3.5f;
    public float expansionRatio { get; init; } = 8f;

    public static EngineSpecs FromGeometry(
        float throatRadiusMm,
        float exitRadiusMm,
        float lengthMm,
        float expansionRatio = 8f,
        float wallMm = 3.5f)
    {
        float rt = MathF.Max(throatRadiusMm, 12f);
        float re = MathF.Max(exitRadiusMm, rt * MathF.Sqrt(MathF.Max(expansionRatio, 4f)));
        return new EngineSpecs
        {
            rThroat = rt,
            rExit = re,
            rInjector = rt * 2.35f,
            length = MathF.Max(lengthMm, 140f),
            wallMm = MathF.Max(wallMm, 3.0f),
            expansionRatio = MathF.Max(expansionRatio, 4f),
        };
    }
}

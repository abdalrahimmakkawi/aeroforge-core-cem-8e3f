using System.Numerics;
using Leap71.ShapeKernel;
using PicoGK;

namespace Leap71.ShapeKernel;

/// <summary>
/// AeroForge extensions to ShapeKernel <see cref="Sh"/> — contour revolution
/// used by the 6-step engine assembly pipeline.
/// </summary>
public partial class Sh
{
    /// <summary>
    /// Revolve an RZ profile (X=Z axial mm, Y=radius mm) into a solid of revolution
    /// from the axis out to the profile radius (gas-path volume or solid body).
    /// </summary>
    public static Voxels voxRevolveContour(List<Vector2> profile, float innerRadius = 0f)
    {
        if (profile == null || profile.Count < 2)
            throw new ArgumentException("Profile needs at least 2 (Z,R) samples.");

        float z0 = profile[0].X;
        float z1 = profile[^1].X;
        float length = MathF.Max(z1 - z0, 1f);
        float rMax = profile.Max(p => p.Y) + 2f;

        float RadiusAt(float t)
        {
            float z = z0 + t * length;
            return AeroForge.Geometry.ProfileHelper.RadiusAtZ(profile, z);
        }

        var frame = new LocalFrame(new Vector3(0, 0, z0));
        var pipe = new BasePipe(frame, length, MathF.Max(innerRadius, 0f), rMax);
        pipe.SetLengthSteps((uint)Math.Clamp(profile.Count * 2, 80, 200));
        pipe.SetPolarSteps(72);
        pipe.SetRadialSteps(5);
        pipe.SetRadius(
            new SurfaceModulation(new LineModulation(_ => MathF.Max(innerRadius, 0f))),
            new SurfaceModulation(new LineModulation(t => MathF.Max(RadiusAt(t), innerRadius + 0.5f)))
        );
        return pipe.voxConstruct();
    }
}

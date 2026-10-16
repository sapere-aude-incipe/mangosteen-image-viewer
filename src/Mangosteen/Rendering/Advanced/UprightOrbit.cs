using System.Windows.Media.Media3D;

namespace Mangosteen.Rendering.Advanced;

internal static class UprightOrbit
{
    internal const double MaximumPitch = 89;

    public static bool TryRotate(Vector3D offset, double yawDegrees, double pitchDegrees, out Vector3D rotated)
    {
        rotated = offset;
        var radius = offset.Length;
        if (!double.IsFinite(radius) || radius <= 1e-12 ||
            !double.IsFinite(yawDegrees) || !double.IsFinite(pitchDegrees)) return false;

        // World Y remains up: using the camera's local up axis would roll the floor.
        var yaw = Math.Atan2(offset.X, offset.Z) + Math.IEEERemainder(yawDegrees, 360) * Math.PI / 180;
        var pitch = Math.Asin(Math.Clamp(offset.Y / radius, -1, 1)) * 180 / Math.PI;
        pitch = Math.Clamp(pitch + pitchDegrees, -MaximumPitch, MaximumPitch) * Math.PI / 180;
        var horizontalRadius = radius * Math.Cos(pitch);
        rotated = new Vector3D(horizontalRadius * Math.Sin(yaw), radius * Math.Sin(pitch), horizontalRadius * Math.Cos(yaw));
        return true;
    }
}

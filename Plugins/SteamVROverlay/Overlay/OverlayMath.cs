using System.Numerics;
using Valve.VR;

namespace SteamVROverlay.Overlay;

/// <summary>
/// Conversions between OpenVR's row-major 3x4 pose matrices and <see cref="Matrix4x4"/>.
/// </summary>
public static class OverlayMath
{
    /// <summary>
    /// Builds the overlay-to-controller transform from translation offsets and intrinsic
    /// pitch/yaw/roll, applied in that order about the overlay's own axes.
    /// </summary>
    public static HmdMatrix34_t BuildLocalTransform(
        float offsetX,
        float offsetY,
        float offsetZ,
        float pitchDegrees,
        float yawDegrees,
        float rollDegrees)
    {
        var rotation = Matrix4x4.CreateFromYawPitchRoll(
            DegreesToRadians(yawDegrees),
            DegreesToRadians(pitchDegrees),
            DegreesToRadians(rollDegrees));

        rotation.Translation = new Vector3(offsetX, offsetY, offsetZ);
        return ToHmdMatrix(rotation);
    }

    /// <summary>
    /// Converts an OpenVR pose into a <see cref="Matrix4x4"/>.
    /// OpenVR stores basis vectors in columns of a row-major 3x4 matrix; <see cref="Matrix4x4"/>
    /// stores them in rows, so the two layouts are transposes of each other.
    /// </summary>
    public static Matrix4x4 ToMatrix4x4(in HmdMatrix34_t m) => new(
        m.m0, m.m4, m.m8, 0f,
        m.m1, m.m5, m.m9, 0f,
        m.m2, m.m6, m.m10, 0f,
        m.m3, m.m7, m.m11, 1f);

    public static HmdMatrix34_t ToHmdMatrix(in Matrix4x4 m) => new()
    {
        m0 = m.M11,
        m1 = m.M21,
        m2 = m.M31,
        m3 = m.M41,
        m4 = m.M12,
        m5 = m.M22,
        m6 = m.M32,
        m7 = m.M42,
        m8 = m.M13,
        m9 = m.M23,
        m10 = m.M33,
        m11 = m.M43,
    };

    /// <summary>
    /// The face normal of an overlay, which lies in the XY plane and faces +Z.
    /// </summary>
    public static Vector3 ForwardOf(in Matrix4x4 m) => Vector3.Normalize(new Vector3(m.M31, m.M32, m.M33));

    public static Vector3 PositionOf(in Matrix4x4 m) => m.Translation;

    /// <summary>
    /// Intersects a ray with a flat, square overlay of the given width.
    /// </summary>
    /// <param name="overlayToWorld">The overlay's pose; its face lies in the local XY plane.</param>
    /// <param name="uv">Hit position with (0, 0) at the texture's top-left and (1, 1) at its bottom-right.</param>
    /// <param name="distance">Distance along the ray to the hit, in metres.</param>
    public static bool TryIntersect(
        in Matrix4x4 overlayToWorld,
        float widthMeters,
        Vector3 rayOrigin,
        Vector3 rayDirection,
        float maxDistance,
        out Vector2 uv,
        out float distance)
    {
        uv = default;
        distance = 0f;

        if (!Matrix4x4.Invert(overlayToWorld, out var worldToOverlay))
        {
            return false;
        }

        var localOrigin = Vector3.Transform(rayOrigin, worldToOverlay);
        var localDirection = Vector3.TransformNormal(Vector3.Normalize(rayDirection), worldToOverlay);
        if (MathF.Abs(localDirection.Z) < 1e-6f)
        {
            return false;
        }

        var t = -localOrigin.Z / localDirection.Z;
        if (t <= 0f || t > maxDistance)
        {
            return false;
        }

        var hit = localOrigin + localDirection * t;
        var u = hit.X / widthMeters + 0.5f;
        var v = 0.5f - hit.Y / widthMeters;
        if (u is < 0f or > 1f || v is < 0f or > 1f)
        {
            return false;
        }

        uv = new Vector2(u, v);
        distance = t;
        return true;
    }

    /// <summary>
    /// The direction a pointing pose aims along. OpenVR poses point down their local -Z.
    /// </summary>
    public static Vector3 AimOf(in Matrix4x4 m) => -ForwardOf(m);

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);
}

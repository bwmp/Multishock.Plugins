using System.Numerics;
using Valve.VR;

namespace SteamVROverlay.Overlay;

/// <summary>
/// A panel's placement on its controller, in the terms the settings store it.
/// </summary>
public readonly record struct OverlayPlacement(
    float OffsetX,
    float OffsetY,
    float OffsetZ,
    float PitchDegrees,
    float YawDegrees,
    float RollDegrees);

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
    /// The inverse of <see cref="BuildLocalTransform"/>. Pointing straight up or down, yaw and
    /// roll turn about the same axis, so the turn is reported as yaw with no roll.
    /// </summary>
    public static OverlayPlacement DecomposeLocalTransform(in Matrix4x4 local)
    {
        // CreateFromYawPitchRoll composes roll, then pitch, then yaw, which puts -sin(pitch) at
        // M32, cos(pitch) times yaw's sine and cosine at M31 and M33, and roll in the second
        // column. Atan2 keeps pitch precise near ±90°, where asin loses most of its digits.
        var cosPitch = MathF.Sqrt(local.M31 * local.M31 + local.M33 * local.M33);
        var pitch = MathF.Atan2(-local.M32, cosPitch);
        float yaw, roll;
        if (cosPitch > 1e-5f)
        {
            yaw = MathF.Atan2(local.M31, local.M33);
            roll = MathF.Atan2(local.M12, local.M22);
        }
        else
        {
            yaw = MathF.Atan2(-local.M13, local.M11);
            roll = 0f;
        }

        var offset = local.Translation;
        return new OverlayPlacement(
            offset.X,
            offset.Y,
            offset.Z,
            RadiansToDegrees(pitch),
            RadiansToDegrees(yaw),
            RadiansToDegrees(roll));
    }

    /// <summary>
    /// Where a grabbed panel sits on the controller wearing it: it keeps the pose it had in the
    /// grabbing hand, but stays within <paramref name="maxOffset"/> metres of the wearing
    /// controller on each axis, the furthest the settings can store.
    /// </summary>
    /// <param name="panelInHand">The panel's pose relative to the grabbing hand when it was grabbed.</param>
    public static Matrix4x4 FollowHand(in Matrix4x4 panelInHand, in Matrix4x4 hand, in Matrix4x4 worldToWrist, float maxOffset)
    {
        var local = panelInHand * hand * worldToWrist;
        local.Translation = Vector3.Clamp(local.Translation, new Vector3(-maxOffset), new Vector3(maxOffset));
        return local;
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
    /// Intersects a ray with a flat overlay of the given size.
    /// </summary>
    /// <param name="overlayToWorld">The overlay's pose; its face lies in the local XY plane.</param>
    /// <param name="uv">Hit position with (0, 0) at the texture's top-left and (1, 1) at its bottom-right.</param>
    /// <param name="distance">Distance along the ray to the hit, in metres.</param>
    public static bool TryIntersect(
        in Matrix4x4 overlayToWorld,
        float widthMeters,
        float heightMeters,
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
        var v = 0.5f - hit.Y / heightMeters;
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

    private static float RadiansToDegrees(float radians) => radians * (180f / MathF.PI);
}

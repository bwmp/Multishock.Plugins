using System.Numerics;
using SteamVROverlay.Overlay;
using Valve.VR;

namespace SteamVROverlay.Tests;

public sealed class OverlayMathTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void TranslationLandsInOpenVrsFourthColumn()
    {
        var transform = OverlayMath.BuildLocalTransform(0.1f, 0.2f, 0.3f, 0f, 0f, 0f);

        Assert.Equal(0.1f, transform.m3, Tolerance);
        Assert.Equal(0.2f, transform.m7, Tolerance);
        Assert.Equal(0.3f, transform.m11, Tolerance);
    }

    [Fact]
    public void AnUnrotatedTransformIsIdentityPlusTranslation()
    {
        var transform = OverlayMath.BuildLocalTransform(0f, 0f, 0f, 0f, 0f, 0f);

        Assert.Equal(1f, transform.m0, Tolerance);
        Assert.Equal(1f, transform.m5, Tolerance);
        Assert.Equal(1f, transform.m10, Tolerance);
        Assert.Equal(0f, transform.m1, Tolerance);
        Assert.Equal(0f, transform.m4, Tolerance);
    }

    [Fact]
    public void MatrixConversionRoundTrips()
    {
        var original = OverlayMath.BuildLocalTransform(0.05f, -0.02f, 0.09f, -30f, 15f, 5f);

        var roundTripped = OverlayMath.ToHmdMatrix(OverlayMath.ToMatrix4x4(original));

        Assert.Equal(original.m0, roundTripped.m0, Tolerance);
        Assert.Equal(original.m5, roundTripped.m5, Tolerance);
        Assert.Equal(original.m6, roundTripped.m6, Tolerance);
        Assert.Equal(original.m9, roundTripped.m9, Tolerance);
        Assert.Equal(original.m3, roundTripped.m3, Tolerance);
        Assert.Equal(original.m11, roundTripped.m11, Tolerance);
    }

    [Fact]
    public void AnUnrotatedOverlayFacesPositiveZ()
    {
        var matrix = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(0f, 0f, 0f, 0f, 0f, 0f));

        var forward = OverlayMath.ForwardOf(matrix);

        Assert.Equal(0f, forward.X, Tolerance);
        Assert.Equal(0f, forward.Y, Tolerance);
        Assert.Equal(1f, forward.Z, Tolerance);
    }

    [Fact]
    public void YawingHalfATurnFlipsTheFacingDirection()
    {
        var matrix = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(0f, 0f, 0f, 0f, 180f, 0f));

        Assert.Equal(-1f, OverlayMath.ForwardOf(matrix).Z, 1e-4f);
    }

    [Fact]
    public void PitchingDownTiltsTheFacingDirectionUpward()
    {
        // A negative pitch tilts the panel's face toward the wearer's eyes.
        var matrix = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(0f, 0f, 0f, -45f, 0f, 0f));

        var forward = OverlayMath.ForwardOf(matrix);

        Assert.True(forward.Y > 0f, $"expected an upward tilt but Y was {forward.Y}");
        Assert.Equal(MathF.Sqrt(0.5f), forward.Z, 1e-4f);
    }

    [Fact]
    public void PositionOfReadsTheTranslationBackFromAPose()
    {
        var pose = OverlayMath.BuildLocalTransform(1.5f, -0.25f, 2f, 20f, 40f, 60f);

        var position = OverlayMath.PositionOf(OverlayMath.ToMatrix4x4(pose));

        Assert.Equal(new Vector3(1.5f, -0.25f, 2f), position);
    }

    [Fact]
    public void ComposingWithADevicePoseMovesTheOverlayIntoWorldSpace()
    {
        // A controller a metre up and rotated a half turn about Y.
        var devicePose = OverlayMath.BuildLocalTransform(0f, 1f, 0f, 0f, 180f, 0f);
        var local = OverlayMath.BuildLocalTransform(0f, 0f, 0.1f, 0f, 0f, 0f);

        var world = OverlayMath.ToMatrix4x4(local) * OverlayMath.ToMatrix4x4(devicePose);
        var position = OverlayMath.PositionOf(world);

        // The 10 cm local +Z offset points along world -Z once the controller is turned around.
        Assert.Equal(0f, position.X, 1e-4f);
        Assert.Equal(1f, position.Y, 1e-4f);
        Assert.Equal(-0.1f, position.Z, 1e-4f);
    }

    [Fact]
    public void ARayAtTheCentreHitsTheMiddleOfThePanel()
    {
        var overlay = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(0f, 1f, -0.5f, 0f, 0f, 0f));

        var hit = OverlayMath.TryIntersect(overlay, 0.2f, new Vector3(0f, 1f, 0f), -Vector3.UnitZ, 2f, out var uv, out var distance);

        Assert.True(hit);
        Assert.Equal(0.5f, uv.X, 1e-4f);
        Assert.Equal(0.5f, uv.Y, 1e-4f);
        Assert.Equal(0.5f, distance, 1e-4f);
    }

    [Fact]
    public void UvOriginIsTheTopLeftOfTheTexture()
    {
        var overlay = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(0f, 0f, -0.5f, 0f, 0f, 0f));

        // Aim 5 cm left and 5 cm up on a 20 cm panel: a quarter in from the top-left corner.
        var hit = OverlayMath.TryIntersect(overlay, 0.2f, new Vector3(-0.05f, 0.05f, 0f), -Vector3.UnitZ, 2f, out var uv, out _);

        Assert.True(hit);
        Assert.Equal(0.25f, uv.X, 1e-4f);
        Assert.Equal(0.25f, uv.Y, 1e-4f);
    }

    [Fact]
    public void RaysOutsideThePanelMiss()
    {
        var overlay = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(0f, 0f, -0.5f, 0f, 0f, 0f));

        Assert.False(OverlayMath.TryIntersect(overlay, 0.2f, new Vector3(0.2f, 0f, 0f), -Vector3.UnitZ, 2f, out _, out _));
    }

    [Fact]
    public void RaysPointingAwayOrTooFarMiss()
    {
        var overlay = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(0f, 0f, -0.5f, 0f, 0f, 0f));

        Assert.False(OverlayMath.TryIntersect(overlay, 0.2f, Vector3.Zero, Vector3.UnitZ, 2f, out _, out _));
        Assert.False(OverlayMath.TryIntersect(overlay, 0.2f, Vector3.Zero, -Vector3.UnitZ, 0.3f, out _, out _));
    }

    [Fact]
    public void IntersectionFollowsARotatedPanel()
    {
        // Panel 50 cm to the left, turned to face +X (back toward the origin).
        var overlay = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(-0.5f, 0f, 0f, 0f, 90f, 0f));

        var hit = OverlayMath.TryIntersect(overlay, 0.2f, Vector3.Zero, -Vector3.UnitX, 2f, out var uv, out var distance);

        Assert.True(hit);
        Assert.Equal(0.5f, uv.X, 1e-4f);
        Assert.Equal(0.5f, uv.Y, 1e-4f);
        Assert.Equal(0.5f, distance, 1e-4f);
    }

    [Fact]
    public void AHmdMatrixIsInterpretedWithBasisVectorsInColumns()
    {
        // Rotation of +90 degrees about Y in OpenVR's column-major-basis layout.
        var matrix = new HmdMatrix34_t
        {
            m0 = 0f, m1 = 0f, m2 = 1f, m3 = 0f,
            m4 = 0f, m5 = 1f, m6 = 0f, m7 = 0f,
            m8 = -1f, m9 = 0f, m10 = 0f, m11 = 0f,
        };

        var forward = OverlayMath.ForwardOf(OverlayMath.ToMatrix4x4(matrix));

        // The third column (m2, m6, m10) is the Z basis vector.
        Assert.Equal(1f, forward.X, Tolerance);
        Assert.Equal(0f, forward.Y, Tolerance);
        Assert.Equal(0f, forward.Z, Tolerance);
    }
}

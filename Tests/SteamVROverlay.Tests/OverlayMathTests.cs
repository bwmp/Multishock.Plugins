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

        var hit = OverlayMath.TryIntersect(overlay, 0.2f, 0.2f, new Vector3(0f, 1f, 0f), -Vector3.UnitZ, 2f, out var uv, out var distance);

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
        var hit = OverlayMath.TryIntersect(overlay, 0.2f, 0.2f, new Vector3(-0.05f, 0.05f, 0f), -Vector3.UnitZ, 2f, out var uv, out _);

        Assert.True(hit);
        Assert.Equal(0.25f, uv.X, 1e-4f);
        Assert.Equal(0.25f, uv.Y, 1e-4f);
    }

    [Fact]
    public void ATallPanelMapsItsHeightOntoTheTexture()
    {
        var overlay = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(0f, 0f, -0.5f, 0f, 0f, 0f));

        // 15 cm up is off a 20 cm square but inside a 20 x 40 cm panel, 1/8 of the way down.
        var hit = OverlayMath.TryIntersect(overlay, 0.2f, 0.4f, new Vector3(0f, 0.15f, 0f), -Vector3.UnitZ, 2f, out var uv, out _);

        Assert.True(hit);
        Assert.Equal(0.5f, uv.X, 1e-4f);
        Assert.Equal(0.125f, uv.Y, 1e-4f);
    }

    [Fact]
    public void RaysOutsideThePanelMiss()
    {
        var overlay = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(0f, 0f, -0.5f, 0f, 0f, 0f));

        Assert.False(OverlayMath.TryIntersect(overlay, 0.2f, 0.2f, new Vector3(0.2f, 0f, 0f), -Vector3.UnitZ, 2f, out _, out _));
    }

    [Fact]
    public void RaysPointingAwayOrTooFarMiss()
    {
        var overlay = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(0f, 0f, -0.5f, 0f, 0f, 0f));

        Assert.False(OverlayMath.TryIntersect(overlay, 0.2f, 0.2f, Vector3.Zero, Vector3.UnitZ, 2f, out _, out _));
        Assert.False(OverlayMath.TryIntersect(overlay, 0.2f, 0.2f, Vector3.Zero, -Vector3.UnitZ, 0.3f, out _, out _));
    }

    [Fact]
    public void IntersectionFollowsARotatedPanel()
    {
        // Panel 50 cm to the left, turned to face +X (back toward the origin).
        var overlay = OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(-0.5f, 0f, 0f, 0f, 90f, 0f));

        var hit = OverlayMath.TryIntersect(overlay, 0.2f, 0.2f, Vector3.Zero, -Vector3.UnitX, 2f, out var uv, out var distance);

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

    private static Matrix4x4 Local(OverlayPlacement p) =>
        OverlayMath.ToMatrix4x4(OverlayMath.BuildLocalTransform(p.OffsetX, p.OffsetY, p.OffsetZ, p.PitchDegrees, p.YawDegrees, p.RollDegrees));

    private static void AssertSameTransform(Matrix4x4 expected, Matrix4x4 actual)
    {
        float[] e = [expected.M11, expected.M12, expected.M13, expected.M21, expected.M22, expected.M23, expected.M31, expected.M32, expected.M33, expected.M41, expected.M42, expected.M43];
        float[] a = [actual.M11, actual.M12, actual.M13, actual.M21, actual.M22, actual.M23, actual.M31, actual.M32, actual.M33, actual.M41, actual.M42, actual.M43];
        for (var i = 0; i < e.Length; i++)
        {
            Assert.Equal(e[i], a[i], 1e-4f);
        }
    }

    public static TheoryData<float, float, float> Orientations => new()
    {
        { 0f, 0f, 0f },
        { -30f, 15f, 5f },
        { -5f, -90f, 180f },
        { 60f, 170f, -120f },
        { -89f, 45f, 30f },
        { 90f, 30f, 0f },
        { -90f, -60f, 20f },
    };

    [Theory]
    [MemberData(nameof(Orientations))]
    public void DecomposingALocalTransformRebuildsTheSameTransform(float pitch, float yaw, float roll)
    {
        var original = Local(new OverlayPlacement(-0.09f, 0.02f, 0.12f, pitch, yaw, roll));

        var placement = OverlayMath.DecomposeLocalTransform(original);

        AssertSameTransform(original, Local(placement));
        Assert.InRange(placement.PitchDegrees, -90f, 90f);
        Assert.InRange(placement.YawDegrees, -180f, 180f);
        Assert.InRange(placement.RollDegrees, -180f, 180f);
    }

    [Fact]
    public void OrdinaryAnglesDecomposeToTheSameNumbers()
    {
        var placement = OverlayMath.DecomposeLocalTransform(Local(new OverlayPlacement(0.05f, -0.02f, 0.09f, -30f, 15f, 5f)));

        Assert.Equal(new OverlayPlacement(0.05f, -0.02f, 0.09f, -30f, 15f, 5f), placement with
        {
            PitchDegrees = MathF.Round(placement.PitchDegrees, 3),
            YawDegrees = MathF.Round(placement.YawDegrees, 3),
            RollDegrees = MathF.Round(placement.RollDegrees, 3),
        });
    }

    [Fact]
    public void ARightHandPlacementMirrorsBackToTheSavedLeftHandValues()
    {
        // Settings are in left-hand terms; the right hand builds its transform with X, yaw and
        // roll negated, so saving a moved right-hand panel negates them again.
        var saved = new OverlayPlacement(-0.06f, 0.01f, 0.12f, -10f, -90f, 25f);
        var rightHandLocal = Local(saved with { OffsetX = -saved.OffsetX, YawDegrees = -saved.YawDegrees, RollDegrees = -saved.RollDegrees });

        var decomposed = OverlayMath.DecomposeLocalTransform(rightHandLocal);
        var mirroredBack = decomposed with { OffsetX = -decomposed.OffsetX, YawDegrees = -decomposed.YawDegrees, RollDegrees = -decomposed.RollDegrees };

        AssertSameTransform(Local(saved), Local(mirroredBack));
    }

    [Fact]
    public void AGrabbedPanelStaysPutUntilTheHandMoves()
    {
        var wrist = Local(new OverlayPlacement(0.2f, 1.1f, -0.3f, 10f, 30f, 0f));
        var local = Local(new OverlayPlacement(-0.06f, 0f, 0.12f, 0f, -90f, 0f));
        var hand = Local(new OverlayPlacement(0.4f, 1.2f, -0.2f, -20f, 0f, 45f));
        Matrix4x4.Invert(hand, out var worldToHand);
        Matrix4x4.Invert(wrist, out var worldToWrist);
        var panelInHand = local * wrist * worldToHand;

        var followed = OverlayMath.FollowHand(panelInHand, hand, worldToWrist, 0.5f);

        AssertSameTransform(local, followed);
    }

    [Fact]
    public void AGrabbedPanelMovesWithTheHand()
    {
        var wrist = Local(new OverlayPlacement(0f, 1f, 0f, 0f, 0f, 0f));
        var local = Local(new OverlayPlacement(0f, 0f, 0.1f, 0f, 0f, 0f));
        var hand = Local(new OverlayPlacement(0.3f, 1f, 0f, 0f, 0f, 0f));
        Matrix4x4.Invert(hand, out var worldToHand);
        Matrix4x4.Invert(wrist, out var worldToWrist);
        var panelInHand = local * wrist * worldToHand;

        var movedHand = hand * Matrix4x4.CreateTranslation(0f, 0.05f, -0.02f);
        var followed = OverlayMath.FollowHand(panelInHand, movedHand, worldToWrist, 0.5f);

        Assert.Equal(0f, followed.Translation.X, 1e-4f);
        Assert.Equal(0.05f, followed.Translation.Y, 1e-4f);
        Assert.Equal(0.08f, followed.Translation.Z, 1e-4f);
    }

    [Fact]
    public void AGrabbedPanelCannotBeDraggedFurtherThanTheSettingsAllow()
    {
        var wrist = Matrix4x4.Identity;
        var panelInHand = Matrix4x4.Identity;
        var hand = Matrix4x4.CreateTranslation(0.9f, -0.2f, -1.4f);

        var followed = OverlayMath.FollowHand(panelInHand, hand, wrist, 0.5f);

        Assert.Equal(new Vector3(0.5f, -0.2f, -0.5f), followed.Translation);
    }
}

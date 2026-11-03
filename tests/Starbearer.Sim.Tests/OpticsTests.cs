using Starbearer.Sim;
using Xunit;

/// <summary>The view through the bubble and past a star: aberration, Doppler, the dark cone, lensing.</summary>
public partial class SimTests
{
    [Fact]
    public void TheBubbleCrowdsTheStarsAheadBlueAndLeavesTheRearRed()
    {
        var along = new Vec3d(0, 0, 1);
        const double b = 0.6;
        // Straight ahead stays ahead, blueshifted by √((1+β)/(1−β)); straight behind, redshifted by its inverse.
        var (ahead, dAhead) = Optics.Aberrate(along, along, b);
        Assert.Equal(1, ahead.Dot(along), 9);
        Assert.Equal(Math.Sqrt((1 + b) / (1 - b)), dAhead, 9);
        Assert.Equal(Math.Sqrt((1 - b) / (1 + b)), Optics.Aberrate(-along, along, b).Doppler, 9);
        // A star abeam is seen forward of abeam, at cos θ' = β, and keeps its side.
        var (abeam, _) = Optics.Aberrate(new Vec3d(1, 0, 0), along, b);
        Assert.Equal(b, abeam.Dot(along), 9);
        Assert.True(abeam.X > 0 && Math.Abs(abeam.Length - 1) < 1e-12);
        // At rest, nothing moves.
        Assert.Equal((new Vec3d(1, 0, 0), 1.0), Optics.Aberrate(new Vec3d(1, 0, 0), along, 0));
        // The faster, the stronger, but never all the way to light speed.
        Assert.True(Optics.Beta(Tuning.StallSpeed) is > 0 and < 0.3);
        Assert.True(Optics.Beta(0.6) < Optics.Beta(1.5) && Optics.Beta(100) < 1);
        // Redshifted far enough, a star goes out of sight; Doppler brightening saturates.
        Assert.Equal(0, Optics.Visible(5000 * 0.25));
        Assert.Equal(1, Optics.Visible(5000));
        Assert.Equal(Optics.MaxGain, Optics.DopplerGain(3));
    }

    [Fact]
    public void BehindAShipFasterThanLightIsDark()
    {
        var along = new Vec3d(0, 0, 1);
        Assert.Equal(0, Optics.Horizon(-along, along, 1));
        Assert.Equal(1, Optics.Horizon(along, along, 1));
        Assert.Equal(1, Optics.Horizon(-along, along, 0));   // out of FTL, nothing is hidden
        // The cone widens with speed.
        var off = Optics.AtAngle(-along, new Vec3d(1, 0, 0), 30 * Math.PI / 180);
        Assert.True(Optics.Horizon(off, along, Tuning.StallSpeed) == 1 && Optics.Horizon(off, along, 1.5) == 0);
    }

    [Fact]
    public void AStarBendsTheLightBehindItIntoTwoImages()
    {
        const double e = 0.02;
        foreach (var source in new[] { 0.001, 0.01, 0.05, 0.2 })
        {
            var (outer, inner) = Optics.Lens(source, e);
            // Both images satisfy the lens equation: source = image − e²/image.
            Assert.Equal(source, outer.Angle - e * e / outer.Angle, 9);
            Assert.Equal(source, inner.Angle - e * e / inner.Angle, 9);
            Assert.True(outer.Angle > source && inner.Angle < 0, "pushed out, and the second image across the lens");
            Assert.True(outer.Gain >= 1 && inner.Gain <= outer.Gain);   // equal only where both saturate
        }
        // Right behind, the images close into the Einstein ring; far off, the lens hardly matters.
        Assert.Equal(e, Optics.Lens(1e-9, e).Outer.Angle, 6);
        var far = Optics.Lens(1, e);
        Assert.Equal(1, far.Outer.Gain, 3);
        Assert.Equal(0, far.Inner.Gain, 3);
        // The ring is wider for a heavier star and closer in.
        Assert.True(Optics.EinsteinAngle(0.04, 1) > Optics.EinsteinAngle(0.02, 1));
        Assert.Equal(2, Optics.EinsteinAngle(0.02, 1) / Optics.EinsteinAngle(0.02, 4), 9);
    }
}

using Starbearer.Sim;
using Xunit;

/// <summary>Readouts: clock, countdowns, compass and nearby contacts.</summary>
public partial class SimTests
{
    [Theory]
    [InlineData(1, 1, "x1")]
    [InlineData(100, 100, "x100")]
    [InlineData(21600, 0.25, "x21.6k")]
    [InlineData(1000000, 1000000 / 86400.0, "x1M")]
    public void TimeRatesReadAsMultiplesOfRealTime(double scale, double unused, string text)
    {
        _ = unused;
        Assert.Equal(text, Instruments.Rate(scale / 86400));
    }

    [Fact]
    public void CompassMapsTheWholeSphereOntoTheDisc()
    {
        var (r, u, f) = (new Vec3d(1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(0, 0, -1));
        Assert.Equal((0.0, 0.0), Instruments.CompassPoint(f, r, u, f));
        var behind = Instruments.CompassPoint(-f, r, u, f);
        Assert.Equal(1, Math.Sqrt(behind.X * behind.X + behind.Y * behind.Y), 9);
        var right = Instruments.CompassPoint(r, r, u, f);
        Assert.Equal(0.5, right.X, 9);
        Assert.Equal(0, right.Y, 9);
        var above = Instruments.CompassPoint(u, r, u, f);
        Assert.Equal(0.5, above.Y, 9);
        var rearUp = Instruments.CompassPoint(u - f, r, u, f);
        Assert.Equal(0.75, rearUp.Y, 9);
    }

    [Fact]
    public void NearbyContactsSortByDistanceAndReadTheApproach()
    {
        var g = WithPorts();
        var chart = g.Stars.Select(s => s.Position).ToArray();
        var c = Instruments.Nearby(g, chart, new Vec3d(6, 0, 0), new Vec3d(0.5, 0, 0), 2);
        Assert.Equal("to", c[0].Star.Name);
        Assert.Equal(2, c[0].Distance, 9);
        Assert.Equal(0, c[0].OffPrograde, 6);
        Assert.Equal(4, c[0].Eta!.Value, 9);
        Assert.Equal(180, c[1].OffPrograde, 6);
        Assert.Null(c[1].Eta);
    }

    [Theory]
    [InlineData(0, "D000 00:00:00")]
    [InlineData(1.5, "D001 12:00:00")]
    [InlineData(12.142, "D012 03:24:28")]
    public void ClockReadsDaysAndTime(double days, string expected) =>
        Assert.Equal(expected, Instruments.Clock(days));

    [Theory]
    [InlineData(0.25, "T-06:00:00")]
    [InlineData(2.5, "T-2d 12:00:00")]
    [InlineData(-0.01, "T+00:14:24")]
    public void CountdownShowsTimeToTheEvent(double days, string expected) =>
        Assert.Equal(expected, Instruments.Countdown(days));
}

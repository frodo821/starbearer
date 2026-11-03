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
    [InlineData(0, "00:00:00.00")]
    [InlineData(1.5, "1d 12:00:00.00")]
    [InlineData(12.142, "12d 03:24:28.80")]
    [InlineData(367.25 + 3 / 86400.0 + 0.67 / 86400, "1y 2d 06:00:03.67")]
    [InlineData(-0.25, "-06:00:00.00")]
    public void ASpanReadsYearsDaysAndTime(double days, string expected) =>
        Assert.Equal(expected, Instruments.Span(days));

    [Theory]
    [InlineData(0.25, "T-06:00:00.00")]
    [InlineData(2.5, "T-2d 12:00:00.00")]
    [InlineData(-0.01, "T+00:14:24.00")]
    public void CountdownShowsTimeToTheEvent(double days, string expected) =>
        Assert.Equal(expected, Instruments.Countdown(days));

    [Theory]
    [InlineData(0, "Jan, 1st 3456, 00:00:00.00")]
    [InlineData(31 + 1.5 + (8 * 3600 + 9 * 60 + 9.12) / 86400.0 - 0.5, "Feb, 2nd 3456, 08:09:09.12")]
    [InlineData(366 + 22, "Jan, 23rd 3457, 00:00:00.00")]   // 3456 is a leap year
    public void AMomentReadsOnTheCalendar(double day, string expected) =>
        Assert.Equal(expected, Instruments.Date(day));

    [Fact]
    public void ADayReadsWithoutTheTime() => Assert.Equal("Jan, 11th 3456", Instruments.Day(10.9));
}

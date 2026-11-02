using Starbearer.Sim;
using Xunit;

/// <summary>Faster-than-light flight: drag, coasting, flybys, burns, the chart and forecasts across stars.</summary>
public partial class SimTests
{
    [Fact]
    public void SpeedDecaysExponentiallyWithDistance()
    {
        var g = Empty(new Star(0, "far", new Vec3d(1000, 1000, 0), 1, 5000, Vec3d.Zero));
        var pos = Vec3d.Zero; var vel = new Vec3d(1, 0, 0);
        var positions = new[] { g.Stars[0].Position };
        for (int i = 0; i < 4000; i++) Physics.Rk4(g, positions, ref pos, ref vel, Vec3d.Zero, 0.01);
        Assert.Equal(Math.Exp(-Tuning.BaseDrag * pos.X), vel.X, 4);
    }

    [Fact]
    public void ACoastInOpenSpaceIsExact()
    {
        var g = Empty(new Star(0, "far", new Vec3d(1000, 1000, 0), 1, 5000, Vec3d.Zero));
        var positions = new[] { g.Stars[0].Position };
        var (pos, vel, well, t) = (Vec3d.Zero, new Vec3d(1, 0, 0), -1, 0.0);
        while (t < 5) t += Physics.Coast(g, positions, ref pos, ref vel, 5 - t, ref well);
        var k = Tuning.BaseDrag;
        Assert.Equal(5, t, 9);
        Assert.Equal(Math.Log(1 + k * 5) / k, pos.X, 9);   // dt/ds = 1/v with v = exp(-k s)
        Assert.Equal(Math.Exp(-k * pos.X), vel.X, 9);
    }

    [Fact]
    public void CoastingMatchesFineSteps()
    {
        // In through a well and out again: the ray cast and the one-star steps against brute force.
        var star = new Star(0, "s", new Vec3d(3, 0.3, 0), 1, 5000, Vec3d.Zero);
        var other = new Star(1, "o", new Vec3d(-6, 5, 2), 1.5, 6000, Vec3d.Zero);
        var g = Empty(star, other);
        var positions = new[] { star.Position, other.Position };
        var (p1, v1, p2, v2) = (Vec3d.Zero, new Vec3d(0.6, 0, 0), Vec3d.Zero, new Vec3d(0.6, 0, 0));
        int well = -1;
        double t1 = 0;
        while (t1 < 12) t1 += Physics.Coast(g, positions, ref p1, ref v1, 12 - t1, ref well);
        for (int i = 0; i < 120000; i++) Physics.Rk4(g, positions, ref p2, ref v2, Vec3d.Zero, 1e-4);
        Assert.True((p1 - p2).Length < 2e-3, $"off by {(p1 - p2).Length}");
        Assert.True((v1 - v2).Length < 2e-3, $"speed off by {(v1 - v2).Length}");
        Assert.True(Math.Abs(v1.Y) > 0.01);   // it really passed through the well
    }

    [Fact]
    public void ACaughtForecastStopsAfterALap()
    {
        var star = new Star(0, "s", Vec3d.Zero, 1, 5000, Vec3d.Zero);
        var g = Empty(star);
        var r = star.Soi * 0.3;
        var speed = Math.Sqrt(star.Mu / r);
        var f = Predictor.Run(g, new[] { star.Position }, new Vec3d(r, 0, 0), new Vec3d(0, 0, speed));
        var lap = 2 * Math.PI * Math.Sqrt(r * r * r / star.Mu);
        Assert.Equal(PredictionEnd.Captured, f.End);
        Assert.InRange(f.Duration, lap * 0.9, lap * 2.1);
        Assert.Single(f.Encounters);
    }

    [Fact]
    public void FlybyDeflectionMatchesHyperbolaFormula()
    {
        // A normal star at game scale: the well is short enough for drag to barely matter.
        var star = new Star(0, "s", Vec3d.Zero, 1, 5000, Vec3d.Zero);
        var g = Empty(star);
        var positions = new[] { star.Position };
        double vInf = 0.6, b = 0.25;
        var pos = new Vec3d(-star.Soi * 1.01, b, 0); var vel = new Vec3d(vInf, 0, 0);
        double rp = double.MaxValue;
        for (int i = 0; i < 100000 && pos.Length < star.Soi * 1.02 + (pos.X < 0 ? 100 : 0); i++)
        {
            Physics.Rk4(g, positions, ref pos, ref vel, Vec3d.Zero, 0.001);
            rp = Math.Min(rp, pos.Length);
        }
        var measured = Math.Atan2(-vel.Y, vel.X);
        var predicted = 2 * Math.Asin(1 / (1 + rp * vInf * vInf / star.Mu));
        Assert.InRange(measured / predicted, 0.85, 1.25);
    }

    [Fact]
    public void BurnFollowsRocketEquation()
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.StarOf(g.Ports[1]), Box, Unseen(g), 1);
        var m0 = v.Mass;
        v.EnterFtl();
        v.Burn(new Vec3d(1, 0, 0), 0.4);
        Assert.Equal(m0 / Math.Exp(0.4 / Tuning.ExhaustVelocity), v.Mass, 9);
        Assert.Equal(Phase.Flight, v.Phase);
    }

    [Fact]
    public void BurnCannotExceedFuel()
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.StarOf(g.Ports[1]), Box, Unseen(g), 1);
        v.EnterFtl();
        v.Burn(new Vec3d(1, 0, 0), 100);
        Assert.Equal(0, v.Fuel, 9);
        Assert.True(v.Velocity.Length < 3);
    }

    [Fact]
    public void RunningOutOfFuelInOpenSpaceIsDeath()
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.StarOf(g.Ports[1]), Box, Unseen(g), 1);
        var away = (v.TruePosition - g.StarAt(g.Ports[1]).Position).Normalized();
        v.EnterFtl();
        v.Burn(away, 100);
        for (int i = 0; i < 4000 && v.Phase is Phase.Flight or Phase.Stalled; i++) v.Advance(0.5, 0, false);
        Assert.Equal(Phase.Dead, v.Phase);
    }

    [Fact]
    public void ChartConvergesOnTruthAsYouApproach()
    {
        Assert.Equal(1, Voyage.ChartFactor(double.MaxValue));
        Assert.True(Voyage.ChartFactor(2) < 0.1);
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.StarOf(g.Ports[1]), Box, Unseen(g), 1);
        var i = g.StarOf(g.Ports[0]);
        Assert.True((v.ChartPositions[i] - v.TruePositions[i]).Length < 0.03);
    }

    [Fact]
    public void SightResetsNavigationError()
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.StarOf(g.Ports[1]), Box, Unseen(g), 1);
        v.EnterFtl();
        v.Burn(new Vec3d(0, 1, 0), 0.5);
        for (int i = 0; i < 20; i++) v.Advance(0.5, 0, false);
        Assert.True(v.NavSigma > 0.03);
        v.TakeSight();
        Assert.Equal(Tuning.SightError, v.NavSigma);
    }

    [Fact]
    public void FtlThrustFollowsTheGivenDirection()
    {
        var g = WithPorts();
        var v = new Voyage(g, From, 1, Box, Unseen(g), 1);
        v.EnterFtl();
        v.PlaceForTest(new Vec3d(4, 0, 0), new Vec3d(0.5, 0, 0));
        v.Advance(0.1, new Vec3d(0, 1, 0), false);
        Assert.InRange(v.Velocity.Y, 0.09, 0.11);
        var fuel = v.Fuel;
        v.Advance(0.1, new Vec3d(0, 0.1, 0), false);    // a tenth of the throttle
        Assert.InRange(fuel - v.Fuel, 0, 0.02);
    }

    [Fact]
    public void StallingWithTooLittleFuelToRestartIsDeath()
    {
        var g = WithPorts();
        var v = new Voyage(g, From, 1, Box, Unseen(g), 1);
        v.EnterFtl();
        v.Burn(new Vec3d(0, 1, 0), v.DeltaVRemaining - 0.02);   // tiny reserve, not enough to restart
        v.PlaceForTest(new Vec3d(4, 30, 0), new Vec3d(0, 0.05, 0));
        for (int i = 0; i < 10 && v.Phase is Phase.Flight or Phase.Stalled; i++) v.Advance(0.5, 0, false);
        Assert.Equal(Phase.Dead, v.Phase);
        Assert.Equal(DeathCause.Stranded, v.Death);
    }

    [Fact]
    public void AShipCoastingBetweenStarsChangesPrimary()
    {
        var g = Galaxy.Generate(5);
        var (from, to) = (g.StarAt(g.Ports[0]), g.StarAt(g.Ports[1]));
        var v = new Voyage(g, g.Ports[0], to.Id, Box, Unseen(g), 1);
        v.Undock();
        var way = (to.Position - from.Position).Normalized();
        var start = from.Position + way * (from.Soi * 1.01);
        v.PlaceSublightForTest((start - from.Position) / Sublight.Scale, way * 0.5);
        var soon = Sublight.NextPrimary(g, from.Id, start, way * 0.5, v.AbsoluteTime);
        Assert.NotNull(soon);
        Assert.True(v.Adrift);
        Assert.False(soon!.Value.Star == from.Id);
        v.AdvanceSublight(soon.Value.Time - v.AbsoluteTime + 0.1, Vec3d.Zero);
        Assert.Equal(soon.Value.Star, v.SystemStar);
        Assert.False(v.Adrift);
    }

    /// <summary>A ship just outside one star, coasting at a point beside the next so it swings past.</summary>
    static (Voyage V, OrbitForecast F, Star To) Flyby(double speed)
    {
        var g = Galaxy.Generate(5);
        // The nearest star, so nothing else lies on the way.
        var from = g.StarAt(g.Ports[0]);
        var to = g.Stars.Where(s => s != from).MinBy(s => (s.Position - from.Position).Length)!;
        var v = new Voyage(g, g.Ports[0], to.Id, Box, Unseen(g), 1);
        v.Undock();
        var way = (to.Position - from.Position).Normalized();
        var start = from.Position + way * (from.Soi * 1.01);
        var aim = to.Position + way.AnyPerpendicular() * (to.Soi * 0.4);
        var velocity = (aim - start).Normalized() * speed;
        v.PlaceSublightForTest((start - from.Position) / Sublight.Scale, velocity);
        var f = Sublight.Predict(from, null, v.SubPosition, velocity, v.AbsoluteTime, Array.Empty<Maneuver>(), 10);
        Sublight.FollowOn(g, from.Id, f);
        return (v, f, to);
    }

    [Fact]
    public void AForecastCarriesOnAroundTheStarItMeets()
    {
        var (v, f, to) = Flyby(0.5);
        Assert.Equal(to.Id, f.Next!.Value.Star);
        var pass = f.Beyond!;
        Assert.Equal(f.Next.Value.Time, pass.Track[0].Time, 9);
        Assert.Equal(Sublight.SystemRadius(to), pass.Track[0].Position.Length, 6);
        Assert.False(pass.Impact);
        Assert.Equal(Sublight.SystemRadius(to), pass.Exit!.Value.Position.Length, 6);
        Assert.True(pass.DrawnCount > 10);
        var bend = f.Exit!.Value.Velocity.Normalized().Dot(pass.Exit.Value.Velocity.Normalized());
        Assert.True(bend < 0.9999, "the pass should bend the path");

        var mid = pass.Track[pass.DrawnCount / 2];
        v.AdvanceSublight(mid.Time - v.AbsoluteTime, Vec3d.Zero);
        Assert.Equal(to.Id, v.SystemStar);
        Assert.True((v.SubPosition - mid.Position).Length < 0.02, $"flown {v.SubPosition} predicted {mid.Position}");
    }

    [Fact]
    public void AForecastStopsFollowingAfterTheLastLeg()
    {
        var g = Galaxy.Generate(5);
        var (_, f, _) = Flyby(0.5);
        Assert.NotNull(f.Beyond);
        var once = new OrbitForecast { Exit = f.Exit };
        Sublight.FollowOn(g, g.StarOf(g.Ports[0]), once, legs: 1);
        Assert.Null(once.Beyond?.Beyond);
        var none = new OrbitForecast { Exit = f.Exit };
        Sublight.FollowOn(g, g.StarOf(g.Ports[0]), none, legs: 0);
        Assert.NotNull(none.Next);
        Assert.Null(none.Beyond);
    }

    [Fact]
    public void ABurnPlannedOnTheStarAheadIsFlownThere()
    {
        var (v, plain, to) = Flyby(0.5);
        var pass = plain.Beyond!;
        var node = new Maneuver { Time = pass.Track[pass.DrawnCount / 2].Time, Prograde = -0.1 };
        v.Nodes.Add(node);
        var f = Sublight.Predict(v.Primary, null, v.SubPosition, v.SubVelocity, v.AbsoluteTime, v.Nodes, 10);
        Sublight.FollowOn(v.G, v.SystemStar, f, v.Nodes);
        Assert.Empty(f.Stages);
        Assert.Equal(to.Id, f.LegOf(node)!.Star);
        Assert.Same(f.Beyond, f.LegOf(node));
        Assert.True(f.Beyond!.Exit!.Value.Time > pass.Exit!.Value.Time, "slowing down should stay longer");

        var check = f.Beyond.Track[^3];
        v.IdealNodesForTest = true;
        v.AdvanceSublight(check.Time - v.AbsoluteTime, Vec3d.Zero);
        Assert.Empty(v.Nodes);
        Assert.Equal(to.Id, v.SystemStar);
        Assert.True((v.SubPosition - check.Position).Length < 0.02, $"flown {v.SubPosition} predicted {check.Position}");
    }

    [Fact]
    public void ABurnInOpenSpaceChangesWhichStarIsMet()
    {
        var (v, plain, to) = Flyby(0.5);
        var when = (plain.Exit!.Value.Time + plain.Next!.Value.Time) / 2;
        // Straight back along the way it came: nothing ahead is the same star any more.
        var node = new Maneuver { Time = when, Prograde = -1.0 };
        v.Nodes.Add(node);
        var f = Sublight.Predict(v.Primary, null, v.SubPosition, v.SubVelocity, v.AbsoluteTime, v.Nodes, 10);
        Sublight.FollowOn(v.G, v.SystemStar, f, v.Nodes);
        Assert.Same(f.Drift, f.LegOf(node));
        Assert.Equal(when, f.Drift!.Stages[0].Node.Time, 9);
        Assert.NotEqual(to.Id, f.Next?.Star);
    }

    [Fact]
    public void LeavingIntoAnOverlappingSphereHandsOverAtOnce()
    {
        var g = Galaxy.Generate(5);
        var inside = g.StarAt(g.Ports[1]);
        var at = inside.Position + new Vec3d(inside.Soi * 0.5, 0, 0);
        var n = Sublight.NextPrimary(g, g.StarOf(g.Ports[0]), at, new Vec3d(1, 0, 0), 7);
        Assert.Equal(inside.Id, n!.Value.Star);
        Assert.Equal(7, n.Value.Time, 9);
    }
}

using Starbearer.Sim;
using Xunit;

public class SimTests
{
    static readonly Cargo Box = new("box", 0.5, 0.9, 100);
    static double[] Unseen(Galaxy g) => Enumerable.Repeat(double.MaxValue, g.Stars.Length).ToArray();

    static Galaxy Empty(params Star[] stars) => new()
    {
        Seed = 1, Stars = stars, Nebulae = Array.Empty<Nebula>(), Ports = Array.Empty<int>(),
    };

    [Fact]
    public void SameSeedGivesSameGalaxy()
    {
        var a = Galaxy.Generate(42); var b = Galaxy.Generate(42);
        Assert.Equal(a.Stars.Select(s => s.Position), b.Stars.Select(s => s.Position));
        Assert.Equal(a.Stars.Select(s => s.Name), b.Stars.Select(s => s.Name));
        Assert.NotEqual(a.Stars[0].Position, Galaxy.Generate(43).Stars[0].Position);
    }

    [Fact]
    public void GenerationRespectsSeparationAndCount()
    {
        var g = Galaxy.Generate(7);
        Assert.Equal(Tuning.StarCount, g.Stars.Length);
        foreach (var a in g.Stars) foreach (var b in g.Stars)
            if (a.Id != b.Id) Assert.True((a.Position - b.Position).Length >= Tuning.MinStarSeparation);
        Assert.Equal(g.Ports.Length, g.Ports.Distinct().Count());
    }

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
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
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
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.EnterFtl();
        v.Burn(new Vec3d(1, 0, 0), 100);
        Assert.Equal(0, v.Fuel, 9);
        Assert.True(v.Velocity.Length < 3);
    }

    [Fact]
    public void RunningOutOfFuelInOpenSpaceIsDeath()
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        var away = (v.TruePosition - g.Stars[g.Ports[1]].Position).Normalized();
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
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        var i = g.Ports[0];
        Assert.True((v.ChartPositions[i] - v.TruePositions[i]).Length < 0.03);
    }

    [Fact]
    public void SightResetsNavigationError()
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.EnterFtl();
        v.Burn(new Vec3d(0, 1, 0), 0.5);
        for (int i = 0; i < 20; i++) v.Advance(0.5, 0, false);
        Assert.True(v.NavSigma > 0.03);
        v.TakeSight();
        Assert.Equal(Tuning.SightError, v.NavSigma);
    }

    [Fact]
    public void ABoardOffersWhatItsStationDeals()
    {
        var g = Galaxy.Generate(5);
        var goods = g.Ports.Concat(g.Depots).GroupBy(p => Missions.KindOf(g, p))
            .ToDictionary(k => k.Key, k => k.SelectMany(p => Missions.Board(g, p, 0, Array.Empty<Consignment>())).Select(m => m.Cargo.Name).ToHashSet());
        Assert.True(goods.Count >= 3);
        foreach (var (kind, names) in goods)
            foreach (var (other, others) in goods)
                if (kind != other) Assert.Empty(names.Intersect(others));
        var board = Missions.Board(g, g.Ports[0], 0, Array.Empty<Consignment>());
        Assert.All(board, m => { Assert.Equal(g.Ports[0], m.Origin); Assert.NotEqual(m.Origin, m.Target); Assert.True(g.HasStation(m.Target)); Assert.True(m.Pay > 0); });
        Assert.NotEqual(board.Select(m => m.Id), Missions.Board(g, g.Ports[0], 1, Array.Empty<Consignment>()).Select(m => m.Id));
    }

    [Fact]
    public void AHoldTakesJobsUntilItsVolumeRunsOut()
    {
        var cargo = new Cargo("crate", 0.5, 0.9, 100, 3);
        Mission Job(int i) => new($"{i}", 0, 1, cargo, 100);
        var s = new SaveData();
        Assert.True(Missions.Take(s, Job(0)));
        Assert.True(Missions.Take(s, Job(1)));
        Assert.False(Missions.Take(s, Job(2)));
        Assert.Equal(6, Missions.VolumeUsed(s.Hold));
        var g = Galaxy.Generate(5);
        var board = Missions.Board(g, g.Ports[0], 0, Array.Empty<Consignment>());
        var taken = new[] { new Consignment { Mission = board[0] } };
        Assert.DoesNotContain(Missions.Board(g, g.Ports[0], 0, taken), m => m.Id == board[0].Id);
    }

    [Fact]
    public void EachLoadWeighsOnTheShipAndTakesItsOwnHeat()
    {
        var g = Galaxy.Generate(5);
        Consignment Load(string id, double heat) => new() { Mission = new Mission(id, g.Ports[0], g.Ports[1], new Cargo(id, 0.5, heat, 100), 100) };
        var one = new Voyage(g, g.Ports[0], g.Ports[1], new[] { Load("a", 0.4) }, Unseen(g), 1);
        var two = new Voyage(g, g.Ports[0], g.Ports[1], new[] { Load("a", 0.4), Load("b", 0.9) }, Unseen(g), 1);
        Assert.Equal(one.Mass + 0.5, two.Mass, 9);
        Assert.True(two.DeltaVRemaining < one.DeltaVRemaining);
        Assert.Equal(0.4, two.HeatTolerance);
    }

    [Fact]
    public void DroppingAJobCostsAPartOfItsPay()
    {
        var job = new Consignment { Mission = new Mission("a", 0, 1, Box, 100) };
        var s = new SaveData { Credits = 40, Hold = new() { job } };
        Missions.Drop(s, job);
        Assert.Empty(s.Hold);
        Assert.Equal(40 - 100 * Tuning.DropPenalty, s.Credits, 9);
        var broke = new SaveData { Credits = 5, Hold = new() { job } };
        Missions.Drop(broke, job);
        Assert.Equal(0, broke.Credits);
    }

    [Fact]
    public void SaveRoundTripsUnvisitedStars()
    {
        var s = SaveData.NewGalaxy(11).Save;
        s.Deliveries = 2; s.Retries = 3;
        var back = SaveData.FromJson(s.ToJson())!;
        Assert.Equal(s.Seed, back.Seed);
        Assert.Equal(2, back.Deliveries);
        Assert.Equal(double.MaxValue, back.ClosestApproach[0]);
    }

    static Galaxy TwoStars() => Empty(
        new Star(0, "from", Vec3d.Zero, 1, 5000, Vec3d.Zero),
        new Star(1, "to", new Vec3d(8, 0, 0), 1, 5000, Vec3d.Zero));

    [Fact]
    public void VoyageStartsCoorbitingTheOriginStation()
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1, epoch: 17);
        var st = g.StationAt(g.Ports[0]);
        Assert.Equal(Phase.Sublight, v.Phase);
        Assert.True((st.Position(17) - v.SubPosition).Length < 1e-9);
        v.AdvanceSublight(5, Vec3d.Zero);
        Assert.True((st.Position(22) - v.SubPosition).Length < 1e-3, "ship and station share an orbit");
    }

    [Fact]
    public void OrbitElementsMatchACircularOrbit()
    {
        var g = Galaxy.Generate(3);
        var st = g.StationAt(g.Ports[0]);
        var e = Sublight.OrbitOf(st.Host!.Mu, st.Position(0), st.Velocity(0));
        Assert.True(e.Bound);
        Assert.Equal(st.Radius, e.Periapsis, 6);
        Assert.Equal(st.Radius, e.Apoapsis, 6);
        Assert.Equal(2 * Math.PI / st.Omega, e.Period, 4);
    }

    [Fact]
    public void JumpAndDescentPreserveOrbitalEnergy()
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.PlaceSublightForTest(new Vec3d(1, 0, 0), new Vec3d(0, 0, 0.2));
        var mu = v.Primary.Mu;
        var before = v.SubVelocity.LengthSq / 2 - mu / v.SubPosition.Length;
        v.EnterFtl();
        Assert.Equal(Phase.Flight, v.Phase);
        Assert.True(v.Descend());
        var after = v.SubVelocity.LengthSq / 2 - mu / v.SubPosition.Length;
        Assert.Equal(before, after, 9);
    }

    [Fact]
    public void DescentNeedsAWell()
    {
        var g = TwoStars();
        var v = new Voyage(g2(), 0, 1, Box, Unseen(g2()), 1);
        v.EnterFtl();
        v.PlaceForTest(new Vec3d(4, 0, 0), new Vec3d(0.5, 0, 0));
        Assert.False(v.Descend());
        Assert.Equal(Phase.Flight, v.Phase);
        static Galaxy g2() => WithPorts();
    }

    static Galaxy WithPorts() => new()
    {
        Seed = 1,
        Stars = new[] { new Star(0, "from", Vec3d.Zero, 1, 5000, Vec3d.Zero), new Star(1, "to", new Vec3d(8, 0, 0), 1, 5000, Vec3d.Zero) },
        Nebulae = Array.Empty<Nebula>(),
        Ports = new[] { 0, 1 },
    };

    [Fact]
    public void ManeuverPredictionMatchesActualFlight()
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        var lap = 2 * Math.PI / g.StationAt(g.Ports[0]).Omega;
        var speed = v.SubVelocity.Length;
        var node = new Maneuver { Time = lap * 0.2, Prograde = speed * 0.08, Radial = -speed * 0.03, Normal = speed * 0.05 };
        v.Nodes.Add(node);
        var f = Sublight.Predict(v.Primary, null, v.SubPosition, v.SubVelocity, 0, v.Nodes, lap * 2, 4000);
        v.IdealNodesForTest = true;
        v.AdvanceSublight(lap * 0.9, Vec3d.Zero);
        Assert.Empty(v.Nodes);
        var predicted = f.Track.OrderBy(x => Math.Abs(x.Time - lap * 0.9)).First().Position;
        Assert.True((predicted - v.SubPosition).Length < 0.01, $"off by {(predicted - v.SubPosition).Length}");
    }

    static (Voyage V, Maneuver Node, BurnTarget Target, Vec3d T, Vec3d N) NodeHere(double prograde)
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        var (t, _, n) = Sublight.Frame(v.SubPosition, v.SubVelocity);
        var node = new Maneuver { Time = v.AbsoluteTime, Prograde = prograde };
        v.Nodes.Add(node);
        return (v, node, new BurnTarget(node.Time, v.SubPosition, v.SubVelocity, v.SubVelocity + t * prograde), t, n);
    }

    [Fact]
    public void BurningAlongTheNodeSpendsIt()
    {
        var (v, node, target, along, _) = NodeHere(0.01);
        v.AdvanceSublight(0.005 / Tuning.SublightThrust, along);
        v.Remeasure(node, target, v.Primary.Mu);
        Assert.InRange(node.Prograde, 0.0045, 0.0055);
        v.AdvanceSublight(0.006 / Tuning.SublightThrust, along);
        v.Remeasure(node, target, v.Primary.Mu);
        Assert.InRange(node.Prograde, -0.0015, -0.0005);   // past the node, what is left points back
        Assert.Single(v.Nodes);                             // done only when the pilot says so
    }

    [Fact]
    public void BurningOffAxisLeavesTheNodeUnfinished()
    {
        var (v, node, target, _, sideways) = NodeHere(0.01);
        v.AdvanceSublight(0.005 / Tuning.SublightThrust, sideways);
        v.Remeasure(node, target, v.Primary.Mu);
        Assert.InRange(node.Prograde, 0.0095, 0.0105);
        Assert.InRange(node.Normal, -0.0055, -0.0045);
        Assert.True(node.DeltaV > 0.01);
    }

    [Fact]
    public void ANodeIsDoneWhenThePilotStopsCloseToIt()
    {
        var (v, node, _, along, _) = NodeHere(0.01);
        var plan = Sublight.Predict(v.Primary, null, v.SubPosition, v.SubVelocity, v.AbsoluteTime, v.Nodes, 1);
        var burn = new NodeBurn();
        void Fly(double dv)
        {
            burn.Prepare(v, node, plan);
            v.AdvanceSublight(dv / Tuning.SublightThrust, along);
            burn.Measure(v, node, along * dv);
        }
        Fly(0.004);
        Assert.Equal(NodeBurn.Ending.Short, burn.Stop(v));   // stopped short: the node stays
        Assert.Single(v.Nodes);
        Fly(0.0055);
        Assert.False(burn.Overshot);
        Fly(0.001);
        Assert.True(burn.Overshot);
        Assert.Equal(NodeBurn.Ending.Done, burn.Stop(v));
        Assert.Empty(v.Nodes);
    }

    [Fact]
    public void CoastingDoesNotChangeWhatIsLeftOfANode()
    {
        // The node sits half an orbit ahead, so its axes differ from the ship's current ones.
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        var el = Sublight.OrbitOf(v.Primary.Mu, v.SubPosition, v.SubVelocity);
        var node = new Maneuver { Time = v.AbsoluteTime + el.Period / 2, Prograde = 0.01 };
        v.Nodes.Add(node);
        var stage = Sublight.Predict(v.Primary, null, v.SubPosition, v.SubVelocity, v.AbsoluteTime, v.Nodes, 1).Stages[0];
        var target = new BurnTarget(node.Time, stage.Position, stage.Velocity, stage.Velocity + Sublight.Impulse(stage.Position, stage.Velocity, node));
        v.Remeasure(node, target, v.Primary.Mu);
        Assert.Equal(0.01, node.Prograde, 6);
        v.AdvanceSublight(el.Period / 4, Vec3d.Zero);
        v.Remeasure(node, target, v.Primary.Mu);
        Assert.Equal(0.01, node.Prograde, 6);
        Assert.Equal(0, node.Radial, 6);
        Assert.Equal(0, node.Normal, 6);
    }

    [Fact]
    public void FtlThrustFollowsTheGivenDirection()
    {
        var g = WithPorts();
        var v = new Voyage(g, 0, 1, Box, Unseen(g), 1);
        v.EnterFtl();
        v.PlaceForTest(new Vec3d(4, 0, 0), new Vec3d(0.5, 0, 0));
        v.Advance(0.1, new Vec3d(0, 1, 0), false);
        Assert.InRange(v.Velocity.Y, 0.09, 0.11);
        var fuel = v.Fuel;
        v.Advance(0.1, new Vec3d(0, 0.1, 0), false);    // a tenth of the throttle
        Assert.InRange(fuel - v.Fuel, 0, 0.02);
    }

    /// <summary>A ship dropped into the target system on an eccentric orbit, as a descent would leave it.</summary>
    static Voyage Arriving(Galaxy g)
    {
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.EnterFtl();
        var star = g.Stars[g.Ports[1]];
        var r = Sublight.SystemRadius(star) * 0.12;
        v.PlaceForTest(star.Position + new Vec3d(r * Sublight.Scale, 0, 0), new Vec3d(0, 0, Math.Sqrt(Sublight.Mu(star) / r) * 1.15));
        Assert.True(v.Descend());
        v.IdealNodesForTest = true;
        return v;
    }

    /// <summary>Flies each level of the system in turn: a plan to the next body or the station, then X.</summary>
    static void FlyDown(Voyage v, Func<Voyage, IOrbiter, List<Maneuver>?> plan)
    {
        for (int attempt = 0; attempt < 24 && v.Phase == Phase.Sublight; attempt++)
        {
            var at = v.Primary.Key;
            var way = v.Waypoint;
            if (way == null)
            {
                v.Nodes.Clear();
                v.Nodes.Add(Sublight.SuggestEscape(v.Primary, v.SubPosition, v.SubVelocity, v.AbsoluteTime));
                for (int i = 0; i < 6000 && v.Phase == Phase.Sublight && v.Primary.Key == at; i++) v.AdvanceSublight(0.05, Vec3d.Zero);
                continue;
            }
            var nodes = plan(v, way!);
            if (nodes == null)
            {
                // No window from here: a pilot would wait for one.
                v.AdvanceSublight(2 * Math.PI / way!.Omega / 7, Vec3d.Zero);
                continue;
            }
            v.Nodes.Clear(); v.Nodes.AddRange(nodes!);
            var until = nodes!.Count > 0 ? nodes.Max(n => n.Time) + 0.5 : v.AbsoluteTime;
            while (v.AbsoluteTime < until && v.Phase == Phase.Sublight && v.Primary.Key == at) v.AdvanceSublight(0.05, Vec3d.Zero);
            if (way is Center)
            {
                for (int i = 0; i < 6000 && v.Phase == Phase.Sublight && v.Primary.Key == at; i++) v.AdvanceSublight(0.05, Vec3d.Zero);
                continue;
            }
            if ((v.StationNow().Position - v.SubPosition).Length > 0.05 && attempt < 23) continue;
            for (int i = 0; i < 2000 && v.Phase == Phase.Sublight && v.Primary.Key == at; i++)
            {
                var (sp, sv) = v.StationNow();
                var toward = sp - v.SubPosition;
                var rel = sv - v.SubVelocity;
                var want = rel + toward.Normalized() * Math.Min(0.02, toward.Length);
                v.AdvanceSublight(0.02, want.Length > 0.005 ? want.Normalized() : Vec3d.Zero);
            }
        }
    }

    static readonly ulong[] Seeds = { 5, 9, 21, 33, 40, 47, 52, 60 };

    /// <summary>The tools are drafts a pilot refines, so what matters is that most systems can be reached.</summary>
    static int Docked(Func<Voyage, IOrbiter, List<Maneuver>?> plan) => Seeds.Count(seed =>
    {
        var v = Arriving(Galaxy.Generate(seed));
        FlyDown(v, plan);
        return v.Phase == Phase.Arrived;
    });

    [Fact]
    public void TheTransferComputerDocksInMostSystems()
    {
        var docked = Docked((s, way) => Sublight.PlanRendezvous(s.Primary, way, s.SubPosition, s.SubVelocity, s.AbsoluteTime));
        Assert.True(docked >= 5, $"docked {docked} of {Seeds.Length}");
    }

    [Fact]
    public void TheFreeToolsDockInMostSystems()
    {
        // H (rough transfer), M (match at closest approach), then X to close. No modules' planner.
        var docked = Docked((s, way) =>
        {
            if (Sublight.SuggestCapture(s.Primary, way, s.SubPosition, s.SubVelocity, s.AbsoluteTime) is { } capture) return capture;
            var nodes = new List<Maneuver>();
            if (Sublight.SuggestPlaneMatch(s.Primary, way, s.SubPosition, s.SubVelocity, s.AbsoluteTime) is { } plane) nodes.Add(plane);
            nodes.Add(Sublight.SuggestTransfer(s.Primary, way, s.SubPosition, s.SubVelocity, s.AbsoluteTime, nodes));
            if (way is Station && Sublight.MatchAtClosest(s.Primary, way, s.SubPosition, s.SubVelocity, s.AbsoluteTime, nodes, 40) is { } match) nodes.Add(match);
            return nodes;
        });
        Assert.True(docked >= 5, $"docked {docked} of {Seeds.Length}");
    }

    [Fact]
    public void ForecastReportsOrbitAfterTheBurnAndStationLead()
    {
        var g = Galaxy.Generate(5);
        var st = g.StationAt(g.Ports[1]);
        var star = st.Host!;
        var p = new Vec3d(st.Radius * 0.6, 0, 0);
        var v = new Vec3d(0, 0, Math.Sqrt(star.Mu / p.Length));
        var node = new Maneuver { Time = 1, Prograde = 0.08 };
        var f = Sublight.Predict(star, st, p, v, 0, new[] { node }, 40);
        Assert.True(f.After.Apoapsis > p.Length * 1.1);
        Assert.NotNull(f.Crossing);
        Assert.InRange(f.Crossing!.Value.LeadDegrees, -180, 180);
    }

    [Fact]
    public void AnyStationTakesAShipThatMatchesIt()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        var depot = g.Depots[0];
        var st = g.StationAt(depot);
        v.EnterFtl();
        v.PlaceForTest(g.Stars[depot].Position + (st.Host!.StarPosition(v.AbsoluteTime) + st.Position(v.AbsoluteTime)) * Sublight.Scale, st.Host.StarVelocity(v.AbsoluteTime) + st.Velocity(v.AbsoluteTime));
        Assert.True(v.Descend());
        v.AdvanceSublight(0.01, Vec3d.Zero);
        Assert.Equal(Phase.Arrived, v.Phase);
    }

    [Fact]
    public void LeavingAStationDoesNotDockStraightBack()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.AdvanceSublight(5, Vec3d.Zero);
        Assert.Equal(Phase.Sublight, v.Phase);
    }

    [Fact]
    public void HeldCargoSurvivesASave()
    {
        var job = new Consignment { Mission = new Mission("a", 0, 3, Box, 120), Integrity = 0.7 };
        var s = new SaveData { Hold = new() { job }, Course = 3 };
        var back = SaveData.FromJson(s.ToJson())!;
        Assert.Equal(job.Mission, back.Hold.Single().Mission);
        Assert.Equal(0.7, back.Hold.Single().Integrity);
        Assert.Equal(3, back.Course);
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], 3, back.Hold, Unseen(g), 1, ship: new ShipState(0.4, 0.5, 100));
        Assert.Equal(0.7, v.CargoIntegrity);
        Assert.Equal(0.4, v.Fuel);
        Assert.Equal(0.5, v.Hull);
        Assert.Equal(100, v.LifeRemaining);
    }

    [Fact]
    public void SuppliesCostCreditsAndStopAtWhatYouCanAfford()
    {
        var s = new SaveData { HeldFuel = 0.5, Credits = 15 };
        Supply.BuyFuel(s);
        Assert.Equal(1.0, s.HeldFuel, 9);
        Assert.Equal(0, s.Credits, 9);
        var rich = new SaveData { HeldFuel = 1.0, Credits = 1000 };
        Supply.BuyFuel(rich);
        Assert.Equal(Tuning.TankCapacity, rich.HeldFuel, 9);
        Assert.Equal(1000 - 0.8 * Tuning.FuelPrice, rich.Credits, 9);
    }

    [Fact]
    public void BankruptcyMeansTheFuelToLeaveCannotBeBought()
    {
        Assert.True(Supply.Bankrupt(new SaveData { HeldFuel = 0.1, Credits = 0 }));
        Assert.False(Supply.Bankrupt(new SaveData { HeldFuel = 0.1, Credits = 100 }));
        Assert.False(Supply.Bankrupt(new SaveData { HeldFuel = 1.0, Credits = 0 }));
    }

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
    public void DescentOrbitMatchesTheStateItComesFrom()
    {
        var star = new Star(0, "test", Vec3d.Zero, 1, 5800, Vec3d.Zero);
        var mu = Sublight.Mu(star);
        var rp = 0.5; var ra = 2.0;
        var speed = Math.Sqrt(mu * (2 / rp - 2 / (rp + ra)));
        var el = Sublight.DescentOrbit(star, rp * Sublight.Scale, speed);
        Assert.Equal(rp, el.Periapsis, 6);
        Assert.Equal(ra, el.Apoapsis, 6);
        Assert.False(Sublight.DescentOrbit(star, rp * Sublight.Scale, speed * 3).Bound);
    }

    [Fact]
    public void ForecastDoesNotShakeWhileCoasting()
    {
        var star = new Star(0, "test", Vec3d.Zero, 1, 5800, Vec3d.Zero);
        var mu = Sublight.Mu(star);
        var st = new Station(0, 1.1, 0.3, Math.Sqrt(mu / (1.1 * 1.1 * 1.1)), new Vec3d(1, 0, 0), new Vec3d(0, 0, 1));
        const double rp = 0.1, ra = 3;
        var p = new Vec3d(ra, 0, 0);
        var v = new Vec3d(0, 0, Math.Sqrt(mu * (2 / ra - 2 / (rp + ra))));
        double t = 0;
        var periapsis = new List<double>(); var closest = new List<double>(); var closeTime = new List<double>(); var lead = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            var f = Sublight.Predict(star, st, p, v, t, Array.Empty<Maneuver>(), 80);
            periapsis.Add(f.Track.Min(x => x.Position.Length));
            closest.Add(f.Closest!.Value.Distance);
            closeTime.Add(f.Closest!.Value.Time);
            lead.Add(f.Crossing!.Value.LeadDegrees);
            Sublight.Propagate(mu, ref p, ref v, 0.037);
            t += 0.037;
        }
        Assert.InRange(periapsis.Max() - periapsis.Min(), 0, 1e-4);
        Assert.InRange(closest.Max() - closest.Min(), 0, 1e-4);
        Assert.InRange(closeTime.Max() - closeTime.Min(), 0, 1e-2);
        Assert.InRange(lead.Max() - lead.Min(), 0, 0.01);
    }

    [Fact]
    public void StationsAreInclinedAndStayOnTheirCircle()
    {
        var g = Galaxy.Generate(5);
        var tilts = g.Ports.Select(p => g.StationAt(p)).ToList();
        Assert.Contains(tilts, s => Math.Abs(s.PlaneNormal.Y) < 0.998);
        foreach (var s in tilts)
        {
            Assert.Equal(s.Radius, s.Position(3.3).Length, 9);
            Assert.Equal(0, s.Position(3.3).Dot(s.Velocity(3.3)), 9);
            var (pa, va) = (s.Position(0), s.Velocity(0));
            Sublight.Propagate(s.Host!.Mu, ref pa, ref va, 4);
            Assert.True((pa - s.Position(4)).Length < 1e-3);
        }
    }

    [Fact]
    public void DescentKeepsTheFullThreeDimensionalState()
    {
        var g = Galaxy.Generate(3);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.EnterFtl();
        var star = g.Stars[g.Ports[1]];
        v.PlaceForTest(star.Position + new Vec3d(0.2, 0.3, 0), new Vec3d(0.1, -0.1, 0.3));
        Assert.True(v.Descend());
        Assert.Equal(0.3 / Sublight.Scale, v.SubPosition.Y, 9);
        Assert.Equal(-0.1, v.SubVelocity.Y, 9);
    }

    [Fact]
    public void PlaneMatchBringsTheOrbitIntoTheStationPlane()
    {
        var g = Galaxy.Generate(5);
        var st = g.StationAt(g.Ports[1]);
        var star = st.Host!;
        var p = new Vec3d(st.Radius * 0.7, 0, 0);
        var v = new Vec3d(0, 0, Math.Sqrt(star.Mu / p.Length));
        var before = Sublight.RelativeInclination(st, p, v);
        var node = Sublight.SuggestPlaneMatch(star, st, p, v, 0);
        Assert.NotNull(node);
        var f = Sublight.Predict(star, st, p, v, 0, new[] { node! }, 1);
        Assert.Equal(1, f.Stages.Count);
        var stage = f.Stages[0];
        var after = stage.Velocity + Sublight.Impulse(stage.Position, stage.Velocity, node!);
        Assert.True(before > 3);
        Assert.True(Sublight.RelativeInclination(st, stage.Position, after) < 1.5, "plane not matched");
    }

    [Fact]
    public void ForecastTracksEveryStageOfAMultiBurnPlan()
    {
        var g = Galaxy.Generate(5);
        var st = g.StationAt(g.Ports[1]);
        var star = st.Host!;
        var p = new Vec3d(st.Radius, 0, 0);
        var v = new Vec3d(0, 0, Math.Sqrt(star.Mu / p.Length));
        var lap = 2 * Math.PI / st.Omega;
        var nodes = new[]
        {
            new Maneuver { Time = lap * 0.1, Prograde = v.Length * 0.05 },
            new Maneuver { Time = lap * 0.4, Prograde = v.Length * 0.05 },
            new Maneuver { Time = lap * 0.7, Prograde = -v.Length * 0.03, Normal = v.Length * 0.02 },
        };
        var f = Sublight.Predict(star, st, p, v, 0, nodes, lap, 800);
        Assert.Equal(3, f.Stages.Count);
        Assert.True(f.Stages[1].After.Apoapsis > f.Stages[0].After.Apoapsis);
        Assert.True(f.Stages[0].TrackIndex < f.Stages[1].TrackIndex && f.Stages[1].TrackIndex < f.Stages[2].TrackIndex);
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

    [Fact]
    public void StallingWithTooLittleFuelToRestartIsDeath()
    {
        var g = WithPorts();
        var v = new Voyage(g, 0, 1, Box, Unseen(g), 1);
        v.EnterFtl();
        v.Burn(new Vec3d(0, 1, 0), v.DeltaVRemaining - 0.02);   // tiny reserve, not enough to restart
        v.PlaceForTest(new Vec3d(4, 30, 0), new Vec3d(0, 0.05, 0));
        for (int i = 0; i < 10 && v.Phase is Phase.Flight or Phase.Stalled; i++) v.Advance(0.5, 0, false);
        Assert.Equal(Phase.Dead, v.Phase);
        Assert.Equal(DeathCause.Stranded, v.Death);
    }

    [Fact]
    public void WaitingAtThePortCostsNoLifeSupport()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1, 0, new ShipState(Tuning.TankCapacity, 1, 100));
        v.AdvanceSublight(300, Vec3d.Zero);
        Assert.True(v.Docked);
        Assert.Equal(100, v.LifeRemaining, 6);
        Assert.Equal(300, v.Time, 6);
        Assert.True((v.LocalStation!.Position(v.AbsoluteTime) - v.SubPosition).Length < Tuning.DockRange);

        v.AdvanceSublight(1, new Vec3d(0, 0, 1));   // the first burn leaves the port
        Assert.False(v.Docked);
        v.AdvanceSublight(10, Vec3d.Zero);
        Assert.Equal(89, v.LifeRemaining, 6);   // 1 day of burn and 10 of coasting
    }

    [Fact]
    public void LeavingByFtlEndsTheDockedState()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.AdvanceSublight(20, Vec3d.Zero);
        v.EnterFtl();
        v.Burn(new Vec3d(0, 1, 0), 0.5);
        v.Advance(5, 0, false);
        Assert.False(v.Docked);
        Assert.Equal(Tuning.LifeSupportDays - 5, v.LifeRemaining, 6);
    }

    [Fact]
    public void UndockingStartsLifeSupportAndTheShipCanTieBackUp()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1, 0, new ShipState(Tuning.TankCapacity, 1, 100));
        Assert.False(v.RequestDock());              // already docked
        v.Undock();
        Assert.True(v.CanDock);
        v.AdvanceSublight(5, Vec3d.Zero);
        Assert.Equal(95, v.LifeRemaining, 6);
        Assert.Equal(Phase.Sublight, v.Phase);      // never left, so no automatic docking
        Assert.True(v.RequestDock());
        Assert.Equal(Phase.Arrived, v.Phase);
    }

    [Fact]
    public void RestockOnlyWorksDockedAndKeepsLifeAccounting()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1, 0, new ShipState(0.5, 0.5, 100));
        v.AdvanceSublight(20, Vec3d.Zero);
        v.Restock(new ShipState(1.5, 1, 600), v.Hold);
        Assert.Equal(1.5, v.Fuel);
        Assert.Equal(1, v.Hull);
        Assert.Equal(600, v.LifeRemaining, 6);
        v.Undock();
        v.AdvanceSublight(10, Vec3d.Zero);
        Assert.Equal(590, v.LifeRemaining, 6);
        v.Restock(new ShipState(0, 0, 0), v.Hold);
        Assert.Equal(1.5, v.Fuel);
    }

    [Theory]
    [InlineData(0.3)]   // ellipse
    [InlineData(0.9)]   // hyperbola
    public void KeplerMatchesTheNumericalCoast(double speed)
    {
        const double Mu = 0.05;
        var (p, v) = (new Vec3d(1, 0.2, 0), new Vec3d(-0.1, 0.05, speed));
        var (np, nv) = (p, v);
        Sublight.Kepler(Mu, ref p, ref v, 7.3);
        const double H = 1e-3;
        for (int i = 0; i < 7300; i++)
        {
            var thrustFree = new Vec3d(1e-100, 0, 0);   // any thrust takes the numerical path
            Sublight.Propagate(Mu, ref np, ref nv, H, thrustFree);
        }
        Assert.True((p - np).Length < 1e-6, $"{(p - np).Length}");
        Assert.True((v - nv).Length < 1e-6, $"{(v - nv).Length}");
    }

    [Fact]
    public void AKeplerOrbitClosesAfterManyLaps()
    {
        const double Mu = 0.05;
        var (p, v) = (new Vec3d(1, 0, 0), new Vec3d(0, 0, 0.26));
        var (p0, v0) = (p, v);
        var period = Sublight.OrbitOf(Mu, p, v).Period;
        Sublight.Kepler(Mu, ref p, ref v, period * 1000 + 1e-9);
        Assert.True((p - p0).Length < 1e-6);
        Assert.True((v - v0).Length < 1e-6);
    }

    [Fact]
    public void GravityActsOnlyInsideTheSphereOfInfluence()
    {
        var g = Galaxy.Generate(5);
        var s = g.Stars[0];
        var pos = g.Stars.Select(x => x.Position).ToArray();
        var just = s.Position + new Vec3d(s.Soi * 0.999, 0, 0);
        var past = s.Position + new Vec3d(s.Soi * 1.001, 0, 0);
        Assert.Equal(0, Physics.Primary(g, pos, just));
        Assert.Equal(-1, Physics.Primary(g, pos, past));
        Assert.True(Physics.Acceleration(g, pos, just, Vec3d.Zero, Vec3d.Zero).Length > 0.9 * s.Mu / (s.Soi * s.Soi));
        Assert.Equal(0, Physics.Acceleration(g, pos, past, Vec3d.Zero, Vec3d.Zero).Length);
    }

    [Fact]
    public void AClosedOrbitDrawsExactlyOneLapButSearchesFurther()
    {
        var g = Galaxy.Generate(5);
        var st = g.StationAt(g.Ports[0]);
        var star = st.Host!;
        var (p, v) = (st.Position(0), st.Velocity(0));
        var f = Sublight.Predict(star, st, p, v * 1.05, 0, Array.Empty<Maneuver>(), 400);
        var el = Sublight.OrbitOf(star.Mu, p, v * 1.05);
        Assert.Null(f.Exit);
        Assert.Equal(el.Period, f.Track[f.DrawnCount - 1].Time, 6);
        Assert.True((f.Track[f.DrawnCount - 1].Position - p).Length < 1e-6);
        Assert.True(f.Track[^1].Time > el.Period);
    }

    [Fact]
    public void AnEscapingOrbitEndsAtTheEdgeOfTheSphereOfInfluence()
    {
        var g = Galaxy.Generate(5);
        var st = g.StationAt(g.Ports[0]);
        var star = st.Host!;
        var (p, v) = (st.Position(0), st.Velocity(0) * 1.6);
        var f = Sublight.Predict(star, null, p, v, 0, Array.Empty<Maneuver>(), 1);
        Assert.NotNull(f.Exit);
        Assert.Equal(star.Edge, f.Exit!.Value.Position.Length, 6);
        Assert.Equal(f.Track.Count, f.DrawnCount);
        var (ep, ev) = (p, v);
        Sublight.Propagate(star.Mu, ref ep, ref ev, f.Exit.Value.Time);
        Assert.True((ep - f.Exit.Value.Position).Length < 1e-6);
    }

    [Fact]
    public void AShipCoastingBetweenStarsChangesPrimary()
    {
        var g = Galaxy.Generate(5);
        var (from, to) = (g.Stars[g.Ports[0]], g.Stars[g.Ports[1]]);
        var v = new Voyage(g, from.Id, to.Id, Box, Unseen(g), 1);
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
        var (from, to) = (g.Stars[g.Ports[0]], g.Stars[g.Ports[1]]);
        var v = new Voyage(g, from.Id, to.Id, Box, Unseen(g), 1);
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
        Sublight.FollowOn(g, g.Ports[0], once, legs: 1);
        Assert.Null(once.Beyond?.Beyond);
        var none = new OrbitForecast { Exit = f.Exit };
        Sublight.FollowOn(g, g.Ports[0], none, legs: 0);
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
        var inside = g.Stars[g.Ports[1]];
        var at = inside.Position + new Vec3d(inside.Soi * 0.5, 0, 0);
        var n = Sublight.NextPrimary(g, g.Ports[0], at, new Vec3d(1, 0, 0), 7);
        Assert.Equal(inside.Id, n!.Value.Star);
        Assert.Equal(7, n.Value.Time, 9);
    }

    [Fact]
    public void EverySystemHasPlanetsAndEveryStationAHost()
    {
        var g = Galaxy.Generate(5);
        foreach (var star in g.Ports.Concat(g.Depots))
        {
            var system = g.System(star);
            Assert.NotEmpty(system.Children);
            Assert.All(system.Children, p => Assert.True(p.OrbitRadius + p.Edge < system.Edge));
            Assert.All(system.Children.SelectMany(p => p.Children), m => Assert.True(m.OrbitRadius + m.Edge < m.Parent!.Edge));
            var st = g.StationAt(star);
            Assert.True(system.Holds(st.Host!));
            Assert.True(st.Radius > Tuning.DockRange * 3 && st.Radius < st.Host!.Edge);
            Assert.True(st.Radius > st.Host!.BodyRadius);
        }
        Assert.Contains(Enumerable.Range(1, 8).Select(i => Galaxy.Generate((ulong)i)).SelectMany(x => x.Ports.Select(x.StationAt)), s => s.Host!.Kind == BodyKind.Star);
    }

    [Fact]
    public void GoingDownIntoABodyKeepsTheStarFrameState()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.Undock();
        var planet = g.System(g.Ports[0]).Children[0];
        var t = v.AbsoluteTime;
        var offset = planet.Position(t) + new Vec3d(planet.Edge * 0.6, 0, 0);
        var velocity = planet.Velocity(t) + new Vec3d(0, 0, 0.05);
        v.PlaceSublightForTest(offset, velocity);
        var ly = v.JumpState.Position;
        v.AdvanceSublight(0.02, Vec3d.Zero);
        Assert.Equal(planet.Key, v.Primary.Key);
        Assert.True((v.JumpState.Position - ly - velocity * (0.02 * Sublight.Scale)).Length < 1e-3);
    }

    [Fact]
    public void APathLeavingAPlanetCarriesOnAroundTheStar()
    {
        var g = Galaxy.Generate(5);
        var planet = g.System(g.Ports[0]).Children[0];
        var p = new Vec3d(planet.Edge * 0.5, 0, 0);
        var v = new Vec3d(0, 0, Math.Sqrt(2 * planet.Mu / p.Length) * 1.3);
        var f = Sublight.PredictPath(g, planet, p, v, 0, Array.Empty<Maneuver>(), 30);
        Assert.NotNull(f.Exit);
        Assert.Equal(planet.Parent!.Key, f.Beyond!.Center!.Key);
    }

    [Fact]
    public void AnApproachToAPlanetEntersItsSphere()
    {
        var g = Galaxy.Generate(5);
        var system = g.System(g.Ports[0]);
        var planet = system.Children[0];
        var at = planet.Position(10);
        var p = at + new Vec3d(planet.Edge * 1.2, 0, 0);
        var v = (at - p) * 2 + planet.Velocity(10);
        var f = Sublight.PredictPath(g, system, p, v, 10, Array.Empty<Maneuver>(), 30);
        Assert.Equal(planet.Key, f.Enter!.Value.Body.Key);
        Assert.Equal(planet.Edge, f.Enter.Value.Position.Length, 6);
        Assert.Equal(planet.Key, f.Beyond!.Center!.Key);
    }

    [Fact]
    public void APlanetPickedAsTargetGetsNodesAndAClosestApproach()
    {
        var g = Galaxy.Generate(5);
        var system = g.System(g.Ports[0]);
        var planet = system.Children.First(c => c.Children.Count > 0);
        var moon = planet.Children[0];
        // A circular orbit in the star's equator, inside the planet's: planets are tilted, so it crosses their plane.
        var r = planet.OrbitRadius * 0.6;
        var (p, v) = (new Vec3d(r, 0, 0), new Vec3d(0, 0, Math.Sqrt(system.Mu / r)));
        var f = Sublight.PredictPath(g, system, p, v, 0, Array.Empty<Maneuver>(), 150, planet);
        Assert.Same(planet, f.Watched);
        Assert.Contains(f.PlaneCrossings, c => c.Ascending);
        Assert.Contains(f.PlaneCrossings, c => !c.Ascending);
        Assert.NotNull(f.Closest);
        Assert.InRange(f.Closest!.Value.Distance, r * 0.3, r * 2);
        // A moon is reached through its planet, so the star's leg is measured against the planet.
        Assert.Same(planet, Sublight.PredictPath(g, system, p, v, 0, Array.Empty<Maneuver>(), 150, moon).Watched);
        Assert.Same(moon, Sublight.Waypoint(planet, moon));
        Assert.Null(Sublight.Waypoint(planet, planet));
        Assert.Null(Sublight.Waypoint(moon, planet));
    }

    [Fact]
    public void ApsidesFallWhereTheOrbitTurns()
    {
        const double mu = 0.3, r = 0.5;
        var (p, v) = (new Vec3d(r, 0, 0), new Vec3d(0, 0.2, Math.Sqrt(mu / r) * 1.2));
        var el = Sublight.OrbitOf(mu, p, v);
        // A quarter of the way round, the apoapsis comes first and the periapsis back where it started.
        var (q, w) = (p, v);
        Sublight.Kepler(mu, ref q, ref w, el.Period / 4);
        var (pe, ap) = Sublight.NextApsides(mu, q, w, 10);
        Assert.Equal(10 + el.Period / 4, ap!.Value.Time, 6);
        Assert.Equal(10 + el.Period * 3 / 4, pe!.Value.Time, 6);
        Assert.Equal(el.Apoapsis, ap.Value.Radius, 6);
        Assert.Equal(el.Apoapsis, ap.Value.Position.Length, 6);
        Assert.True((pe.Value.Position - p).Length < 1e-6);
        Assert.True(ap.Value.Apo && !pe.Value.Apo);

        // An open orbit has a periapsis only while still falling in.
        var fast = new Vec3d(0, 0, Math.Sqrt(2 * mu / r) * 1.3);
        var (out1, out2) = (p, fast);
        Sublight.Kepler(mu, ref out1, ref out2, 1);
        Assert.Equal((null, null), Sublight.NextApsides(mu, out1, out2, 0));
        var (inbound, _) = Sublight.NextApsides(mu, out1, -out2, 0);
        Assert.Equal(1, inbound!.Value.Time, 6);
        Assert.Equal(r, inbound.Value.Radius, 6);

        // A circle has none, and a forecast marks one of each on an ellipse's lap.
        Assert.Equal((null, null), Sublight.NextApsides(mu, p, new Vec3d(0, 0, Math.Sqrt(mu / r)), 0));
        var star = new Center { Star = 0, Name = "s", Mu = mu, BodyRadius = 0.01, Edge = 50 };
        var f = Sublight.Predict(star, null, q, w, 0, Array.Empty<Maneuver>(), 1);
        Assert.Equal(1, f.Apsides.Count(a => a.Apo));
        Assert.Equal(1, f.Apsides.Count(a => !a.Apo));
    }

    [Fact]
    public void TheTargetStaysWithItsSystem()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], new Cargo("t", 0.5, 0.8, 100), new double[g.Stars.Length], 1);
        Assert.Same(v.LocalStation, v.Goal);
        var other = g.System(g.Ports[1]).Children[0];
        v.SetGoal(other);
        Assert.Same(v.LocalStation, v.Goal);   // a body of another system can't be steered for from here
        var here = g.System(g.Ports[0]).Children.First(c => c.Key != v.Primary.Key);
        v.SetGoal(here);
        Assert.Same(here, v.Goal);
        v.SetGoal(null);
        Assert.Null(v.Goal);                   // cleared: nothing is steered for
        v.SetGoal(v.LocalStation);
        Assert.Same(v.LocalStation, v.Goal);
        v.SetGoal(here);
        var (at, _) = v.RelativeNow(here);
        Assert.True((at - (here.StarPosition(v.AbsoluteTime) - v.Primary.StarPosition(v.AbsoluteTime))).Length < 1e-12);
    }

    [Fact]
    public void SpheresOfInfluenceNeverOverlap()
    {
        int stars = 0, planets = 0, moons = 0, belts = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var g = Galaxy.Generate(seed);
            foreach (var a in g.Stars)
                foreach (var b in g.Stars)
                    if (a.Id < b.Id && (a.Position - b.Position).Length < a.Soi + b.Soi) stars++;
            foreach (var star in g.Stars)
            {
                var system = g.System(star.Id);
                bool Clash(Center x, Center y) => x.OrbitRadius < y.OrbitRadius && x.OrbitRadius + x.Edge > y.OrbitRadius - y.Edge;
                planets += system.Children.Sum(x => system.Children.Count(y => Clash(x, y)));
                moons += system.Children.Sum(p => p.Children.Sum(x => p.Children.Count(y => Clash(x, y))));
            }
            foreach (var port in g.Ports.Concat(g.Depots))
            {
                var st = g.StationAt(port);
                if (st.Host!.Kind == BodyKind.Star && g.System(port).Children.Any(c => Math.Abs(c.OrbitRadius - st.Radius) < c.Edge)) belts++;
            }
        }
        Assert.True(stars + planets + moons + belts == 0, $"stars {stars} planets {planets} moons {moons} belts {belts}");
    }

    [Fact]
    public void DistantStationDoesNotDock()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.AdvanceSublight(10, Vec3d.Zero);
        Assert.Equal(Phase.Sublight, v.Phase);
    }
}

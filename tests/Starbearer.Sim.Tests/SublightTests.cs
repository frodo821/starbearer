using Starbearer.Sim;
using Xunit;

/// <summary>Normal space: orbits, descents, maneuver nodes, the planners, and forecasts within a system.</summary>
public partial class SimTests
{
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
            // X to close, then dock once the readout allows it.
            for (int i = 0; i < 4000 && v.Phase == Phase.Sublight && v.Primary.Key == at; i++)
            {
                if (v.RequestDock()) break;
                v.AdvanceSublight(0.005, v.AssistTowardStation(approach: true) ?? Vec3d.Zero);
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
        // A station well inside its host's sphere, so three burns raise the orbit without leaving it.
        var st = g.Ports.Select(g.StationAt).MinBy(s => s.Radius / s.Host!.Edge)!;
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
}

using Starbearer.Sim;
using Xunit;

/// <summary>Gates: passage, the velocity carried through, tolls, and the jobs gates open up.</summary>
public partial class SimTests
{
    static (Voyage V, Station From, Station To) AtGate()
    {
        var g = Galaxy.Generate(7);
        var gate = g.Gates[0];
        var v = new Voyage(g, gate, g.StarOf(gate), Array.Empty<Consignment>(), Unseen(g), 1);
        v.Undock();
        return (v, g.StationAt(gate), g.StationAt(g.GateLinks[gate]));
    }

    [Fact]
    public void AGateKeepsTheVelocityRelativeToIt()
    {
        var (v, from, to) = AtGate();
        var t = v.AbsoluteTime;
        var (tf, rf, nf) = Sublight.Frame(from.Position(t), from.Velocity(t));
        var offset = tf * (Tuning.DockRange * 0.4) + nf * (Tuning.DockRange * 0.2);
        var relative = tf * 0.03 - rf * 0.01;
        v.PlaceSublightForTest(from.Position(t) + offset, from.Velocity(t) + relative);
        v.BookGate(25);
        v.AdvanceSublight(1e-4, Vec3d.Zero);
        Assert.Equal(to.Star, v.SystemStar);
        Assert.Equal(1, v.Passages);
        Assert.Equal(25, v.TollsDue);
        Assert.Null(v.GateBooked);
        Assert.Equal(Phase.Sublight, v.Phase);
        var t1 = v.AbsoluteTime;
        var (tt, rt, nt) = Sublight.Frame(to.Position(t1), to.Velocity(t1));
        var (_, sv) = v.StationNow();
        var rel = v.SubVelocity - sv;
        Assert.Equal(0.03, rel.Dot(tt), 3);
        Assert.Equal(-0.01, rel.Dot(rt), 3);
        Assert.Equal(0, rel.Dot(nt), 3);
    }

    [Fact]
    public void WithoutABookingAGateIsJustAStation()
    {
        var (v, from, _) = AtGate();
        var t = v.AbsoluteTime;
        var star = v.SystemStar;
        v.PlaceSublightForTest(from.Position(t) + new Vec3d(Tuning.DockRange * 0.4, 0, 0), from.Velocity(t) + new Vec3d(0.03, 0, 0));
        v.AdvanceSublight(1e-4, Vec3d.Zero);
        Assert.Equal(star, v.SystemStar);
        Assert.Equal(0, v.Passages);
    }

    [Fact]
    public void TheTollIsHalfTheFuelThePassageSaves()
    {
        var (v, _, _) = AtGate();
        var g = v.G;
        var gate = v.LocalStation!.Id;
        var toll = Gates.Toll(g, v);
        var fuel = Tuning.FtlFuelPerLyMass * g.GateSpan(gate) * v.Mass * v.Class.Drag;
        Assert.Equal(Math.Round(Tuning.GateTollShare * Tuning.FuelPrice * fuel), toll);
        Assert.True(toll < Tuning.FuelPrice * fuel, "cheaper than flying it");
        Assert.True(toll < Ships.Courier.Tank * Tuning.FuelPrice, "a small ship pays less than a tank");
        // A heavier ship pays more: the same gate with a hold of cargo.
        var loaded = new Voyage(g, gate, v.TargetStar, new[] { new Consignment { Mission = new Mission("m", gate, gate, new Cargo("lead", 1.5, 0.9, 100), 100) } }, Unseen(g), 1);
        Assert.True(Gates.Toll(g, loaded) > toll);
    }

    [Fact]
    public void JobsGoWhereAGateReaches()
    {
        var g = Galaxy.Generate(7);
        var crossing = 0;
        foreach (var port in g.Ports)
        {
            var region = g.StarAt(port).Region;
            var reach = g.LinkedRegions(region).Append(region).ToHashSet();
            foreach (var m in Missions.Board(g, Market.Open(g), port, 0, Array.Empty<Consignment>()))
            {
                Assert.Contains(g.StarAt(m.Target).Region, reach);
                Assert.False(g.Gates.Contains(m.Target));
                if (g.StarAt(m.Target).Region != region) crossing++;
            }
        }
        Assert.True(crossing > 0, "some jobs cross to another region");
    }
}

public partial class SimTests
{
    [Fact]
    public void AGateBurnsPartsAndPowerByHowFarItReachesAndStopsWithout()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        var m = s.MarketIn(g);
        var (near, far) = (g.Gates.Where(g.Alive).MinBy(g.GateSpan), g.Gates.Where(g.Alive).MaxBy(g.GateSpan));
        double Rate(int gate, string ware) => g.Economies[gate].Upkeep.Single(u => u.Ware == ware).Rate;
        Assert.True(Rate(far, "electronics") > Rate(near, "electronics"));
        Assert.Equal(Rate(far, "fission_fuel") / Rate(near, "fission_fuel"), g.GateSpan(far) / g.GateSpan(near), 6);
        Assert.True(Gates.Open(g, m, far));
        // An end out of its parts stops the pair, both ways; the news says so.
        var exit = g.GateLinks[far];
        Array.Clear(m.Stock[exit]);
        Assert.False(Gates.Open(g, m, far));
        Assert.False(Gates.Open(g, m, exit));
        Gates.Watch(g, m);   // its supply lines would fill it again within the day
        Assert.Contains(m.War.Log, e => e.Kind == "gate down" && e.Station == exit);
        Assert.Contains("止まった", War.Describe(g, m.War.Log.Last(e => e.Kind == "gate down")));
        // Supplied again, it runs.
        var e = g.Economies[exit];
        m.Stock[exit] = e.Target.ToArray();
        m.AdvanceTo(g, 2);
        Assert.True(Gates.Open(g, m, far));
        Assert.Contains(m.War.Log, x => x.Kind == "gate up");
    }

    [Fact]
    public void GoodsGoThroughARunningGateAndNotAStoppedOne()
    {
        var g = Galaxy.Generate(5);
        bool Plain(int x) => g.KindOf(x) is not (StationKind.Fortress or StationKind.Armory or StationKind.FuelPlant or StationKind.Gate);
        var routed = g.Neighbours.First(n => n.Gate >= 0 && Plain(n.A) && Plain(n.B) && g.StarAt(n.A).Region != g.StarAt(n.B).Region);
        Assert.True(routed.Closeness > routed.Direct);
        Assert.Equal(0, routed.Direct);   // across regions there is no way but the gate
    }
}

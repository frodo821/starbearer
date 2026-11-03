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
    public void TheTollIsAShareOfShipModulesAndCargo()
    {
        var (v, _, _) = AtGate();
        var bare = Gates.Toll(new SaveData(), v);
        Assert.Equal(Math.Round(Tuning.GateTollRate * Ships.Courier.Price), bare);
        var rich = new SaveData();
        rich.Modules.Add(Modules.Transfer);
        Assert.True(Gates.Toll(rich, v) > bare);
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
        var (near, far) = (g.Gates.MinBy(g.GateSpan), g.Gates.MaxBy(g.GateSpan));
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

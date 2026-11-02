using Starbearer.Sim;
using Xunit;

/// <summary>Gates: passage, the velocity carried through, tolls, and the jobs gates open up.</summary>
public partial class SimTests
{
    static (Voyage V, Station From, Station To) AtGate()
    {
        var g = Galaxy.Generate(7);
        var gate = g.Gates[0];
        var v = new Voyage(g, gate, gate, Array.Empty<Consignment>(), Unseen(g), 1);
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
            var region = g.Stars[port].Region;
            var reach = g.LinkedRegions(region).Append(region).ToHashSet();
            foreach (var m in Missions.Board(g, port, 0, Array.Empty<Consignment>()))
            {
                Assert.Contains(g.Stars[m.Target].Region, reach);
                Assert.False(g.Gates.Contains(m.Target));
                if (g.Stars[m.Target].Region != region) crossing++;
            }
        }
        Assert.True(crossing > 0, "some jobs cross to another region");
    }
}

using Starbearer.Sim;
using Xunit;

/// <summary>Industry: factions building stations where their economy falls short.</summary>
public partial class SimTests
{
    static (Galaxy G, Market M, int Faction) ReadyToBuild()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        var m = s.MarketIn(g);
        return (g, m, 0);
    }

    [Fact]
    public void ANewStationGoesToAFreeSystemOfTheFactionsThatSuitsIt()
    {
        var (g, _, f) = ReadyToBuild();
        var region = Array.IndexOf(g.RegionOwners, f);
        var star = Industry.Place(g, f, StationKind.GasHarvester, g.Regions[region].Center);
        Assert.True(star >= 0);
        Assert.Equal(f, g.RegionOwners[g.Stars[star].Region]);
        Assert.Contains(g.System(star).Children, p => p.Kind == BodyKind.Giant);
        Assert.False(g.Claim(star).Contested);
        Assert.Equal(StationKind.Refinery, Industry.MakerOf("steel"));
        Assert.Null(Industry.MakerOf("missiles"));   // war goods are the armories'
    }

    [Fact]
    public void ALongShortageBuildsAStationThatMakesIt()
    {
        var (g, m, f) = ReadyToBuild();
        m.War.Industry[f] = 1;
        m.War.Shortage[f]["medical"] = Industry.ShortDays + 10;
        var clinics = g.Ports.Count(p => g.KindOf(p) == StationKind.Clinic && g.Alive(p) && g.OwnerOf(p) == f);
        m.AdvanceTo(g, 1);
        var built = Assert.Single(m.War.Log, e => e.Kind == "built" && g.KindOf(e.Station) != StationKind.Fortress);
        Assert.Equal(StationKind.Clinic, g.KindOf(built.Station));
        Assert.Equal(f, built.Faction);
        Assert.Equal(clinics + 1, g.Ports.Count(p => g.KindOf(p) == StationKind.Clinic && g.Alive(p) && g.OwnerOf(p) == f));
        Assert.True(m.War.Industry[f] < 1);
        Assert.Contains("医療ステーション", War.Describe(g, built));
        // Not again until the gap has passed, however short it is.
        m.War.Industry[f] = 1;
        m.War.Shortage[f]["food"] = Industry.ShortDays + 10;
        m.AdvanceTo(g, 10);
        Assert.Single(m.War.Log, e => e.Kind == "built" && g.KindOf(e.Station) != StationKind.Fortress);
    }

    [Fact]
    public void WhatTheNewStationWouldRunOnComesFirstWhenThatIsShortToo()
    {
        var (g, m, f) = ReadyToBuild();
        m.War.Industry[f] = 1;
        m.War.Shortage[f]["electronics"] = Industry.ShortDays + 50;    // a factory's make
        m.War.Shortage[f]["silicon_carbide"] = Industry.ShortDays;      // and what a factory runs on
        m.AdvanceTo(g, 1);
        var built = Assert.Single(m.War.Log, e => e.Kind == "built" && g.KindOf(e.Station) != StationKind.Fortress);
        Assert.Equal(StationKind.Refinery, g.KindOf(built.Station));
    }
}

public partial class SimTests
{
    [Fact]
    public void AStrandedWareGetsAGateBetweenWhereItIsMadeAndWanted()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        var m = s.MarketIn(g);
        var f = 0;
        // A ware piled up at its makers and run out where it is wanted, for months.
        var ware = "medical";
        foreach (var (st, e) in g.Economies.Where(x => g.OwnerOf(x.Key) == f && x.Value.IndexOf(ware) >= 0))
            m.Stock[st][e.IndexOf(ware)] = e.Makes(ware) ? e.Target[e.IndexOf(ware)] * 1.9 : 0;
        m.War.Stranded[f][ware] = Industry.ShortDays + 1;
        m.War.GateProgress[f] = 1;
        var before = g.Gates.Count(g.Alive);
        // Built on the day's stock, before evening out has run its course.
        Industry.BuildGate(g, m, f);
        var built = Assert.Single(m.War.Log, e => e.Kind == "gate built");
        Assert.Equal(f, built.Faction);
        Assert.Equal(before + 2, g.Gates.Count(g.Alive));
        Assert.True(Gates.Open(g, m, built.Station));
        Assert.True(g.GateSpan(built.Station) >= Tuning.GateMinLy);
        Assert.Contains(g.Neighbours, n => n.Gate == built.Station || n.Gate == g.GateLinks[built.Station]);   // goods take it
        Assert.True(m.War.GateProgress[f] < 1);
        Assert.Contains("ゲートを建てた", War.Describe(g, built));
    }
}

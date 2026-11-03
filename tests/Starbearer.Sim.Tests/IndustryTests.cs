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
    public void EveryRegionHasSitesToGrowOn()
    {
        var g = Galaxy.Generate(5);
        var sites = g.UnbuiltSites.Where(x => g.KindOf(x) < StationKind.Depot).ToList();
        Assert.All(g.Regions, r => Assert.True(sites.Count(x => g.StarAt(x).Region == r.Id) >= 5));
        Assert.All(sites, x => Assert.Equal(g.RegionOwners[g.StarAt(x).Region], g.OwnerOf(x)));
        Assert.All(sites, x => Assert.False(g.Alive(x)));
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
    public void GateSitesWaitUnbuiltAndPassNothing()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        var m = s.MarketIn(g);
        var sites = g.Gates.Where(x => g.UnbuiltSites.Contains(x)).ToList();
        Assert.NotEmpty(sites);
        Assert.All(sites, x => Assert.Contains(g.GateLinks[x], sites));
        Assert.All(sites, x => Assert.True(g.GateSpan(x) >= Tuning.GateSiteMinLy));
        Assert.All(sites, x => Assert.False(Gates.Open(g, m, x)));
        Assert.All(g.Regions, r => Assert.DoesNotContain(g.LinkedRegions(r.Id), x => g.Gates.Where(g.Alive).All(gt => g.StarAt(gt).Region != x)));
    }

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
        Assert.True(m.War.GateProgress[f] < 1);
        Assert.Contains("ゲートを建てた", War.Describe(g, built));
    }
}

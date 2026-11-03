using Starbearer.Sim;
using Xunit;

/// <summary>War: battlefields, fire decided by supply, stations falling and the ships lost with them, building.</summary>
public partial class SimTests
{
    /// <summary>A galaxy at day 0 and one of its battlefields: the defender's fortress and the attacker's.</summary>
    static (SaveData S, Galaxy G, Market M, int Star, int Defender, int Attacker) AtTheFront()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        var m = s.MarketIn(g);
        var star = War.Fronts(g).First();
        var forts = g.StationsIn(star).Where(x => g.KindOf(x) == StationKind.Fortress).ToList();
        var holder = g.Claim(star).Holder;
        return (s, g, m, star, forts.First(x => g.OwnerOf(x) == holder), forts.First(x => g.OwnerOf(x) != holder));
    }

    static void Starve(Galaxy g, Market m, int fortress) => Array.Clear(m.Stock[fortress]);

    [Fact]
    public void TheWarStartsWithBattlefieldsAndSitesLeftToBuild()
    {
        var (_, g, m, star, defender, attacker) = AtTheFront();
        Assert.Equal(2, War.Sides(g, star).Length);
        Assert.True(g.Claim(star).Contested);
        Assert.NotEmpty(g.UnbuiltSites);
        Assert.All(g.UnbuiltSites, x => Assert.False(g.Alive(x)));
        Assert.All(g.UnbuiltSites, x => Assert.DoesNotContain(x, g.StationsIn(g.StarOf(x))));
        Assert.All(g.Factions, f => Assert.Contains(f.Weapon, War.Weapons));
        Assert.NotEqual(g.Factions[0].Weapon, g.Factions[1].Weapon);
        // A fortress uses its faction's weapon; an armory, where there is one, makes it.
        Assert.True(g.Economies[defender].Uses(g.Factions[g.OwnerOf(defender)].Weapon));
        Assert.All(g.Ports.Where(p => g.KindOf(p) == StationKind.Armory), a => Assert.True(g.Economies[a].Makes(g.Factions[g.OwnerOf(a)].Weapon)));
        Assert.Empty(Missions.Board(g, m, defender, 0, Array.Empty<Consignment>()));
        Assert.True(War.Strength(g, m, attacker) > 0.9);   // they open supplied
    }

    [Fact]
    public void EquallySuppliedFortressesHoldAndTheStarvedOneFalls()
    {
        var (s, g, m, star, defender, attacker) = AtTheFront();
        var (sd, gd, md, _, d2, a2) = AtTheFront();
        // Both supplied: neither gives ground over a month.
        for (int day = 1; day <= 30; day++)
        {
            foreach (var f in new[] { d2, a2 }) { var e = gd.Economies[f]; md.Stock[f] = e.Target.ToArray(); }
            md.AdvanceTo(gd, day);
        }
        Assert.True(gd.War.HullOf(d2) > 0.95 && gd.War.HullOf(a2) > 0.95);

        // The defender's ammunition cut off: the attacker wears it down and then whatever else of its side is there.
        s.Fleet.Add(new OwnedShip { Id = 2, Station = defender });
        var theirs = g.StationsIn(star).Where(x => g.OwnerOf(x) == g.OwnerOf(defender)).ToList();
        int day0 = 0;
        for (int day = 1; day <= 2000 && theirs.Any(g.Alive); day++)
        {
            Starve(g, m, defender);
            var e = g.Economies[attacker]; m.Stock[attacker] = e.Target.ToArray();
            m.AdvanceTo(g, day);
            day0 = day;
        }
        Assert.All(theirs, x => Assert.False(g.Alive(x)));
        Assert.InRange(day0, 60, 1000);                                  // it takes months, not days
        Assert.Equal((g.OwnerOf(attacker), false), g.Claim(star));       // the system changes hands
        Assert.Contains(m.War.Log, x => x.Kind == "destroyed" && x.Station == defender);
        Assert.Contains(m.War.Log, x => x.Kind == "taken" && x.Star == star && x.Faction == g.OwnerOf(attacker));
        // A ship left there is lost with it; the one flown is not.
        var lost = War.Casualties(s, g);
        Assert.Equal(2, Assert.Single(lost).Id);
        Assert.DoesNotContain(s.Fleet, x => x.Id == 2);
        Assert.Contains(s.Fleet, x => x.Id == s.Flown);
        // The war is saved with the market.
        var back = SaveData.FromJson(s.ToJson())!;
        Assert.Equal(m.War.Down, back.Market!.War.Down);
        Assert.Equal(m.War.Log, back.Market.War.Log);
    }

    [Fact]
    public void AFactionWithMaterialsBuildsAFortressWhereTheWarNeedsIt()
    {
        var (_, g, m, _, defender, _) = AtTheFront();
        var f = g.OwnerOf(defender);
        var before = War.Fortresses(g).Count(x => g.OwnerOf(x) == f);
        m.War.Progress[f] = 1.5;
        m.AdvanceTo(g, 1);
        var built = Assert.Single(m.War.Log, x => x.Kind == "built");
        Assert.Equal(f, built.Faction);
        Assert.True(g.Alive(built.Station));
        Assert.Equal(before + 1, War.Fortresses(g).Count(x => g.OwnerOf(x) == f));
        Assert.Equal(0, m.War.Built[built.Station]);   // the day it went up
        Assert.True(m.War.Progress[f] < 1);
    }
}

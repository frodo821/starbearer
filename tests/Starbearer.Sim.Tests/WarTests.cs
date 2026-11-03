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
    public void TheWarStartsWithBattlefields()
    {
        var (_, g, m, star, defender, attacker) = AtTheFront();
        Assert.Equal(2, War.Sides(g, star).Length);
        Assert.True(g.Claim(star).Contested);
        Assert.All(g.StationStars.Keys, x => Assert.True(g.Alive(x)));   // nothing waits unbuilt
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
        // Both supplied: they wear each other down only slowly.
        for (int day = 1; day <= 30; day++)
        {
            foreach (var f in new[] { d2, a2 }) { var e = gd.Economies[f]; md.Stock[f] = e.Target.ToArray(); }
            md.AdvanceTo(gd, day);
        }
        Assert.True(gd.War.HullOf(d2) > 0.85 && gd.War.HullOf(a2) > 0.85, $"{gd.War.HullOf(d2)} {gd.War.HullOf(a2)}");

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
        // The system changed hands (the loser may since have come back to contest it).
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
    public void HavingTakenASystemAFactionCarriesTheWarToTheNext()
    {
        var (_, g, m, star, defender, attacker) = AtTheFront();
        var (us, them) = (g.OwnerOf(attacker), g.OwnerOf(defender));
        // The attacker wins the battlefield...
        foreach (var x in g.StationsIn(star).Where(x => g.OwnerOf(x) == them).ToList())
        {
            m.War.Hull[x] = 1e-9;
            Array.Clear(m.Stock[x]);
        }
        for (int day = 1; g.StationsIn(star).Any(x => g.OwnerOf(x) == them) && day < 50; day++) m.AdvanceTo(g, day);
        Assert.Equal((us, false), g.Claim(star));
        // ...and its next fortress goes to an enemy system within reach of what it holds: the front moves on.
        var before = War.Fortresses(g).Where(x => g.OwnerOf(x) == us).ToHashSet();
        m.War.Progress[us] = 1;
        m.AdvanceTo(g, m.Day + 1);
        var built = War.Fortresses(g).Where(x => g.OwnerOf(x) == us).Except(before).Single();
        var at = g.StarOf(built);
        Assert.True(g.BuiltDuringGame(built));
        Assert.True(g.Claim(at).Contested);
        Assert.Contains(them, War.Sides(g, at));
        Assert.True(g.Stars.Where(s => War.Sides(g, s.Id).Contains(us) && s.Id != at).Min(s => (s.Position - g.Stars[at].Position).Length) <= Tuning.FrontReachLy);
        // It is saved and comes back with the market.
        var s2 = new SaveData { Market = m };
        var back = SaveData.FromJson(s2.ToJson())!;
        var g2 = Galaxy.Generate(g.Seed);
        back.Market!.AdvanceTo(g2, m.Day);
        Assert.True(g2.Alive(built));
        Assert.Equal(StationKind.Fortress, g2.KindOf(built));
        Assert.Equal(at, g2.StarOf(built));
    }

    [Fact]
    public void AGateCaughtInTheFightingIsDestroyed()
    {
        var (_, g, m, _, defender, attacker) = AtTheFront();
        var (us, them) = (g.OwnerOf(attacker), g.OwnerOf(defender));
        var gate = g.Gates.First(x => g.OwnerOf(x) == them && g.Alive(x));
        var at = g.StarOf(gate);
        var fort = m.Build(g, at, StationKind.Fortress, us, fill: 1);
        Assert.True(g.Claim(at).Contested);
        for (int day = 1; g.Alive(gate) && day < 3000; day++)
        {
            foreach (var x in g.StationsIn(at).Where(x => g.OwnerOf(x) == them && g.KindOf(x) == StationKind.Fortress)) Array.Clear(m.Stock[x]);
            m.Stock[fort] = g.Economies[fort].Target.ToArray();
            m.AdvanceTo(g, day);
        }
        Assert.False(g.Alive(gate));
        Assert.False(g.Alive(g.GateLinks[gate]));   // its other end falls with it
        Assert.Contains(m.War.Log, e => e.Kind == "destroyed" && e.Station == gate);
        Assert.Contains(m.War.Log, e => e.Kind == "collapsed" && e.Station == g.GateLinks[gate]);
        Assert.Contains("崩れた", War.Describe(g, m.War.Log.Last(e => e.Kind == "collapsed")));
        Assert.DoesNotContain(g.Neighbours, n => n.Gate == gate || n.Gate == g.GateLinks[gate]);
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

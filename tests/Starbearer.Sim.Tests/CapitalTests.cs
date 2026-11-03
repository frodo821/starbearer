using Starbearer.Sim;
using Xunit;

/// <summary>Capitals: where they stand, what they offer a friend, and what their fall does to their faction.</summary>
public partial class SimTests
{
    [Fact]
    public void EachFactionHasOneCapitalDeepInItsTerritory()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var g = Galaxy.Generate(seed);
            foreach (var f in g.Factions)
            {
                var capital = Assert.Single(g.Ports, p => g.KindOf(p) == StationKind.Capital && g.OwnerOf(p) == f.Id);
                var star = g.StarOf(capital);
                Assert.Equal(f.Id, g.RegionOwners[g.Stars[star].Region]);
                Assert.True(g.GateIn(star) < 0, "not a gate's system");
                Assert.DoesNotContain(g.StationsIn(star), x => g.KindOf(x) is StationKind.Fortress or StationKind.Armory);
                Assert.True(g.StationsIn(star).Count() >= 3, "a hub");
                Assert.True(g.Economies[capital].Uses("food"));
                // Deep: farther from the enemy's regions than most of the faction's own stars.
                double FromEnemy(Star s) => g.Stars.Where(e => g.RegionOwners[e.Region] != f.Id).Min(e => (e.Position - s.Position).Length);
                var own = g.Stars.Where(s => g.RegionOwners[s.Region] == f.Id).Select(FromEnemy).OrderBy(x => x).ToList();
                Assert.True(FromEnemy(g.Stars[star]) >= own[own.Count / 2], $"seed {seed}: capital {FromEnemy(g.Stars[star]):F0} ly from the enemy, median {own[own.Count / 2]:F0}");
            }
        }
    }

    [Fact]
    public void ACapitalOffersStateWorkOnlyToAFriendAndCountsServiceMore()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        var m = s.MarketIn(g);
        var capital = War.Capital(g, 0);
        Assert.DoesNotContain(Missions.Board(g, m, capital, 0, Array.Empty<Consignment>()), x => x.State);
        var state = Missions.Board(g, m, capital, 0, Array.Empty<Consignment>(), friend: true).Where(x => x.State).ToList();
        Assert.NotEmpty(state);
        Assert.All(state, x =>
        {
            Assert.Equal(0, g.OwnerOf(x.Target));
            Assert.True((g.StarAt(x.Target).Position - g.StarAt(capital).Position).Length >= 15, "far off");
            Assert.Null(x.Cargo.Ware);
        });
        // The board at the dock follows the standing: a friend sees the state work.
        s.PortStation = capital;
        s.Standing = new double[g.Factions.Length];
        Assert.DoesNotContain(Missions.OffersFor(s, g), x => x.State);
        s.Standing[0] = Factions.FriendlyAt;
        Assert.Contains(Missions.OffersFor(s, g), x => x.State);
        // The same service counts more at the capital than at another station of its faction.
        var other = g.Ports.First(p => g.OwnerOf(p) == 0 && g.KindOf(p) == StationKind.City || g.OwnerOf(p) == 0 && g.KindOf(p) == StationKind.Colony);
        s.Standing = new double[g.Factions.Length];
        var atCapital = Factions.Serve(s, g, capital, 300, null);
        s.Standing = new double[g.Factions.Length];
        var elsewhere = Factions.Serve(s, g, other, 300, null);
        Assert.Equal(elsewhere * Factions.CapitalWeight, atCapital, 9);
    }

    [Fact]
    public void ACapitalsFallWeakensItsFactionUntilItMovesItsSeat()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        var m = s.MarketIn(g);
        const int f = 0, enemy = 1;
        var capital = War.Capital(g, f);
        var star = g.StarOf(capital);
        Assert.Equal(1.0, War.Command(g, m, f), 9);
        m.War.Hull[capital] = 0.5;
        Assert.Equal(0.9, War.Command(g, m, f), 9);   // damaged, it commands less

        // An enemy fortress in its system, kept supplied, with the faction building nothing to relieve it.
        var fort = m.Build(g, star, StationKind.Fortress, enemy, fill: 1);
        foreach (var x in g.StationsIn(star).Where(x => g.OwnerOf(x) == f)) m.War.Hull[x] = 0.05;
        var day = (int)m.Day;
        while (g.Alive(capital) && day < 2000)
        {
            m.Stock[fort] = g.Economies[fort].Target.ToArray();
            m.War.Progress[f] = 0;
            m.AdvanceTo(g, ++day);
        }
        Assert.False(g.Alive(capital));
        var fell = Assert.Single(m.War.Log, e => e.Kind == "capital fell");
        Assert.Equal(capital, fell.Station);
        Assert.Equal(War.FallenCommand, War.Command(g, m, f), 9);
        Assert.Contains("陥落", War.Describe(g, fell));
        // Armoured: it holds out far longer than its last stand's few days of structure would by fire alone.
        Assert.True(fell.Day - g.War.Built[fort] > 0.05 / War.FireRate, $"fell after {fell.Day - g.War.Built[fort]:F0} days");

        // Some months on, the faction moves its seat to a system of its own, and commands again.
        m.AdvanceTo(g, fell.Day + War.RelocateDays + 1);
        var moved = Assert.Single(m.War.Log, e => e.Kind == "capital moved");
        var seat = War.Capital(g, f);
        Assert.Equal(seat, moved.Station);
        Assert.NotEqual(star, g.StarOf(seat));
        Assert.Equal((f, false), g.Claim(g.StarOf(seat)));
        Assert.InRange(War.Command(g, m, f), 0.89, 1);
        Assert.Empty(m.War.CapitalFell);
        // All of it is saved with the market.
        var back = SaveData.FromJson(s.ToJson())!;
        Assert.Equal(m.War.Log, back.Market!.War.Log);
    }
}

using Starbearer.Sim;
using Xunit;

/// <summary>Factions: who owns which regions and stations, standing, and who holds a system.</summary>
public partial class SimTests
{
    [Fact]
    public void TheFactionsSplitTheRegionsEachARunAGateJoins()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var g = Galaxy.Generate(seed);
            Assert.Equal(Tuning.FactionCount, g.Factions.Length);
            Assert.Equal(g.Factions.Length, g.Factions.Select(f => f.Name).Distinct().Count());
            Assert.Equal(g.Regions.Length, g.RegionOwners.Length);
            foreach (var f in g.Factions)
            {
                var mine = Enumerable.Range(0, g.Regions.Length).Where(i => g.RegionOwners[i] == f.Id).ToList();
                Assert.Equal(g.Regions.Length / g.Factions.Length, mine.Count);
                Assert.All(mine, a => Assert.True(mine.Count == 1 || g.LinkedRegions(a).Any(mine.Contains), $"seed {seed}: {f.Name} is cut in two"));
            }
            // Every station but the fortresses belongs to its region's faction, and a system without one is
            // held by it alone. Each front has a battlefield on each side from the start.
            var forts = g.StationStars.Keys.Where(st => g.KindOf(st) == StationKind.Fortress).ToHashSet();
            Assert.All(g.StationStars.Keys.Except(forts), st => Assert.Equal(g.RegionOwners[g.StarAt(st).Region], g.OwnerOf(st)));
            Assert.All(g.StationStars.Values.Distinct().Where(star => !g.AllStationsIn(star).Any(forts.Contains)),
                star => Assert.Equal((g.RegionOwners[g.Stars[star].Region], false), g.Claim(star)));
            Assert.All(War.Fronts(g), star => Assert.Equal(g.RegionOwners[g.Stars[star].Region], g.Claim(star).Holder));   // the defender came first
            Assert.NotEmpty(War.Fronts(g));
            // The two sides meet: some gate leads from one faction's region into the other's.
            Assert.Contains(g.Gates, gate => g.OwnerOf(gate) != g.OwnerOf(g.GateLinks[gate]));
        }
    }

    static (SaveData S, Galaxy G, int Ours, int Theirs) Contested()
    {
        const int Rival = Galaxy.FirstStationId + 2;
        var g = WithPorts();
        g = new Galaxy
        {
            Seed = g.Seed, Stars = g.Stars, Nebulae = g.Nebulae, Ports = new[] { From, To, Rival },
            StationStars = new() { [From] = 0, [To] = 1, [Rival] = 0 },
            Kinds = new() { [From] = StationKind.City, [To] = StationKind.City, [Rival] = StationKind.Factory },
            Factions = new[] { new Faction(0, "A"), new Faction(1, "B") },
            RegionOwners = new[] { 0 },
            Owners = new() { [Rival] = 1 },
        };
        return (new SaveData { Fleet = new() { new OwnedShip { Id = 1, Station = From } } }, g, From, Rival);
    }

    [Fact]
    public void ASystemIsHeldByItsFirstStationsFactionAndContestedWithAnother()
    {
        var (_, g, ours, theirs) = Contested();
        Assert.Equal(0, g.OwnerOf(ours));
        Assert.Equal(1, g.OwnerOf(theirs));
        Assert.Equal((0, true), g.Claim(0));
        Assert.Equal((0, false), g.Claim(1));
        Assert.Equal("係争中（A 領）", Factions.ClaimText(g, 0));
        Assert.Equal((-1, false), WithPorts().Claim(0));      // a galaxy with no factions: nobody's
    }

    [Fact]
    public void ServingAFactionRaisesItAndLowersItsEnemyUntilItShutsTheDoor()
    {
        var (s, g, ours, theirs) = Contested();
        Assert.Equal(Stance.Neutral, Factions.Toward(s, g, theirs));
        var moved = Factions.Serve(s, g, ours, 1500, "food");
        Assert.Equal(1, moved, 9);
        Assert.Equal(1, s.Standing[0], 9);
        Assert.Equal(-Factions.EnemyShare, s.Standing[1], 9);
        Factions.Serve(s, g, ours, 1500, "missiles");                  // war goods count four times over
        Assert.Equal(5, s.Standing[0], 9);
        Assert.Equal(Stance.Friendly, Factions.Toward(s, g, ours));
        Assert.Equal(Stance.Hostile, Factions.Toward(s, g, theirs));
        Assert.False(Factions.Admits(s, g, theirs));
        Assert.True(Factions.Admits(s, g, ours));
        Factions.Serve(s, g, ours, 1e6, null);
        Assert.Equal(Factions.Ceiling, s.Standing[0]);
        Assert.Equal(Factions.Floor, s.Standing[1]);
        Assert.Equal(0, Factions.Serve(s, WithPorts(), From, 1500, null));   // nobody's station: nothing moves
    }

    [Fact]
    public void DeliveringAndSellingAreServiceToTheStationsOwner()
    {
        var (s, g, job) = WithAWareJob();
        Assert.True(Missions.Take(s, g, job));
        var owner = g.OwnerOf(job.Target);
        var moved = Missions.Deliver(s, g, s.Hold.Single(c => c.Mission == job));
        Assert.Equal(job.Pay * Factions.PerCredit * (Factions.Military.Contains(job.Cargo.Ware!) ? Factions.MilitaryWeight : 1), moved, 9);
        Assert.True(s.Standing[owner] > 0);
        Assert.True(s.Standing[1 - owner] < 0);

        var (t, h, m) = DockedWhereMade("food");
        Assert.True(Trade.Buy(t, h, m, "food", 3));
        t.PortStation = h.Economies.First(e => e.Value.Uses("food")).Key;
        var got = Trade.SellUnits(t, h, m, "food", 2)!.Value;
        Assert.Equal(got * Factions.PerCredit, t.Standing[h.OwnerOf(t.PortStation)], 9);
        Assert.NotNull(Trade.SaleQuote(t, h, m, "food", 1));
        Assert.Equal(got * Factions.PerCredit, t.Standing[h.OwnerOf(t.PortStation)], 9);   // a quote serves no one
        var back = SaveData.FromJson(t.ToJson())!;
        Assert.Equal(t.Standing, back.Standing);
    }
}

public partial class SimTests
{
    [Fact]
    public void EachFactionMakesWhatItUsesAndEveryRegionMakesFuel()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var g = Galaxy.Generate(seed);
            foreach (var f in g.Factions)
            {
                var mine = g.Economies.Where(x => g.OwnerOf(x.Key) == f.Id && !g.UnbuiltSites.Contains(x.Key)).Select(x => x.Value).ToList();
                var used = mine.SelectMany(e => e.Wares.Where(e.Uses)).Distinct();
                Assert.All(used, w => Assert.True(mine.Any(e => e.Makes(w)), $"seed {seed}: {f.Name} uses {w} but makes none"));
            }
            Assert.All(g.Regions, r => Assert.Contains(g.Ports, p => g.StarAt(p).Region == r.Id && g.KindOf(p) == StationKind.FuelPlant));
        }
    }
}

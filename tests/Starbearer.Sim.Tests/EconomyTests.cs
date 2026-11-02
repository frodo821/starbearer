using Starbearer.Sim;
using Xunit;

/// <summary>The economy: what stations make and use, stock and price, and how the market moves on.</summary>
public partial class SimTests
{
    [Fact]
    public void StationsWorkTheBodiesTheirKindNeeds()
    {
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var g = Galaxy.Generate(seed);
            foreach (var star in g.Economies.Keys)
            {
                var kind = Missions.KindOf(g, star);
                var host = g.StationAt(star).Host!;
                if (kind == StationKind.GasHarvester) Assert.Equal(BodyKind.Giant, host.Kind);
                if (kind is StationKind.Mine or StationKind.Gate) Assert.Equal(BodyKind.Star, host.Kind);
            }
            // Every economic kind is somewhere, so every ware is made somewhere.
            for (var k = (StationKind)0; k < StationKind.Depot; k++) Assert.Contains(k, g.Kinds.Values);
            var made = g.Economies.Values.SelectMany(e => e.Wares.Where(e.Makes)).ToHashSet();
            var used = g.Economies.Values.SelectMany(e => e.Wares.Where(e.Uses)).ToHashSet();
            Assert.Subset(made, used);
        }
    }

    [Fact]
    public void PriceIsBaseAtTheTargetAndRisesAsStockRunsOut()
    {
        var w = Wares.Get("steel");
        Assert.Equal(w.Price, Economy.Price(w, 1), 9);
        Assert.True(Economy.Price(w, 0.5) > w.Price);
        Assert.True(Economy.Price(w, 0) > Economy.Price(w, 0.5));
        Assert.True(Economy.Price(w, 1.5) < w.Price);
        Assert.Equal(Economy.Price(w, Economy.Capacity), Economy.Price(w, 10), 9);   // full is full
        Assert.InRange(Economy.Price(w, 0) / w.Price, 1.7, 1.9);
    }

    [Fact]
    public void EveryStationKeepsFuelForShips()
    {
        var g = Galaxy.Generate(5);
        Assert.All(g.Economies.Values, e => Assert.True(e.IndexOf(Wares.ShipFuel) >= 0 && e.Target[e.IndexOf(Wares.ShipFuel)] >= Economy.ShipFuelStock));
        Assert.Equal(g.Ports.Length + g.Depots.Length + g.Gates.Length, g.Economies.Count);
    }

    /// <summary>The first station of a kind, and the market as opened.</summary>
    static (Galaxy G, Market M, int Star, StationEconomy E) StationOf(StationKind kind)
    {
        var g = Galaxy.Generate(5);
        var star = g.Economies.Keys.First(s => Missions.KindOf(g, s) == kind);
        return (g, Market.Open(g), star, g.Economies[star]);
    }

    [Fact]
    public void ALineRunsOnlyAsFarAsItsInputsAndRoomAllow()
    {
        var (g, m, star, e) = StationOf(StationKind.Factory);
        // Cut it off from its neighbours: an economy of one.
        var alone = new Market { Stock = { [star] = (double[])m.Stock[star].Clone() } };
        var solo = new Galaxy { Seed = g.Seed, Stars = g.Stars, Nebulae = g.Nebulae, Ports = new[] { star }, Kinds = g.Kinds };
        double S(string w) => alone.StockOf(solo, star, w);
        foreach (var w in e.Wares) alone.Take(solo, star, w, 1e9);
        alone.Put(solo, star, "fission_fuel", 10);
        alone.Put(solo, star, "silicon_carbide", 0.3);   // half a day of it
        alone.Put(solo, star, "titanium", 100);
        alone.AdvanceTo(solo, 1);
        Assert.Equal(0, S("silicon_carbide"), 9);
        Assert.Equal(0.6 * 0.5, S("electronics"), 9);     // half a day's make
        Assert.Equal(100 - 0.35 * 0.5, S("titanium"), 9);
        // Without power, nothing runs.
        alone.Take(solo, star, "fission_fuel", 1e9);
        alone.Put(solo, star, "silicon_carbide", 100);
        alone.AdvanceTo(solo, 2);
        Assert.Equal(0.3, S("electronics"), 9);
        // Full of output, the line stops.
        alone.Put(solo, star, "fission_fuel", 10);
        alone.Put(solo, star, "electronics", 1e3);
        var before = S("silicon_carbide");
        alone.AdvanceTo(solo, 3);
        Assert.Equal(before, S("silicon_carbide"), 9);
    }

    [Fact]
    public void NeighboursEvenOutWhatTheyBothHold()
    {
        var g = Galaxy.Generate(5);
        var (a, b, _) = g.Neighbours.First(n => g.Stars[n.A].Region == g.Stars[n.B].Region);
        var m = Market.Open(g);
        var fuel = Wares.ShipFuel;
        m.Take(g, a, fuel, 1e9);
        m.Put(g, b, fuel, 200);
        var gap = m.Fill(g, b, fuel)!.Value - m.Fill(g, a, fuel)!.Value;
        m.AdvanceTo(g, 5);
        Assert.True(m.Fill(g, b, fuel) - m.Fill(g, a, fuel) < gap);
        Assert.True(m.StockOf(g, a, fuel) > 0);
    }

    [Fact]
    public void TheMarketStaysSaneOverYearsAndIsTheSameHoweverItIsStepped()
    {
        foreach (var seed in new ulong[] { 5, 9 })
        {
            var g = Galaxy.Generate(seed);
            var once = Market.Open(g);
            once.AdvanceTo(g, 400);
            var steps = Market.Open(g);
            for (int d = 37; d < 400; d += 37) steps.AdvanceTo(g, d + 0.5);
            steps.AdvanceTo(g, 400);
            Assert.Equal(400, once.Day);
            foreach (var (star, e) in g.Economies)
                for (int i = 0; i < e.Wares.Length; i++)
                {
                    var s = once.Stock[star][i];
                    Assert.False(double.IsNaN(s));
                    Assert.InRange(s, 0, e.Target[i] * Economy.Capacity + 1e-6);
                    Assert.Equal(s, steps.Stock[star][i], 9);
                }
            // Fuel is made in every region, so it doesn't run out across the galaxy.
            var dry = g.Economies.Keys.Count(s => once.Fill(g, s, Wares.ShipFuel) < 0.1);
            Assert.True(dry < g.Economies.Count / 5, $"{dry} stations out of fuel");
        }
    }
}

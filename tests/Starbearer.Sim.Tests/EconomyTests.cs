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

public partial class SimTests
{
    /// <summary>A new galaxy docked at a station that makes this ware, with credits to spend.</summary>
    static (SaveData S, Galaxy G, Market M) DockedWhereMade(string ware, double credits = 5000)
    {
        var (s, g) = SaveData.NewGalaxy(5);
        s.PortStar = g.Economies.First(e => e.Value.Makes(ware)).Key;
        s.Credits = credits;
        return (s, g, s.MarketIn(g));
    }

    [Fact]
    public void BuyingTakesStockAndRaisesThePriceUnitByUnit()
    {
        var (s, g, m) = DockedWhereMade("food");
        var star = s.PortStar;
        var price = m.Price(g, star, "food")!.Value;
        var one = Trade.Cost(g, m, star, "food", 1, true);
        Assert.True(one > price);                                         // the spread
        Assert.True(Trade.Cost(g, m, star, "food", 4, true) > 4 * one);   // dearer as stock falls
        var stock = m.StockOf(g, star, "food");
        Assert.True(Trade.Buy(s, g, m, "food", 3));
        Assert.Equal(stock - 3, m.StockOf(g, star, "food"), 9);
        Assert.True(m.Price(g, star, "food") > price);
        var lot = Assert.Single(s.Hold);
        Assert.False(lot.IsJob);
        Assert.Equal(3, lot.Mission.Cargo.Volume);
        Assert.Equal("food", lot.Mission.Cargo.Ware);
        Assert.Equal(5000 - lot.Cost, s.Credits, 9);
        // More of the same joins the lot.
        Assert.True(Trade.Buy(s, g, m, "food", 2));
        Assert.Equal(5, Assert.Single(s.Hold).Mission.Cargo.Volume);
    }

    [Fact]
    public void AStationSellsWhatItMakesAndBuysWhatItUses()
    {
        var (s, g, m) = DockedWhereMade("steel");
        Assert.True(Trade.Sells(g, s.PortStar, "steel"));
        Assert.True(Trade.Sells(g, s.PortStar, Wares.ShipFuel));   // fuel everywhere
        Assert.False(Trade.Buys(g, s.PortStar, "steel"));
        Assert.Equal(0, Trade.MaxBuy(s, g, m, "food"));             // not made here
        // The courier's bays take containers only: steel is bulk.
        Assert.Equal(0, Trade.MaxBuy(s, g, m, "steel"));
        s.Fleet.Add(OwnedShip.Build(Ships.Bulker, 2, s.PortStar));
        Hangar.Board(s, 2);
        var most = Trade.MaxBuy(s, g, m, "steel");
        Assert.True(most > 0 && most <= m.StockOf(g, s.PortStar, "steel"));
        Assert.True(Trade.Buy(s, g, m, "steel", most));
        Assert.Equal(0, Trade.MaxBuy(s, g, m, "steel"));               // the bays, the stock or the credits ran out
        Assert.False(Trade.Buy(s, g, m, "steel", 1));
        // Sold where it is used, at that station's price.
        var lot = s.Hold.Single();
        Assert.Null(Trade.Offer(g, m, s.PortStar, lot));
        var user = g.Economies.First(e => e.Value.Uses("steel")).Key;
        s.PortStar = user;   // as if flown there (test only)
        var userStock = m.StockOf(g, user, "steel");
        var offer = Trade.Offer(g, m, user, lot)!.Value;
        var credits = s.Credits;
        Assert.Equal(offer, Trade.Sell(s, g, m, lot));
        Assert.Equal(credits + offer, s.Credits, 9);
        Assert.Empty(s.Hold);
        Assert.Equal(userStock + most, m.StockOf(g, user, "steel"), 9);
    }

    [Fact]
    public void GoodsOfOnesOwnCountInTheTollAndCostNothingToLeave()
    {
        var (s, g, m) = DockedWhereMade("food");
        Trade.Buy(s, g, m, "food", 2);
        var lot = s.Hold.Single();
        Assert.Equal(lot.Cost, lot.Value, 9);
        Assert.Equal(0, Missions.DropFee(lot));
        var credits = s.Credits;
        Missions.Drop(s, lot);
        Assert.Empty(s.Hold);
        Assert.Equal(credits, s.Credits);
    }

    [Fact]
    public void TheMarketAndGoodsAboardSurviveASave()
    {
        var (s, g, m) = DockedWhereMade("food");
        Trade.Buy(s, g, m, "food", 2);
        s.TotalDays = 30;
        s.MarketIn(g);
        var back = SaveData.FromJson(s.ToJson())!;
        Assert.Equal(30, back.Market!.Day);
        foreach (var (star, stock) in s.Market!.Stock) Assert.Equal(stock, back.Market.Stock[star]);
        var lot = back.Hold.Single();
        Assert.Equal(s.Hold.Single().Cost, lot.Cost, 9);
        Assert.Equal("food", lot.Mission.Cargo.Ware);
        Assert.False(lot.IsJob);
    }
}

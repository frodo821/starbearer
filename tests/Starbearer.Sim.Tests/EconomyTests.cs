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
                var kind = g.KindOf(star);
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
        var star = g.Economies.Keys.First(s => g.KindOf(s) == kind);
        return (g, Market.Open(g), star, g.Economies[star]);
    }

    [Fact]
    public void ALineRunsOnlyAsFarAsItsInputsAndRoomAllow()
    {
        var (g, m, star, e) = StationOf(StationKind.Factory);
        // Cut it off from its neighbours: an economy of one.
        var alone = new Market { Stock = { [star] = (double[])m.Stock[star].Clone() } };
        var solo = new Galaxy { Seed = g.Seed, Stars = g.Stars, Nebulae = g.Nebulae, Ports = new[] { star }, Kinds = g.Kinds, StationStars = g.StationStars };
        double S(string w) => alone.StockOf(solo, star, w);
        foreach (var w in e.Wares) alone.Take(solo, star, w, 1e9);
        alone.Put(solo, star, "fission_fuel", 10);
        const double k = Economy.Scale;
        alone.Put(solo, star, "silicon_carbide", 0.7 * k / 2);   // half a day of it
        alone.Put(solo, star, "titanium", 100);
        alone.AdvanceTo(solo, 1);
        Assert.Equal(0, S("silicon_carbide"), 9);
        Assert.Equal(1.0 * k / 2, S("electronics"), 9);         // half a day's make
        Assert.Equal(100 - 0.4 * k / 2, S("titanium"), 9);
        // Without power, nothing runs.
        alone.Take(solo, star, "fission_fuel", 1e9);
        alone.Put(solo, star, "silicon_carbide", 100);
        alone.AdvanceTo(solo, 2);
        Assert.Equal(1.0 * k / 2, S("electronics"), 9);
        // Full of output, the line stops.
        alone.Put(solo, star, "fission_fuel", 10);
        alone.Put(solo, star, "electronics", 1e3);
        var before = S("silicon_carbide");
        alone.AdvanceTo(solo, 3);
        Assert.Equal(before, S("silicon_carbide"), 9);
    }

    [Fact]
    public void AFullByproductDoesNotStopALine()
    {
        var (g, m, star, e) = StationOf(StationKind.ChemPlant);
        var alone = new Market { Stock = { [star] = (double[])m.Stock[star].Clone() } };
        var solo = new Galaxy { Seed = g.Seed, Stars = g.Stars, Nebulae = g.Nebulae, Ports = new[] { star }, Kinds = g.Kinds, StationStars = g.StationStars };
        double S(string w) => alone.StockOf(solo, star, w);
        double Full(string w) => e.Target[e.IndexOf(w)] * Economy.Capacity;
        foreach (var w in e.Wares) alone.Take(solo, star, w, 1e9);
        alone.Put(solo, star, Wares.ShipFuel, 100);
        alone.Put(solo, star, "water", 100);
        alone.Put(solo, star, "hydrogen", Full("hydrogen"));   // no room for the hydrogen water splits off
        alone.AdvanceTo(solo, 1);
        const double k = Economy.Scale;
        Assert.Equal(2.0 * k, S("oxygen"), 9);                  // the oxygen comes all the same
        Assert.Equal(Full("hydrogen"), S("hydrogen"), 9);       // and the hydrogen is let go
        Assert.Equal(100 - 1.5 * k, S("water"), 9);
    }

    [Fact]
    public void NeighboursEvenOutWhatTheyBothHold()
    {
        var g = Galaxy.Generate(5);
        var (a, b, _, _, _) = g.Neighbours.First(n => g.StarAt(n.A).Region == g.StarAt(n.B).Region);
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
            // Fortresses burn it for war, and a fallen station holds nothing: the rest keep enough.
            var open = g.Economies.Keys.Where(s => g.Alive(s) && g.KindOf(s) != StationKind.Fortress).ToList();
            var dry = open.Count(s => once.Fill(g, s, Wares.ShipFuel) < 0.1);
            Assert.True(dry < open.Count / 5, $"{dry} stations out of fuel");
        }
    }
}

public partial class SimTests
{
    /// <summary>A new galaxy docked at a station that makes this ware, with credits to spend.</summary>
    static (SaveData S, Galaxy G, Market M) DockedWhereMade(string ware, double credits = 5000)
    {
        var (s, g) = SaveData.NewGalaxy(5);
        s.PortStation = g.Economies.First(e => e.Value.Makes(ware)).Key;
        s.Credits = credits;
        return (s, g, s.MarketIn(g));
    }

    [Fact]
    public void BuyingTakesStockAndRaisesThePriceUnitByUnit()
    {
        var (s, g, m) = DockedWhereMade("food");
        var star = s.PortStation;
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
        Assert.True(Trade.Sells(g, s.PortStation, "steel"));
        Assert.True(Trade.Sells(g, s.PortStation, Wares.ShipFuel));   // fuel everywhere
        Assert.False(Trade.Buys(g, s.PortStation, "steel"));
        Assert.Equal(0, Trade.MaxBuy(s, g, m, "food"));             // not made here
        // The courier's bays take containers only: steel is bulk.
        Assert.Equal(0, Trade.MaxBuy(s, g, m, "steel"));
        s.Fleet.Add(OwnedShip.Build(Ships.Bulker, 2, s.PortStation));
        Hangar.Board(s, 2);
        var most = Trade.MaxBuy(s, g, m, "steel");
        Assert.True(most > 0 && most <= m.StockOf(g, s.PortStation, "steel"));
        Assert.True(Trade.Buy(s, g, m, "steel", most));
        Assert.Equal(0, Trade.MaxBuy(s, g, m, "steel"));               // the bays, the stock or the credits ran out
        Assert.False(Trade.Buy(s, g, m, "steel", 1));
        // Sold where it is used, at that station's price.
        var lot = s.Hold.Single();
        Assert.Null(Trade.Offer(g, m, s.PortStation, lot));
        var user = g.Economies.First(e => e.Value.Uses("steel")).Key;
        s.PortStation = user;   // as if flown there (test only)
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

public partial class SimTests
{
    [Fact]
    public void NewsTakesLongerToComeFromFurtherAway()
    {
        var g = Galaxy.Generate(5);
        var stations = g.EconomyOrder;
        var a = stations[0];
        Assert.Equal(0, g.InfoLag(a, a));
        foreach (var b in stations)
        {
            Assert.Equal(g.InfoLag(a, b), g.InfoLag(b, a), 9);
            Assert.True(double.IsFinite(g.InfoLag(a, b)));   // gates link every region
        }
        var near = stations.Where(b => b != a && g.StarAt(b).Region == g.StarAt(a).Region).Min(b => g.InfoLag(a, b));
        var far = stations.Where(b => g.StarAt(b).Region != g.StarAt(a).Region).Max(b => g.InfoLag(a, b));
        Assert.True(far > near * 2);
    }

    [Fact]
    public void AFarStationIsKnownAsItWasWhenItsNewsSetOut()
    {
        var g = Galaxy.Generate(5);
        var m = Market.Open(g);
        var here = g.EconomyOrder[0];
        var there = g.EconomyOrder.Where(s => s != here).MaxBy(s => g.InfoLag(here, s) < 60 ? g.InfoLag(here, s) : 0);
        var lag = g.InfoLag(here, there);
        var ware = g.Economies[there].Wares[0];
        var then = m.Copy();
        m.AdvanceTo(g, 200);
        var (fill, day) = m.Known(g, here, there, ware)!.Value;
        Assert.InRange(day, 200 - lag - Market.SnapshotDays, 200 - lag);
        then.AdvanceTo(g, day);
        Assert.Equal(then.Fill(g, there, ware)!.Value, fill, 1);           // a byte's worth of rounding
        var mine = g.Economies[here].Wares[0];
        Assert.Equal((m.Fill(g, here, mine)!.Value, 200.0), m.Known(g, here, here, mine));   // here is seen as it stands
        // Old news is let go of, so the save stays small.
        m.AdvanceTo(g, 800);
        Assert.True(m.History.Count <= Market.HistoryDays / Market.SnapshotDays + 2);
        Assert.True(new SaveData { Market = m }.ToJson().Length < 200_000);
    }
}

public partial class SimTests
{
    [Fact]
    public void AStationsBoardCanBeReadFromItsNews()
    {
        var g = Galaxy.Generate(5);
        var m = Market.Open(g);
        var here = g.EconomyOrder[0];
        var there = g.EconomyOrder.First(s => s != here && g.KindOf(s) != StationKind.Gate && g.InfoLag(here, s) is > 5 and < 60);
        var then = m.Copy();
        m.AdvanceTo(g, 150);
        Assert.Same(m, m.KnownAs(g, here, here));
        var known = m.KnownAs(g, here, there)!;
        Assert.Equal(m.Known(g, here, there, g.Economies[there].Wares[0])!.Value.Day, known.Day);
        then.AdvanceTo(g, known.Day);
        foreach (var (star, e) in g.Economies.Where(x => then.Stock.ContainsKey(x.Key)))   // not those built after the news left
            for (int i = 0; i < e.Wares.Length; i++)
                Assert.InRange(known.Stock[star][i] - then.Stock[star][i], -e.Target[i] * 0.01, e.Target[i] * 0.01);
        Assert.NotEmpty(Missions.Board(g, known, there, 0, Array.Empty<Consignment>()));
    }
}

public partial class SimTests
{
    [Fact]
    public void SellingInPartKeepsTheRestOfTheLotAtItsShareOfTheCost()
    {
        var (s, g, m) = DockedWhereMade("food");
        Trade.Buy(s, g, m, "food", 6);
        var lot = s.Hold.Single();
        var cost = lot.Cost;
        s.PortStation = g.Economies.First(e => e.Value.Uses("food")).Key;   // as if flown there (test only)
        var quote = Trade.SaleQuote(s, g, m, "food", 4)!.Value;
        var stock = m.StockOf(g, s.PortStation, "food");
        var credits = s.Credits;
        Assert.Equal(stock, m.StockOf(g, s.PortStation, "food"));            // a quote moves nothing
        Assert.Equal(quote, Trade.SellUnits(s, g, m, "food", 4)!.Value, 9);
        Assert.Equal(credits + quote, s.Credits, 9);
        Assert.Equal(stock + 4, m.StockOf(g, s.PortStation, "food"), 9);
        var rest = s.Hold.Single();
        Assert.Equal(2, rest.Mission.Cargo.Volume);
        Assert.Equal(cost / 3, rest.Cost, 9);
        Assert.Equal(2, Trade.Held(s, "food"));
        Assert.Null(Trade.SellUnits(s, g, m, "food", 3));                 // more than aboard
    }
}

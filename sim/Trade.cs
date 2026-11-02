namespace Starbearer.Sim;

/// <summary>
/// Buying and selling goods at the station the flown ship is docked at. A station sells what it makes
/// (and fuel, which every station keeps for ships) and buys what it uses. Each unit moves the stock and
/// so the price, so a big trade gets dearer, or cheaper, as it goes.
/// </summary>
public static class Trade
{
    /// <summary>What a station adds on a sale and takes off a purchase.</summary>
    public const double Spread = 0.05;

    public static bool Sells(Galaxy g, int star, string ware) =>
        g.Economies.TryGetValue(star, out var e) && (e.Makes(ware) || ware == Wares.ShipFuel);

    public static bool Buys(Galaxy g, int star, string ware) =>
        g.Economies.TryGetValue(star, out var e) && e.Uses(ware);

    /// <summary>
    /// The price of units bought (buying) or sold here, unit by unit as the stock moves, without
    /// touching the market. Fractions are allowed: fuel is bought by the mass the tank takes.
    /// </summary>
    public static double Cost(Galaxy g, Market m, int star, string ware, double units, bool buying)
    {
        var e = g.Economies[star];
        var i = e.IndexOf(ware);
        var w = Wares.Get(ware);
        var stock = m.Stock[star][i];
        double total = 0;
        for (var left = units; left > 1e-12;)
        {
            var step = Math.Min(1, left);
            var mid = buying ? stock - step / 2 : stock + step / 2;
            total += step * Economy.Price(w, mid / e.Target[i]);
            stock += buying ? -step : step;
            left -= step;
        }
        return total * (buying ? 1 + Spread : 1 - Spread);
    }

    /// <summary>The most whole units of a ware the flown ship can buy here: stock, credits and room in the bays all count.</summary>
    public static int MaxBuy(SaveData s, Galaxy g, Market m, string ware)
    {
        var star = s.PortStar;
        if (!Sells(g, star, ware)) return 0;
        var bays = CargoBays.Of(s);
        var w = Wares.Get(ware);
        var n = 0;
        while (n < m.StockOf(g, star, ware) - 1 + 1e-9
               && bays.Fits(s.Hold, w.Load(n + 1))
               && Cost(g, m, star, ware, n + 1, true) <= s.Credits + 1e-9)
            n++;
        return n;
    }

    /// <summary>Buys whole units into the hold, joining a lot of the same ware already aboard and unspoilt.</summary>
    public static bool Buy(SaveData s, Galaxy g, Market m, string ware, int units)
    {
        if (units <= 0 || units > MaxBuy(s, g, m, ware)) return false;
        var star = s.PortStar;
        var cost = Cost(g, m, star, ware, units, true);
        s.Credits -= cost;
        m.Take(g, star, ware, units);
        var w = Wares.Get(ware);
        var same = s.Hold.FirstOrDefault(c => !c.IsJob && c.Mission.Cargo.Ware == ware && c.Integrity >= 1);
        if (same != null) s.Hold.Remove(same);
        var volume = units + (same?.Mission.Cargo.Volume ?? 0);
        s.Hold.Add(new Consignment
        {
            Mission = new Mission($"lot-{ware}-{star}-{m.Day:F0}-{s.Hold.Count}", star, -1, w.Load(volume), 0),
            Cost = cost + (same?.Cost ?? 0),
        });
        return true;
    }

    /// <summary>What a lot would fetch here now, spoilage taken off; null where the station doesn't buy it.</summary>
    public static double? Offer(Galaxy g, Market m, int star, Consignment lot) =>
        lot.IsJob || lot.Mission.Cargo.Ware is not { } ware || !Buys(g, star, ware) ? null
        : Cost(g, m, star, ware, lot.Mission.Cargo.Volume, false) * lot.Integrity;

    /// <summary>Sells a whole lot here. Returns what it fetched, or null if the station doesn't buy it.</summary>
    public static double? Sell(SaveData s, Galaxy g, Market m, Consignment lot)
    {
        if (Offer(g, m, s.PortStar, lot) is not { } value) return null;
        m.Put(g, s.PortStar, lot.Mission.Cargo.Ware!, lot.Mission.Cargo.Volume);
        s.Credits += value;
        s.Hold.Remove(lot);
        return value;
    }

    /// <summary>Units of ship fuel for a mass of it in the tank.</summary>
    public static double FuelUnits(double mass) => mass / Wares.Get(Wares.ShipFuel).Mass;

    /// <summary>What filling the tank here would cost, and how much of it the stock covers (as tank mass).</summary>
    public static (double Mass, double Cost) FuelQuote(SaveData s, Galaxy g, Market m)
    {
        var room = Math.Max(0, s.Ship.Type.Tank - s.HeldFuel);
        var stock = m.StockOf(g, s.PortStar, Wares.ShipFuel);
        var units = Math.Min(FuelUnits(room), stock);
        return (units * Wares.Get(Wares.ShipFuel).Mass, units > 0 ? Cost(g, m, s.PortStar, Wares.ShipFuel, units, true) : 0);
    }

    /// <summary>Fills the tank as far as stock and credits go. The fuel comes out of the station's stock.</summary>
    public static void BuyFuel(SaveData s, Galaxy g, Market m)
    {
        var (mass, cost) = FuelQuote(s, g, m);
        if (mass <= 0) return;
        var units = FuelUnits(mass);
        if (cost > s.Credits)
        {
            // Short of credits: as many units as they pay for.
            var lo = 0.0; var hi = units;
            for (int i = 0; i < 40; i++) { var mid = (lo + hi) / 2; if (Cost(g, m, s.PortStar, Wares.ShipFuel, mid, true) <= s.Credits) lo = mid; else hi = mid; }
            units = lo;
            cost = Cost(g, m, s.PortStar, Wares.ShipFuel, units, true);
        }
        s.Credits = Math.Max(0, s.Credits - cost);
        m.Take(g, s.PortStar, Wares.ShipFuel, units);
        s.HeldFuel += units * Wares.Get(Wares.ShipFuel).Mass;
    }

    /// <summary>What a unit of tank mass costs here now, for the bankruptcy test and the readouts.</summary>
    public static double FuelPricePerMass(Galaxy g, Market m, int star) =>
        m.Price(g, star, Wares.ShipFuel) is { } p ? p * (1 + Spread) / Wares.Get(Wares.ShipFuel).Mass : Tuning.FuelPrice;
}

using System.Text.Json.Serialization;

namespace Starbearer.Sim;

/// <summary>
/// The stock every station holds, and the galaxy day it stands at. It moves on in whole days, when the
/// game asks for a later day: lines run, upkeep burns, neighbours even out. Saved with the checkpoint.
/// </summary>
public sealed class Market
{
    public double Day { get; set; }
    /// <summary>Per station (by star), stock in the order of its StationEconomy.Wares.</summary>
    public Dictionary<int, double[]> Stock { get; set; } = new();

    /// <summary>Opening stock: around the target, a little either way by the seed.</summary>
    public static Market Open(Galaxy g)
    {
        var r = new Rng(g.Seed ^ 0x3A2C7E);
        var m = new Market();
        foreach (var (star, e) in g.Economies)
            m.Stock[star] = e.Target.Select(t => t * r.Range(0.6, 1.3)).ToArray();
        return m;
    }

    public double StockOf(Galaxy g, int star, string ware) =>
        g.Economies.TryGetValue(star, out var e) && e.IndexOf(ware) is var i and >= 0 ? Stock[star][i] : 0;

    /// <summary>Stock over target, or null where the station doesn't deal in it.</summary>
    public double? Fill(Galaxy g, int star, string ware) =>
        g.Economies.TryGetValue(star, out var e) && e.IndexOf(ware) is var i and >= 0 ? Stock[star][i] / e.Target[i] : null;

    /// <summary>What a unit goes for here now, or null where the station doesn't deal in it.</summary>
    public double? Price(Galaxy g, int star, string ware) => Fill(g, star, ware) is { } f ? Economy.Price(Sim.Wares.Get(ware), f) : null;

    /// <summary>Takes stock out (a sale to the ship, a job's cargo loaded). Returns what there was to take.</summary>
    public double Take(Galaxy g, int star, string ware, double units)
    {
        var i = g.Economies[star].IndexOf(ware);
        if (i < 0) return 0;
        var got = Math.Min(units, Stock[star][i]);
        Stock[star][i] -= got;
        return got;
    }

    /// <summary>Puts stock in (a sale from the ship, a delivery). A station only takes wares it deals in.</summary>
    public bool Put(Galaxy g, int star, string ware, double units)
    {
        var i = g.Economies[star].IndexOf(ware);
        if (i < 0) return false;
        Stock[star][i] += units;
        return true;
    }

    /// <summary>Runs the economy on to the given galaxy day, a day at a time.</summary>
    public void AdvanceTo(Galaxy g, double day)
    {
        while (Day + 1 <= day)
        {
            Step(g);
            Day += 1;
        }
    }

    void Step(Galaxy g)
    {
        foreach (var (star, e) in g.Economies) Produce(e, Stock[star]);
        foreach (var (a, b, closeness) in g.Neighbours) Even(g.Economies[a], Stock[a], g.Economies[b], Stock[b], closeness);
    }

    /// <summary>
    /// One day of work: upkeep first, and the share of it that could be paid runs every line, each as far
    /// as its scarcest input and its room for output allow.
    /// </summary>
    static void Produce(StationEconomy e, double[] stock)
    {
        var power = 1.0;
        foreach (var (w, q) in e.Upkeep)
        {
            var i = e.IndexOf(w);
            power = Math.Min(power, q > 0 ? Math.Min(1, stock[i] / q) : 1);
        }
        foreach (var (w, q) in e.Upkeep) { var i = e.IndexOf(w); stock[i] = Math.Max(0, stock[i] - q * power); }
        foreach (var line in e.Lines)
        {
            var rate = power;
            foreach (var (w, q) in line.In) rate = Math.Min(rate, stock[e.IndexOf(w)] / q);
            foreach (var (w, q) in line.Out)
            {
                var i = e.IndexOf(w);
                rate = Math.Min(rate, Math.Max(0, (e.Target[i] * Economy.Capacity - stock[i]) / q));
            }
            if (rate <= 0) continue;
            foreach (var (w, q) in line.In) { var i = e.IndexOf(w); stock[i] = Math.Max(0, stock[i] - q * rate); }
            foreach (var (w, q) in line.Out) stock[e.IndexOf(w)] += q * rate;
        }
    }

    /// <summary>Wares both stations hold flow from the fuller to the emptier, by how close they are.</summary>
    static void Even(StationEconomy ea, double[] sa, StationEconomy eb, double[] sb, double closeness)
    {
        for (int i = 0; i < ea.Wares.Length; i++)
        {
            var j = eb.IndexOf(ea.Wares[i]);
            if (j < 0) continue;
            var (fa, fb) = (sa[i] / ea.Target[i], sb[j] / eb.Target[j]);
            var move = Economy.Spread * closeness * (fa - fb) * Math.Min(ea.Target[i], eb.Target[j]) / 2;
            move = move > 0 ? Math.Min(move, sa[i]) : Math.Max(move, -sb[j]);
            sa[i] -= move; sb[j] += move;
        }
    }

    public Market Copy() => new() { Day = Day, Stock = Stock.ToDictionary(k => k.Key, k => (double[])k.Value.Clone()) };
}

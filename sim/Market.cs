using System.Text.Json.Serialization;

namespace Starbearer.Sim;

/// <summary>
/// The stock every station holds, and the galaxy day it stands at. It moves on in whole days, when the
/// game asks for a later day: lines run, upkeep burns, neighbours even out. Saved with the checkpoint.
/// </summary>
public sealed class Market
{
    public double Day { get; set; }
    /// <summary>The war, which runs day by day with the market. The galaxy reads it from here (see AdvanceTo).</summary>
    public WarState War { get; set; } = new();
    /// <summary>Per station (by star), stock in the order of its StationEconomy.Wares.</summary>
    public Dictionary<int, double[]> Stock { get; set; } = new();
    /// <summary>How full every station was, every few days back, for news that is still on its way.</summary>
    public List<MarketSnapshot> History { get; set; } = new();

    /// <summary>Snapshots are taken this often, and kept this far back: older news than that is no news.</summary>
    public const double SnapshotDays = 5, HistoryDays = 90;

    /// <summary>
    /// The market at the galaxy's day 0: stock seeded around the target, then run on from well before
    /// the start, so it opens settled into its shortages and with news already travelling.
    /// </summary>
    public static Market Open(Galaxy g)
    {
        var r = new Rng(g.Seed ^ 0x3A2C7E);
        var m = new Market { Day = -HistoryDays, War = WarState.Start(g) };
        g.War = m.War;
        foreach (var (star, e) in g.Economies)
            m.Stock[star] = e.Target.Select(t => t * r.Range(0.6, 1.3)).ToArray();
        m.AdvanceTo(g, 0);
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
        g.War = War;
        if (History.Count == 0) Record(g);
        while (Day + 1 <= day)
        {
            Step(g);
            Day += 1;
            if (Day % SnapshotDays == 0) Record(g);
        }
    }

    /// <summary>Keeps how full every ware is everywhere, a byte each, and lets go of what no news is that old.</summary>
    void Record(Galaxy g)
    {
        var bytes = new List<byte>();
        foreach (var star in g.EconomyOrder)
        {
            var e = g.Economies[star];
            for (int i = 0; i < e.Wares.Length; i++)
                bytes.Add((byte)Math.Round(Math.Clamp(Stock[star][i] / e.Target[i] / Economy.Capacity, 0, 1) * 255));
        }
        History.RemoveAll(h => h.Day == Day || h.Day < Day - HistoryDays - SnapshotDays);
        History.Add(new MarketSnapshot { Day = Day, Fills = Convert.ToBase64String(bytes.ToArray()) });
    }

    /// <summary>
    /// The whole market as the news about one station that has reached another tells it: every
    /// station's stock from the snapshot of that day (to a byte's rounding), and the day itself. Here
    /// itself is the market as it stands. Null if no news that old is kept.
    /// </summary>
    public Market? KnownAs(Galaxy g, int here, int star)
    {
        if (star == here) return this;
        var asOf = Day - g.InfoLag(here, star);
        var snap = History.Where(h => h.Day <= asOf + 1e-9).MaxBy(h => h.Day);
        if (snap == null) return null;
        var bytes = Convert.FromBase64String(snap.Fills);
        var known = new Market { Day = snap.Day };
        var k = 0;
        foreach (var s in g.EconomyOrder)
        {
            var e = g.Economies[s];
            known.Stock[s] = e.Target.Select(t => bytes[k++] / 255.0 * Economy.Capacity * t).ToArray();
        }
        return known;
    }

    /// <summary>
    /// What a pilot at one station knows of a ware at another: its fill as the news that has reached
    /// here tells it, and the day that news is from. Here itself is known as it stands. Null where the
    /// station doesn't deal in the ware or no news that old is kept.
    /// </summary>
    public (double Fill, double Day)? Known(Galaxy g, int here, int star, string ware)
    {
        if (!g.Economies.TryGetValue(star, out var e)) return null;
        var i = e.IndexOf(ware);
        if (i < 0) return null;
        if (star == here) return (Stock[star][i] / e.Target[i], Day);
        var asOf = Day - g.InfoLag(here, star);
        var snap = History.Where(h => h.Day <= asOf + 1e-9).MaxBy(h => h.Day);
        if (snap == null) return null;
        var offset = 0;
        foreach (var s in g.EconomyOrder) { if (s == star) break; offset += g.Economies[s].Wares.Length; }
        var b = Convert.FromBase64String(snap.Fills)[offset + i];
        return (b / 255.0 * Economy.Capacity, snap.Day);
    }

    /// <summary>One day: production (a fortress's upkeep is the war's), trade between neighbours, then the war.</summary>
    void Step(Galaxy g)
    {
        foreach (var (star, e) in g.Economies)
            if (g.Alive(star) && g.KindOf(star) != StationKind.Fortress) Produce(e, Stock[star]);
        foreach (var (a, b, closeness, gate, direct) in g.Neighbours)
            if (g.Alive(a) && g.Alive(b)) Even(g.Economies[a], Stock[a], g.Economies[b], Stock[b], gate >= 0 && !Gates.Open(g, this, gate) ? direct : closeness);
        Sim.War.Step(g, this);
        Industry.Step(g, this);
        Gates.Watch(g, this);
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

    public Market Copy() => new() { Day = Day, War = War.Copy(), Stock = Stock.ToDictionary(k => k.Key, k => (double[])k.Value.Clone()), History = History.ToList() };
}

/// <summary>Every station's fill on one day, a byte per ware in Galaxy.EconomyOrder, base64 for a small save.</summary>
public sealed class MarketSnapshot
{
    public double Day { get; set; }
    public string Fills { get; set; } = "";
}

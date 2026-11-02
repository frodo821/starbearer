namespace Starbearer.Sim;

/// <summary>One production line: what it takes in and puts out per day, at full rate.</summary>
public sealed record Line((string Ware, double Rate)[] In, (string Ware, double Rate)[] Out);

/// <summary>
/// What a station makes and uses, from its kind and the seed: its lines, the upkeep that keeps them
/// running, and the wares it keeps in stock. Derived, so it never needs saving.
/// </summary>
public sealed class StationEconomy
{
    public required int Station { get; init; }
    public required Line[] Lines { get; init; }
    /// <summary>Burnt every day for power; without it, nothing runs.</summary>
    public required (string Ware, double Rate)[] Upkeep { get; init; }
    /// <summary>Every ware held here, in a fixed order the stock arrays follow.</summary>
    public required string[] Wares { get; init; }
    /// <summary>The stock aimed for, per ware: a month or so of flow. Price is 1x base here.</summary>
    public required double[] Target { get; init; }

    public int IndexOf(string ware) => Array.IndexOf(Wares, ware);
    public bool Makes(string ware) => Lines.Any(l => l.Out.Any(o => o.Ware == ware));
    public bool Uses(string ware) => Lines.Any(l => l.In.Any(i => i.Ware == ware)) || Upkeep.Any(u => u.Ware == ware);
}

/// <summary>
/// The economy, X4-style: each station keeps stock per ware against a target, and price follows how
/// full that stock is. Lines turn inputs into outputs while there is input and room; stations next to
/// each other even out what they hold, a stand-in for the traders the galaxy doesn't fly yet.
/// </summary>
public static class Economy
{
    /// <summary>Days of flow a station aims to hold.</summary>
    public const double TargetDays = 25;
    /// <summary>Stock beyond this many times the target has no room: lines making it stop.</summary>
    public const double Capacity = 2;
    /// <summary>Every station keeps fuel for ships calling, even one that makes and burns none.</summary>
    public const double ShipFuelStock = 30;
    /// <summary>Share of the gap in fill two neighbours close each day, at no distance.</summary>
    public const double Spread = 0.3;
    /// <summary>Distance over which that falls to 1/e.</summary>
    public const double SpreadLy = 12;
    /// <summary>How fast news of stock and prices travels along those links, and the day a gate pair adds.</summary>
    public const double NewsLyPerDay = 1.5, GateRelayDays = 1;

    static (string, double) R(string ware, double rate) => (ware, rate);
    static Line Make(params (string, double)[] outs) => new(Array.Empty<(string, double)>(), outs);
    static Line From((string, double)[] ins, params (string, double)[] outs) => new(ins, outs);
    static Line Use(params (string, double)[] ins) => new(ins, Array.Empty<(string, double)>());

    /// <summary>
    /// The lines a kind of station can run: those it always runs, and options it runs a seeded few of
    /// (mines and harvesters work what their ground holds).
    /// </summary>
    static (Line[] Always, Line[] Options, int Picks) Plan(StationKind kind) => kind switch
    {
        StationKind.Mine => (Array.Empty<Line>(), new[] { Make(R("iron_ore", 2)), Make(R("titanium_ore", 1)), Make(R("silica", 1.5)), Make(R("uranium_ore", 0.4)) }, 2),
        StationKind.IceMine => (new[] { Make(R("water", 6)), Make(R("organics", 1.2)) }, Array.Empty<Line>(), 0),
        StationKind.GasHarvester => (new[] { Make(R("deuterium", 0.6)) }, new[] { Make(R("hydrogen", 2)), Make(R("methane", 1.5)), Make(R("ammonia", 1)) }, 2),
        StationKind.Refinery => (new[]
        {
            From(new[] { R("iron_ore", 2), R("hydrogen", 0.6) }, R("steel", 2)),   // reduced with hydrogen
            From(new[] { R("titanium_ore", 1) }, R("titanium", 0.6)),
            From(new[] { R("silica", 1), R("methane", 0.4) }, R("silicon_carbide", 0.6)),
        }, Array.Empty<Line>(), 0),
        StationKind.ChemPlant => (new[]
        {
            From(new[] { R("water", 1.5) }, R("oxygen", 2), R("hydrogen", 0.4)),
            From(new[] { R("ammonia", 1) }, R("hydrazine", 0.8)),
        }, Array.Empty<Line>(), 0),
        StationKind.FuelPlant => (new[]
        {
            From(new[] { R("deuterium", 1) }, R("fusion_fuel", 2.5)),
            From(new[] { R("uranium_ore", 0.5) }, R("fission_fuel", 0.25)),
        }, Array.Empty<Line>(), 0),
        StationKind.Farm => (new[] { From(new[] { R("water", 1), R("organics", 0.6), R("ammonia", 0.3) }, R("food", 2)) }, Array.Empty<Line>(), 0),
        StationKind.Clinic => (new[] { From(new[] { R("organics", 0.5), R("water", 0.3) }, R("medical", 0.6)) }, Array.Empty<Line>(), 0),
        StationKind.Factory => (new[] { From(new[] { R("silicon_carbide", 0.6), R("titanium", 0.35) }, R("electronics", 0.6)) }, Array.Empty<Line>(), 0),
        StationKind.City => (new[] { Use(R("food", 1.2), R("water", 0.6), R("oxygen", 0.6), R("medical", 0.12), R("electronics", 0.08)) }, Array.Empty<Line>(), 0),
        StationKind.Colony => (new[] { Use(R("food", 0.6), R("water", 0.5), R("oxygen", 0.5), R("medical", 0.08), R("steel", 0.2)) }, Array.Empty<Line>(), 0),
        StationKind.Shipyard => (new[] { Use(R("steel", 0.4), R("titanium", 0.2), R("electronics", 0.08), R("silicon_carbide", 0.1), R("hydrazine", 0.15)) }, Array.Empty<Line>(), 0),
        _ => (Array.Empty<Line>(), Array.Empty<Line>(), 0),
    };

    static Line[] LinesFor(StationKind kind, Rng r)
    {
        var (always, options, picks) = Plan(kind);
        return always.Concat(options.OrderBy(_ => r.Next()).Take(picks)).ToArray();
    }

    /// <summary>
    /// What a kind of station makes and uses per day on average, before the seed picks its options, upkeep
    /// included. Generation uses it to put down the kinds a region is short of.
    /// </summary>
    public static (Dictionary<string, double> Makes, Dictionary<string, double> Uses) Nominal(StationKind kind)
    {
        var (always, options, picks) = Plan(kind);
        var makes = new Dictionary<string, double>(); var uses = new Dictionary<string, double>();
        void Add(Line l, double share)
        {
            foreach (var (w, q) in l.Out) makes[w] = makes.GetValueOrDefault(w) + q * share;
            foreach (var (w, q) in l.In) uses[w] = uses.GetValueOrDefault(w) + q * share;
        }
        foreach (var l in always) Add(l, 1);
        foreach (var l in options) Add(l, (double)picks / options.Length);
        foreach (var (w, q) in UpkeepFor(kind)) uses[w] = uses.GetValueOrDefault(w) + q;
        return (makes, uses);
    }

    /// <summary>Power: refineries and factories run on fission, fuel plants on their own make, the rest on fusion fuel.</summary>
    static (string, double)[] UpkeepFor(StationKind kind) => kind switch
    {
        StationKind.FuelPlant => Array.Empty<(string, double)>(),
        StationKind.Refinery or StationKind.Factory => new[] { R("fission_fuel", 0.05) },
        StationKind.Gate => new[] { R("fusion_fuel", 0.15) },
        StationKind.City or StationKind.Colony or StationKind.Shipyard => new[] { R("fusion_fuel", 0.1) },
        _ => new[] { R("fusion_fuel", 0.05) },
    };

    public static StationEconomy Of(Galaxy g, int station)
    {
        var kind = g.KindOf(station);
        var lines = LinesFor(kind, new Rng(g.Seed * 40503 + g.SeedKey(station)));
        var upkeep = UpkeepFor(kind);
        var flow = new Dictionary<string, double>();
        void Add(string w, double rate) => flow[w] = flow.GetValueOrDefault(w) + rate;
        foreach (var l in lines) { foreach (var (w, q) in l.In) Add(w, q); foreach (var (w, q) in l.Out) Add(w, q); }
        foreach (var (w, q) in upkeep) Add(w, q);
        Add(Wares.ShipFuel, 0);
        var wares = flow.Keys.OrderBy(Sim.Wares.IndexOf).ToArray();
        var target = wares.Select(w => Math.Max(flow[w] * TargetDays, w == Sim.Wares.ShipFuel ? ShipFuelStock : 0)).ToArray();
        return new StationEconomy { Station = station, Lines = lines, Upkeep = upkeep, Wares = wares, Target = target };
    }

    /// <summary>
    /// Price from fill (stock over target): 1x base at the target, rising toward 1.8x as stock runs
    /// out and falling toward 0.55x at full.
    /// </summary>
    public static double Price(Ware w, double fill) => w.Price * Math.Exp(-0.6 * (Math.Clamp(fill, 0, Capacity) - 1));
}

namespace Starbearer.Sim;

/// <summary>
/// The factions build up their economy where it falls short, X4-style. A ware whose users across a
/// faction have stayed short for long enough gets a new station that makes it, on one of the faction's
/// sites (placed at generation), in the region that is shortest of it; when what that station would
/// need is short too, the station making that comes first. Building takes materials: steel the
/// faction's shipyards gather, as they do for fortresses. A faction builds now and then, never past
/// its sites, so regions keep their gaps long enough for ships to fill them.
///
/// Where a ware is stranded instead, plenty where it is made and short where it is wanted, more makers
/// would not help: the faction builds a gate, on the pair of its gate sites that brings the most of
/// those makers and users closest. A gate is dear (titanium and electronics, gathered by the shipyards
/// over months) and costs its upkeep for good.
/// </summary>
public static class Industry
{
    /// <summary>A ware counts as short where its users hold less than this share of their target, on average.</summary>
    public const double ShortFill = 0.35;
    /// <summary>Days a ware must stay short before a faction builds for it.</summary>
    public const double ShortDays = 60;
    /// <summary>The least time between two stations a faction builds.</summary>
    public const double BuildGapDays = 45;
    /// <summary>Steel a station takes, and what each shipyard puts toward it a day, as far as its stock goes.</summary>
    public const double SteelPerStation = 25 * Economy.Scale, YardSteel = 0.1 * Economy.Scale;

    /// <summary>Makers count as having plenty above this fill, on average.</summary>
    public const double PlentyFill = 1.2;
    /// <summary>What a gate takes, and what each shipyard puts toward it a day, as far as its stock goes.</summary>
    public const double TitaniumPerGate = 60 * Economy.Scale, ElectronicsPerGate = 20 * Economy.Scale,
        YardTitanium = 0.05 * Economy.Scale, YardElectronics = 0.02 * Economy.Scale;
    /// <summary>A gate is built only where it brings stranded goods at least this much closer, summed over pairs.</summary>
    public const double MinGateGain = 0.02;

    static readonly StationKind[] Economic = Enumerable.Range(0, (int)StationKind.Depot).Select(i => (StationKind)i).ToArray();

    /// <summary>The kind of station that makes most of a ware, or null where none of the economy's does (war goods).</summary>
    public static StationKind? MakerOf(string ware)
    {
        var best = Economic.Select(k => (k, Rate: Economy.Nominal(k).Makes.GetValueOrDefault(ware))).MaxBy(x => x.Rate);
        return best.Rate > 0 ? best.k : null;
    }

    public static void Step(Galaxy g, Market m)
    {
        var w = m.War;
        if (g.Factions.Length == 0 || m.Day < 0) return;
        if (w.Industry.Length < g.Factions.Length) w.Industry = new double[g.Factions.Length];
        while (w.Shortage.Count < g.Factions.Length) w.Shortage.Add(new());
        if (w.LastBuilt.Length < g.Factions.Length) w.LastBuilt = Enumerable.Repeat(double.NegativeInfinity, g.Factions.Length).ToArray();
        while (w.Stranded.Count < g.Factions.Length) w.Stranded.Add(new());
        if (w.GateProgress.Length < g.Factions.Length) w.GateProgress = new double[g.Factions.Length];
        foreach (var f in g.Factions)
        {
            Gather(g, m, f.Id);
            Track(g, m, f.Id);
            if (w.Industry[f.Id] >= 1 && m.Day - w.LastBuilt[f.Id] >= BuildGapDays) Build(g, m, f.Id);
            GatherForGate(g, m, f.Id);
            if (w.GateProgress[f.Id] >= 1) BuildGate(g, m, f.Id);
        }
    }

    /// <summary>The faction's shipyards set titanium and electronics aside for a gate, while some ware has been stranded a while.</summary>
    static void GatherForGate(Galaxy g, Market m, int faction)
    {
        var w = m.War;
        if (w.GateProgress[faction] >= 1 || !w.Stranded[faction].Values.Any(d => d >= ShortDays / 2)) return;
        foreach (var yard in g.Ports.Where(p => g.KindOf(p) == StationKind.Shipyard && g.Alive(p) && g.OwnerOf(p) == faction))
        {
            var e = g.Economies[yard];
            var (ti, el) = (e.IndexOf("titanium"), e.IndexOf("electronics"));
            var tiUsed = Math.Min(YardTitanium, m.Stock[yard][ti]);
            var elUsed = Math.Min(YardElectronics, m.Stock[yard][el]);
            m.Stock[yard][ti] -= tiUsed;
            m.Stock[yard][el] -= elUsed;
            w.GateProgress[faction] += 0.5 * tiUsed / TitaniumPerGate + 0.5 * elUsed / ElectronicsPerGate;
        }
    }

    /// <summary>
    /// How much closer a gate pair would bring a ware's makers with plenty to its users short of it,
    /// summed over every such pair: the gain over how close they are by the routes running now.
    /// </summary>
    static double Gain(Galaxy g, Market m, int faction, string ware, int x, int y, Dictionary<(int, int), double> now)
    {
        double Apart(int a, int b) => (g.StarAt(a).Position - g.StarAt(b).Position).Length;
        var makers = g.Economies.Where(e => g.Alive(e.Key) && g.OwnerOf(e.Key) == faction && e.Value.Makes(ware) && m.Fill(g, e.Key, ware) >= 1).Select(e => e.Key).ToList();
        var users = g.Economies.Where(e => g.Alive(e.Key) && g.OwnerOf(e.Key) == faction && Wants(g, m, e.Key, e.Value, ware) && m.Fill(g, e.Key, ware) < ShortFill).Select(e => e.Key).ToList();
        double gain = 0;
        foreach (var s in makers)
            foreach (var t in users)
            {
                var ly = Math.Min(Apart(s, x) + Apart(y, t), Apart(s, y) + Apart(x, t)) + Tuning.GateTradeLy;
                var via = Math.Exp(-ly / Economy.SpreadLy);
                gain += Math.Max(0, via - now.GetValueOrDefault((Math.Min(s, t), Math.Max(s, t))));
            }
        return gain;
    }

    /// <summary>Builds the faction's gate site pair that most helps its longest-stranded ware, if any helps enough.</summary>
    public static void BuildGate(Galaxy g, Market m, int faction)
    {
        var w = m.War;
        var stranded = w.Stranded[faction].Where(x => x.Value >= ShortDays).OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key).ToList();
        if (stranded.Count == 0) return;
        var sites = g.Gates.Where(x => x < g.GateLinks[x] && g.OwnerOf(x) == faction && !g.Alive(x) && !w.Destroyed.Contains(x)).ToList();
        if (sites.Count == 0) return;
        var now = g.Neighbours.ToDictionary(n => (n.A, n.B), n => n.Gate < 0 || Gates.Open(g, m, n.Gate) ? n.Closeness : n.FallbackGate < 0 || Gates.Open(g, m, n.FallbackGate) ? n.Fallback : n.Direct);
        foreach (var ware in stranded)
        {
            var (site, gain) = sites.Select(x => (x, Gain(g, m, faction, ware, x, g.GateLinks[x], now))).MaxBy(p => p.Item2);
            if (gain < MinGateGain) continue;
            var exit = g.GateLinks[site];
            foreach (var end in new[] { site, exit })
            {
                m.Stock[end] = g.Economies[end].Target.ToArray();   // it opens supplied
                w.Down.Remove(end);
                w.Built[end] = m.Day;
            }
            w.GateProgress[faction] -= 1;
            w.Stranded[faction].Remove(ware);
            w.Log.Add(new WarEvent(m.Day, "gate built", site, g.StarOf(site), faction));
            g.GateBuilt();
            return;
        }
    }

    static void Gather(Galaxy g, Market m, int faction)
    {
        var w = m.War;
        if (w.Industry[faction] >= 1) return;
        foreach (var yard in g.Ports.Where(p => g.KindOf(p) == StationKind.Shipyard && g.Alive(p) && g.OwnerOf(p) == faction))
        {
            var i = g.Economies[yard].IndexOf("steel");
            var used = Math.Min(YardSteel, m.Stock[yard][i]);
            m.Stock[yard][i] -= used;
            w.Industry[faction] += used / SteelPerStation;
        }
    }

    /// <summary>
    /// Whether a station wants more of a ware: it burns it for upkeep, or a line using it makes
    /// something it holds short of its target. A plant whose output is stocked up doesn't need more input.
    /// </summary>
    static bool Wants(Galaxy g, Market m, int station, StationEconomy e, string ware) =>
        e.Upkeep.Any(u => u.Ware == ware)
        || e.Lines.Any(l => l.In.Any(i => i.Ware == ware) && (l.Out.Length == 0 || l.Out.Any(o => m.Fill(g, station, o.Ware) < 1)));

    /// <summary>The average fill of a ware over a faction's stations that want it, in a region or everywhere; null where none does.</summary>
    static double? FillOf(Galaxy g, Market m, int faction, string ware, int region = -1)
    {
        var fills = g.Economies.Where(x => g.Alive(x.Key) && g.OwnerOf(x.Key) == faction && Wants(g, m, x.Key, x.Value, ware)
                                           && (region < 0 || g.StarAt(x.Key).Region == region))
            .Select(x => Math.Min(Economy.Capacity, m.Fill(g, x.Key, ware)!.Value)).ToList();
        return fills.Count == 0 ? null : fills.Average();
    }

    /// <summary>
    /// Counts up the days each ware has been short across the faction, and stranded (short where it is
    /// wanted while its makers have plenty); plenty wears either count down faster.
    /// </summary>
    static void Track(Galaxy g, Market m, int faction)
    {
        var shortage = m.War.Shortage[faction];
        var stranded = m.War.Stranded[faction];
        foreach (var ware in Wares.All.Select(x => x.Id))
        {
            if (MakerOf(ware) == null) continue;
            var fill = FillOf(g, m, faction, ware);
            if (fill == null) continue;
            var made = g.Economies.Where(x => g.Alive(x.Key) && g.OwnerOf(x.Key) == faction && x.Value.Makes(ware)).Select(x => m.Fill(g, x.Key, ware)!.Value).ToList();
            var plenty = made.Count > 0 && made.Average() >= PlentyFill;
            // Stranded rather than short: more makers wouldn't help, only a way between them would.
            var isStranded = plenty && fill < ShortFill;
            stranded[ware] = isStranded ? stranded.GetValueOrDefault(ware) + 1 : Math.Max(0, stranded.GetValueOrDefault(ware) - 2);
            if (stranded[ware] == 0) stranded.Remove(ware);
            shortage[ware] = fill < ShortFill && !isStranded ? shortage.GetValueOrDefault(ware) + 1 : Math.Max(0, shortage.GetValueOrDefault(ware) - 2);
            if (shortage[ware] == 0) shortage.Remove(ware);
        }
    }

    /// <summary>The faction's free sites of a kind, where it could build one.</summary>
    static IEnumerable<int> Sites(Galaxy g, Market m, int faction, StationKind kind) =>
        g.Ports.Where(p => g.KindOf(p) == kind && g.OwnerOf(p) == faction && !g.Alive(p) && !m.War.Destroyed.Contains(p));

    static void Build(Galaxy g, Market m, int faction)
    {
        var w = m.War;
        var shortage = w.Shortage[faction];
        foreach (var (ware, _) in shortage.Where(x => x.Value >= ShortDays).OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToList())
        {
            var kind = MakerOf(ware)!.Value;
            // What it would run on, if that is short too and can be built, comes first.
            var upstream = Economy.Nominal(kind).Uses.Keys
                .Where(i => shortage.GetValueOrDefault(i) >= ShortDays / 2 && MakerOf(i) is { } k && Sites(g, m, faction, k).Any())
                .OrderByDescending(i => shortage[i]).FirstOrDefault();
            var target = upstream ?? ware;
            var builds = MakerOf(target)!.Value;
            // In the region that is shortest of it.
            var site = Sites(g, m, faction, builds).OrderBy(s => FillOf(g, m, faction, target, g.StarAt(s).Region) ?? 1).ThenBy(s => s).FirstOrDefault(-1);
            if (site < 0) continue;
            var e = g.Economies[site];
            m.Stock[site] = e.Target.Select(t => t * 0.5).ToArray();
            w.Down.Remove(site);
            w.Built[site] = m.Day;
            w.Industry[faction] -= 1;
            w.LastBuilt[faction] = m.Day;
            shortage.Remove(target);
            w.Log.Add(new WarEvent(m.Day, "built", site, g.StarOf(site), faction));
            return;
        }
    }
}

namespace Starbearer.Sim;

/// <summary>
/// The factions build up their economy where it falls short, X4-style. A ware whose users across a
/// faction have stayed short for long enough gets a new station that makes it, on one of the faction's
/// regions, in the one that is shortest of it, near the stations there that want it; when what that
/// station would need is short too, the station making that comes first. Building takes materials: steel the
/// faction's shipyards gather, as they do for fortresses. A faction builds now and then, so regions
/// keep their gaps long enough for ships to fill them.
///
/// Where a ware is stranded instead, plenty where it is made and short where it is wanted, more makers
/// would not help: the faction builds a gate, one end near the makers, the other near the users. A gate is dear (titanium and electronics, gathered by the shipyards
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

    /// <summary>The kind of station that makes most of a ware, or null where none of the economy's does (war goods).</summary>
    public static StationKind? MakerOf(string ware)
    {
        var best = Economy.EconomicKinds.Select(k => (k, Rate: Economy.Nominal(k).Makes.GetValueOrDefault(ware))).MaxBy(x => x.Rate);
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
        foreach (var yard in g.Shipyards(faction))
        {
            var titanium = m.Take(g, yard, "titanium", YardTitanium);
            var electronics = m.Take(g, yard, "electronics", YardElectronics);
            w.GateProgress[faction] += 0.5 * titanium / TitaniumPerGate + 0.5 * electronics / ElectronicsPerGate;
        }
    }

    /// <summary>
    /// How much closer a gate between two stars would bring a ware's makers with plenty to its users
    /// short of it, summed over every such pair: the gain over how close they are by the routes running now.
    /// </summary>
    static double Gain(Galaxy g, Market m, int faction, string ware, int x, int y, Dictionary<(int, int), double> now)
    {
        double Apart(int a, int star) => (g.StarAt(a).Position - g.Stars[star].Position).Length;
        var (makers, users) = Ends(g, m, faction, ware);
        double gain = 0;
        foreach (var s in makers)
            foreach (var t in users)
            {
                var ly = Math.Min(Apart(s, x) + Apart(t, y), Apart(s, y) + Apart(t, x)) + Tuning.GateTradeLy;
                gain += Math.Max(0, Math.Exp(-ly / Economy.SpreadLy) - now.GetValueOrDefault((Math.Min(s, t), Math.Max(s, t))));
            }
        return gain;
    }

    /// <summary>A ware's makers across a faction that have plenty, and the stations short of it that want it.</summary>
    static (List<int> Makers, List<int> Users) Ends(Galaxy g, Market m, int faction, string ware) =>
        (g.Economies.Where(e => g.Alive(e.Key) && g.OwnerOf(e.Key) == faction && e.Value.Makes(ware) && m.Fill(g, e.Key, ware) >= 1).Select(e => e.Key).ToList(),
         g.Economies.Where(e => g.Alive(e.Key) && g.OwnerOf(e.Key) == faction && Wants(g, m, e.Key, e.Value, ware) && m.Fill(g, e.Key, ware) < ShortFill).Select(e => e.Key).ToList());

    static Vec3d Middle(Galaxy g, IEnumerable<int> stations) =>
        stations.Select(x => g.StarAt(x).Position).Aggregate((Sum: Vec3d.Zero, N: 0), (a, p) => (a.Sum + p, a.N + 1)) is var (sum, n) && n > 0 ? sum * (1.0 / n) : Vec3d.Zero;

    /// <summary>
    /// Where a faction puts a new station of a kind near a point: a star of one of its regions that it
    /// holds alone (or nobody does), whose system suits the kind, an empty one before one with stations;
    /// -1 if none. Gates go only where no gate stands.
    /// </summary>
    public static int Place(Galaxy g, int faction, StationKind kind, Vec3d near, int avoid = -1)
    {
        return g.Stars.Where(st => st.Id != avoid && g.RegionOwners.Length > st.Region && g.RegionOwners[st.Region] == faction
                                   && g.Claim(st.Id) is var (holder, contested) && !contested && (holder == faction || holder < 0)
                                   && (kind != StationKind.Gate || g.GateIn(st.Id) < 0) && Galaxy.Fits(kind, g.System(st.Id)))
            .OrderBy(st => (st.Position - near).Length + (g.HasStation(st.Id) ? Tuning.CrowdedLy : 0)).ThenBy(st => st.Id)
            .Select(st => st.Id).DefaultIfEmpty(-1).First();
    }

    /// <summary>
    /// Builds a gate for the faction's longest-stranded ware it can help: one end near where it is made
    /// in plenty, the other near where it is wanted, if they are far enough apart and the gate brings
    /// them enough closer.
    /// </summary>
    public static void BuildGate(Galaxy g, Market m, int faction)
    {
        var w = m.War;
        var stranded = w.Stranded[faction].Where(x => x.Value >= ShortDays).OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key).ToList();
        if (stranded.Count == 0) return;
        var now = g.Neighbours.ToDictionary(n => (n.A, n.B), n => n.Gate < 0 || Gates.Open(g, m, n.Gate) ? n.Closeness : n.Direct);
        foreach (var ware in stranded)
        {
            var (makers, users) = Ends(g, m, faction, ware);
            if (makers.Count == 0 || users.Count == 0) continue;
            var x = Place(g, faction, StationKind.Gate, Middle(g, makers));
            if (x < 0) continue;
            var y = Place(g, faction, StationKind.Gate, Middle(g, users), avoid: x);
            if (y < 0 || (g.Stars[x].Position - g.Stars[y].Position).Length < Tuning.GateMinLy) continue;
            if (Gain(g, m, faction, ware, x, y, now) < MinGateGain) continue;
            var id = m.Build(g, x, StationKind.Gate, faction, fill: 1, exitStar: y);   // it opens supplied
            w.GateProgress[faction] -= 1;
            w.Stranded[faction].Remove(ware);
            w.Log.Add(new WarEvent(m.Day, "gate built", id, x, faction));
            return;
        }
    }

    static void Gather(Galaxy g, Market m, int faction)
    {
        var w = m.War;
        if (w.Industry[faction] >= 1) return;
        foreach (var yard in g.Shipyards(faction)) w.Industry[faction] += m.Take(g, yard, "steel", YardSteel) / SteelPerStation;
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

    static void Build(Galaxy g, Market m, int faction)
    {
        var w = m.War;
        var shortage = w.Shortage[faction];
        var regions = Enumerable.Range(0, g.Regions.Length).Where(r => g.RegionOwners[r] == faction).ToList();
        foreach (var (ware, _) in shortage.Where(x => x.Value >= ShortDays).OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToList())
        {
            var kind = MakerOf(ware)!.Value;
            // What it would run on, if that is short too, comes first.
            var upstream = Economy.Nominal(kind).Uses.Keys
                .Where(i => shortage.GetValueOrDefault(i) >= ShortDays / 2 && MakerOf(i) != null)
                .OrderByDescending(i => shortage[i]).FirstOrDefault();
            var target = upstream ?? ware;
            var builds = MakerOf(target)!.Value;
            // In the region shortest of it, near the stations there that want it.
            var region = regions.OrderBy(r => FillOf(g, m, faction, target, r) ?? 1).ThenBy(r => r).First();
            var wanting = g.Economies.Where(x => g.Alive(x.Key) && g.OwnerOf(x.Key) == faction && g.StarAt(x.Key).Region == region && Wants(g, m, x.Key, x.Value, target)).Select(x => x.Key).ToList();
            var star = Place(g, faction, builds, wanting.Count > 0 ? Middle(g, wanting) : g.Regions[region].Center);
            if (star < 0) continue;
            var id = m.Build(g, star, builds, faction);
            w.Industry[faction] -= 1;
            w.LastBuilt[faction] = m.Day;
            shortage.Remove(target);
            w.Log.Add(new WarEvent(m.Day, "built", id, star, faction));
            return;
        }
    }
}

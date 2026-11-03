namespace Starbearer.Sim;

/// <summary>How goods and news get between stations: what each makes and uses, who evens out with whom, and how long news takes.</summary>
public sealed partial class Galaxy
{
    Dictionary<int, StationEconomy>? _economies;
    /// <summary>What each station makes and uses, by station id. Gates and depots included: they keep fuel for ships.</summary>
    public IReadOnlyDictionary<int, StationEconomy> Economies => _economies ??=
        Ports.Concat(Depots).Concat(Gates).OrderBy(s => _built.Contains(s) ? s : int.MinValue).ToDictionary(s => s, s => Economy.Of(this, s));

    (int A, int B, double Closeness, int Gate, double Direct)[]? _neighbours;
    /// <summary>
    /// Station pairs whose stock evens out, and how strongly: closer ones more. Only a faction's own
    /// stations trade: within a region, between regions along a gate route (weaker the farther the
    /// stations sit from the gates), and along the supply lines of what a faction keeps supplied from
    /// everywhere. Goods reach the enemy only in ships' holds. Gate is the gate the best route goes
    /// through (-1 for none), and Direct how close they are by no gate, for days that gate stands still.
    /// Worked out over the stations standing, again whenever a station is built or a gate falls.
    /// </summary>
    public (int A, int B, double Closeness, int Gate, double Direct)[] Neighbours => _neighbours ??= Economies.Keys.Where(Alive).OrderBy(s => s).ToArray() is var live
        ? live.SelectMany(a => live.Where(b => b > a && OwnerOf(a) == OwnerOf(b)).Select(b =>
        {
            var direct = Closeness(a, b, null);
            if (GateLinks.TryGetValue(a, out var x) && x == b) return (a, b, 1.0, a, 0.0);
            var (ly, gate) = TradeLy(a, b);
            var via = ly == null ? direct : Closeness(a, b, ly);
            return (a, b, Math.Max(via, direct), via > direct ? gate : -1, direct);
        }).Where(l => l.Item3 > 0)).ToArray() : default!;

    (int A, int B, double Closeness)[]? _links;
    /// <summary>
    /// Every pair news travels between directly: within a region, each pair of linked gates, and a
    /// supply line of a fortress, an armory or a fuel plant, to every station that deals in what it needs.
    /// </summary>
    (int A, int B, double Closeness)[] Links => _links ??= Economies.Keys.Where(Alive).OrderBy(s => s).ToArray() is var live
        ? live.SelectMany(a => live.Where(b => b > a).Select(b => (a, b, Closeness(a, b, null))).Where(l => l.Item3 > 0)).ToArray() : default!;

    /// <summary>
    /// How far goods go between two stations by way of a gate: to it, and on from its exit, with the
    /// passage counted as Tuning.GateTradeLy more, and the gate. Null where no gate route is shorter than the way straight there.
    /// </summary>
    (double? Ly, int Gate) TradeLy(int a, int b)
    {
        double Apart(int x, int y) => (StarAt(x).Position - StarAt(y).Position).Length;
        var direct = Apart(a, b);
        (double? Ly, int Gate) best = (null, -1);
        foreach (var (gate, exit) in GateLinks)
        {
            if (!Alive(gate) || !Alive(exit)) continue;
            var ly = Apart(a, gate) + Apart(exit, b) + Tuning.GateTradeLy;
            if (ly < direct && (best.Ly == null || ly < best.Ly)) best = (ly, gate);
        }
        return best;
    }

    /// <summary>How far a gate reaches: from its system to its exit's.</summary>
    public double GateSpan(int gate) => (StarAt(gate).Position - StarAt(GateLinks[gate]).Position).Length;

    /// <summary>What a faction keeps supplied from all its stations, not just its neighbours: the war, the fuel everything burns, and its gates.</summary>
    static bool SuppliedFar(StationKind kind) => kind is StationKind.Fortress or StationKind.Armory or StationKind.FuelPlant or StationKind.Gate;

    /// <summary>
    /// How strongly two stations even out: by distance within a region, or along a gate route (gateLy)
    /// between any two; a supply line reaches the whole faction, weaker with distance.
    /// </summary>
    double Closeness(int a, int b, double? gateLy)
    {
        if (GateLinks.TryGetValue(a, out var x) && x == b) return 1;
        var ly = (StarAt(a).Position - StarAt(b).Position).Length;
        var near = StarAt(a).Region == StarAt(b).Region ? Math.Exp(-ly / Economy.SpreadLy) : 0;
        if (gateLy is { } via) near = Math.Max(near, Math.Exp(-via / Economy.SpreadLy));
        if (SuppliedFar(Kinds[a]) || SuppliedFar(Kinds[b]))
            return Economies[a].Wares.Any(w => w != Wares.ShipFuel && Economies[b].IndexOf(w) >= 0) || Kinds[a] == Kinds[b]
                ? Math.Max(Tuning.SupplyLine * Math.Exp(-Math.Min(ly, gateLy ?? ly) / Tuning.SupplyLy), near) : 0;
        return near;
    }

    double[,]? _lag;
    int[]? _economyOrder;

    /// <summary>Stations in the order market snapshots keep them: those from the start, then those built after, as built.</summary>
    public int[] EconomyOrder => _economyOrder ??= Economies.Keys.ToArray();

    /// <summary>
    /// Days news takes from one station to another: along the same links stock evens out over, at
    /// Economy.NewsLyPerDay within a region and a day across each linked pair of gates.
    /// </summary>
    public double InfoLag(int from, int to)
    {
        if (from == to) return 0;
        if (_lag == null)
        {
            var order = EconomyOrder;
            var index = order.Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i);
            var n = order.Length;
            var edges = Enumerable.Range(0, n).Select(_ => new List<(int To, double Days)>()).ToArray();
            foreach (var (a, b, _) in Links)
            {
                var days = GateLinks.TryGetValue(a, out var x) && x == b ? Economy.GateRelayDays
                    : (StarAt(a).Position - StarAt(b).Position).Length / Economy.NewsLyPerDay;
                edges[index[a]].Add((index[b], days));
                edges[index[b]].Add((index[a], days));
            }
            var lag = new double[n, n];
            for (int src = 0; src < n; src++)
            {
                var dist = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
                var done = new bool[n];
                dist[src] = 0;
                for (int k = 0; k < n; k++)
                {
                    var u = -1;
                    for (int i = 0; i < n; i++) if (!done[i] && (u < 0 || dist[i] < dist[u])) u = i;
                    if (u < 0 || double.IsPositiveInfinity(dist[u])) break;
                    done[u] = true;
                    foreach (var (v, d) in edges[u]) dist[v] = Math.Min(dist[v], dist[u] + d);
                }
                for (int i = 0; i < n; i++) lag[src, i] = dist[i];
            }
            _lag = lag;
        }
        var idx = EconomyOrder;
        return _lag[Array.IndexOf(idx, from), Array.IndexOf(idx, to)];
    }
}

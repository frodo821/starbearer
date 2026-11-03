namespace Starbearer.Sim;

public sealed record Star(int Id, string Name, Vec3d Position, double Mass, double Temperature, Vec3d ChartOffset)
{
    /// <summary>The region the star belongs to.</summary>
    public int Region { get; init; }
    public double Mu => Mass * Tuning.MuPerMass;
    public double Soi => Tuning.SoiPerSqrtMass * Math.Sqrt(Mass);
    public double Radius => Tuning.StarRadiusPerSqrtMass * Math.Sqrt(Mass);
}

public sealed record Nebula(Vec3d Center, double Radius, double Density);

/// <summary>A stretch of the galaxy, named so places can be told apart. It carries no rules of its own.</summary>
public sealed record Region(int Id, string Name, Vec3d Center);

public sealed class Galaxy
{
    public required ulong Seed { get; init; }
    public required Star[] Stars { get; init; }
    public required Nebula[] Nebulae { get; init; }
    public Region[] Regions { get; init; } = Array.Empty<Region>();
    /// <summary>
    /// Stations have ids of their own, from FirstStationId up, apart from star ids: a system can hold
    /// several. Mistaking one for a star index fails at once, out of the star array's range.
    /// </summary>
    public const int FirstStationId = 1000;
    /// <summary>The star each station is in, by station id.</summary>
    public Dictionary<int, int> StationStars { get; init; } = new();
    /// <summary>Station ids of the ports: everything but depots and gates.</summary>
    public required int[] Ports { get; init; }
    /// <summary>What each station is, by station id.</summary>
    public Dictionary<int, StationKind> Kinds { get; init; } = new();
    /// <summary>Station ids of the small resupply stations. Their boards have only small jobs.</summary>
    public int[] Depots { get; init; } = Array.Empty<int>();
    /// <summary>Station ids of the gates. Gates post no jobs and take no deliveries.</summary>
    public int[] Gates { get; init; } = Array.Empty<int>();
    /// <summary>Where each gate leads: the station id of the gate it is paired with.</summary>
    public Dictionary<int, int> GateLinks { get; init; } = new();

    /// <summary>The powers at war over the galaxy. Empty in a galaxy made by hand, where no one owns anything.</summary>
    public Faction[] Factions { get; set; } = Array.Empty<Faction>();
    /// <summary>The faction each region was given at the start, by region id.</summary>
    public int[] RegionOwners { get; set; } = Array.Empty<int>();
    /// <summary>Owners of stations that don't simply belong to their region's faction, by station id.</summary>
    public Dictionary<int, int> Owners { get; init; } = new();

    /// <summary>The faction that owns a station, for good; -1 for none.</summary>
    public int OwnerOf(int station) =>
        Owners.TryGetValue(station, out var f) ? f : RegionOwners.Length > 0 ? RegionOwners[StarAt(station).Region] : -1;

    /// <summary>
    /// Who holds a system: the faction of its earliest station (stations are numbered as they are built),
    /// and whether another faction has a station there too. -1 where no faction has one.
    /// </summary>
    public (int Holder, bool Contested) Claim(int star)
    {
        var owners = StationsIn(star).Select(OwnerOf).Where(f => f >= 0).ToList();
        return owners.Count == 0 ? (-1, false) : (owners[0], owners.Distinct().Count() > 1);
    }

    public int StarOf(int station) => StationStars[station];
    public StationKind KindOf(int station) => Kinds[station];
    public bool IsStation(int id) => StationStars.ContainsKey(id);
    /// <summary>Every station in a system, in id order.</summary>
    public IEnumerable<int> StationsIn(int star) => (_byStar ??= StationStars.GroupBy(x => x.Value).ToDictionary(k => k.Key, k => k.Select(x => x.Key).OrderBy(x => x).ToArray()))
        .TryGetValue(star, out var ids) ? ids : Array.Empty<int>();
    Dictionary<int, int[]>? _byStar;
    public bool HasStation(int star) => StationsIn(star).Any();

    /// <summary>
    /// What a station's seeded traits (its orbit, its lines, its board) are drawn from: its star for the
    /// first station there, so a system keeps its look as stations are added, and a distinct key for the rest.
    /// </summary>
    public ulong SeedKey(int station)
    {
        var star = StarOf(station);
        var index = Array.IndexOf(StationsIn(star).ToArray(), station);
        return (ulong)star + (ulong)index * 1_000_003;
    }
    /// <summary>A system's gate, or -1.</summary>
    public int GateIn(int star) => StationsIn(star).Where(GateLinks.ContainsKey).DefaultIfEmpty(-1).First();
    public Star StarAt(int station) => Stars[StarOf(station)];
    /// <summary>A system's first station, the one a path is measured against by default; null without one.</summary>
    public Station? MainStation(int star) => StationsIn(star).Select(StationAt).FirstOrDefault();

    /// <summary>The regions a gate in this one leads to.</summary>
    public IEnumerable<int> LinkedRegions(int region) =>
        Gates.Where(g => StarAt(g).Region == region).Select(g => StarAt(GateLinks[g]).Region).Distinct();

    HashSet<int>? _depots;
    public bool IsDepot(int station) => (_depots ??= Depots.ToHashSet()).Contains(station);

    Dictionary<int, StationEconomy>? _economies;
    /// <summary>What each station makes and uses, by station id. Gates and depots included: they keep fuel for ships.</summary>
    public IReadOnlyDictionary<int, StationEconomy> Economies => _economies ??=
        Ports.Concat(Depots).Concat(Gates).ToDictionary(s => s, s => Economy.Of(this, s));

    (int A, int B, double Closeness)[]? _neighbours;
    /// <summary>
    /// Station pairs whose stock evens out, and how strongly: every pair within a region, closer ones more,
    /// and each pair of linked gates. Goods cross between regions only in ships' holds.
    /// </summary>
    public (int A, int B, double Closeness)[] Neighbours => _neighbours ??= Economies.Keys.OrderBy(s => s)
        .SelectMany(a => Economies.Keys.Where(b => b > a && (StarAt(a).Region == StarAt(b).Region || GateLinks.TryGetValue(a, out var x) && x == b))
            .Select(b => (a, b, GateLinks.TryGetValue(a, out var y) && y == b ? 1.0 : Math.Exp(-(StarAt(a).Position - StarAt(b).Position).Length / Economy.SpreadLy))))
        .ToArray();

    double[,]? _lag;
    int[]? _economyOrder;

    /// <summary>Stations in the fixed order market snapshots keep them.</summary>
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
            foreach (var (a, b, _) in Neighbours)
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

    readonly Dictionary<int, Center> _systems = new();
    readonly Dictionary<int, Station> _stations = new();

    /// <summary>A star with its planets and moons.</summary>
    public Center System(int star)
    {
        lock (_systems)
        {
            if (!_systems.TryGetValue(star, out var c)) _systems[star] = c = Bodies.Build(Seed, Stars[star]);
            return c;
        }
    }

    // Only bodies whose sphere leaves room for an orbit clear of the body (see PlaceStation).
    static List<Center> Planets(Center system) => system.Children.Where(p => p.Edge * 0.22 > Tuning.HostClearance).ToList();
    static List<Center> Moons(Center system) => system.Children.SelectMany(p => p.Children).Where(m => m.Edge * 0.25 > Tuning.HostClearance).ToList();

    /// <summary>Whether a system has the body a kind of station works: gas harvesting needs a giant, ice a moon.</summary>
    static bool Fits(StationKind kind, Center system) => kind switch
    {
        StationKind.GasHarvester => Planets(system).Any(p => p.Kind == BodyKind.Giant),
        _ => true,   // ice is cut from a moon where there is one, else from the outer belt
    };

    /// <summary>What a kind of station circles: mines and gates the star (a belt, open space), harvesters their
    /// giant or moon, refineries, factories and shipyards a moon if there is one, the rest a planet.</summary>
    static Center HostFor(StationKind kind, Center system, Rng r)
    {
        var planets = Planets(system);
        var moons = Moons(system);
        var giants = planets.Where(p => p.Kind == BodyKind.Giant).ToList();
        return kind switch
        {
            StationKind.Mine or StationKind.Gate => system,
            StationKind.GasHarvester when giants.Count > 0 => giants[r.Index(giants.Count)],
            StationKind.IceMine or StationKind.Refinery or StationKind.Factory or StationKind.Shipyard when moons.Count > 0 => moons[r.Index(moons.Count)],
            StationKind.IceMine => system,
            _ when planets.Count > 0 => planets[r.Index(planets.Count)],
            _ => system,
        };
    }

    /// <summary>A station's orbit, by station id, derived from the seed so it never needs saving.</summary>
    public Station StationAt(int id)
    {
        lock (_stations)
        {
            if (!_stations.TryGetValue(id, out var st)) _stations[id] = st = PlaceStation(id);
            return st;
        }
    }

    Station PlaceStation(int id)
    {
        var star = StarOf(id);
        var r = new Rng(Seed * 1000003 + SeedKey(id));
        var system = System(star);
        var host = HostFor(KindOf(id), system, r);
        var share = host.Kind == BodyKind.Moon ? r.Range(0.25, 0.4) : r.Range(0.22, Bodies.FreeInnerShare - 0.08);
        var radius = host == system ? Bodies.WidestGap(system) : host.Edge * share;
        var node = r.Range(0, 2 * Math.PI);
        var tilt = r.Range(5, 28) * Math.PI / 180 * (r.Unit() < 0.5 ? -1 : 1);
        var u = new Vec3d(Math.Cos(node), 0, Math.Sin(node));
        var w = new Vec3d(-Math.Sin(node), 0, Math.Cos(node)) * Math.Cos(tilt) + new Vec3d(0, Math.Sin(tilt), 0);
        return new Station(id, star, radius, r.Range(0, 2 * Math.PI), Math.Sqrt(host.Mu / (radius * radius * radius)), u, w) { Host = host };
    }

    static readonly string[] Syllables = "ka ru mi to sa ne ra li on ve th al dor ys ean or tes mar qu ien sol vi ga".Split(' ');

    internal static string MakeName(Rng r)
    {
        var s = "";
        for (int i = 0, n = 2 + r.Index(2); i < n; i++) s += Syllables[r.Index(Syllables.Length)];
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static Galaxy Generate(ulong seed)
    {
        var r = new Rng(seed);
        var stars = new List<Star>();
        var names = new HashSet<string>();
        // Placement checks only nearby cells, so a large galaxy costs no more per star than a small one.
        var cells = new Dictionary<(int, int, int), List<Vec3d>>();
        (int, int, int) Cell(Vec3d p) => ((int)Math.Floor(p.X / Tuning.MinStarSeparation), (int)Math.Floor(p.Y / Tuning.MinStarSeparation), (int)Math.Floor(p.Z / Tuning.MinStarSeparation));
        bool Crowded(Vec3d p)
        {
            var (cx, cy, cz) = Cell(p);
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (cells.TryGetValue((cx + x, cy + y, cz + z), out var list) && list.Any(q => (q - p).Length < Tuning.MinStarSeparation)) return true;
            return false;
        }
        for (int attempts = 0; stars.Count < Tuning.StarCount && attempts < 1000 * Tuning.StarCount; attempts++)
        {
            var p = new Vec3d(r.Range(-1, 1), r.Range(-1, 1), r.Range(-1, 1));
            if (p.LengthSq > 1) continue;
            p = new Vec3d(p.X * Tuning.GalaxyRadius, p.Y * Tuning.GalaxyThickness, p.Z * Tuning.GalaxyRadius);
            if (Crowded(p)) continue;
            var cell = Cell(p);
            if (!cells.TryGetValue(cell, out var here)) cells[cell] = here = new();
            here.Add(p);
            string name;
            do name = MakeName(r); while (!names.Add(name));
            var mass = Math.Pow(r.Range(0.25, 1.25), 2.2) + 0.35;       // mostly small, few heavy
            var temp = 3000 + 4800 * Math.Pow(mass, 0.8);
            stars.Add(new Star(stars.Count, name, p, mass, temp, r.NormalVec() * Tuning.ChartErrorLy));
        }

        var nebulae = new Nebula[Tuning.NebulaCount];
        for (int i = 0; i < nebulae.Length; i++)
        {
            var c = new Vec3d(r.Range(-1, 1) * Tuning.GalaxyRadius, r.Range(-1, 1) * Tuning.GalaxyThickness, r.Range(-1, 1) * Tuning.GalaxyRadius);
            nebulae[i] = new Nebula(c, r.Range(5, 10), r.Range(0.8, 2.5));
        }

        var regions = Partition(r, stars, names);
        stars = stars.Select(s => s with { Region = regions.Member[s.Id] }).ToList();

        var kinds = new Dictionary<int, StationKind>();
        var links = PlaceGates(r, stars, regions.List, kinds);
        var systems = new Dictionary<int, Center>();
        Center SystemOf(int star) => systems.TryGetValue(star, out var c) ? c : systems[star] = Bodies.Build(seed, stars[star]);
        for (int k = 0; k < regions.List.Length; k++)
            PlaceStations(r, stars, stars.Where(s => s.Region == k).ToList(), kinds, SystemOf);
        SpreadEveryKind(r, kinds, SystemOf);
        // Placement worked by star; each station now gets its own id, in placement order (each region's guaranteed ones first).
        var id = kinds.Keys.Select((star, i) => (star, i)).ToDictionary(x => x.star, x => FirstStationId + x.i);
        var byId = kinds.ToDictionary(k => id[k.Key], k => k.Value);
        var stations = byId.Keys.ToList();
        var galaxy = new Galaxy
        {
            Seed = seed, Stars = stars.ToArray(), Nebulae = nebulae, Regions = regions.List, Kinds = byId,
            StationStars = kinds.Keys.ToDictionary(star => id[star], star => star),
            Ports = stations.Where(s => byId[s] is not (StationKind.Depot or StationKind.Gate)).ToArray(),
            Depots = stations.Where(s => byId[s] == StationKind.Depot).ToArray(),
            Gates = stations.Where(s => byId[s] == StationKind.Gate).ToArray(),
            GateLinks = links.ToDictionary(l => id[l.Key], l => id[l.Value]),
        };
        foreach (var (star, c) in systems) galaxy._systems[star] = c;   // built already, to fit the stations
        (galaxy.Factions, galaxy.RegionOwners) = Sim.Factions.Divide(r, galaxy, names, Tuning.FactionCount);
        return galaxy;
    }

    /// <summary>
    /// Gate pairs between regions: a spanning tree over the region centres so every region can be
    /// reached, each region's nearest neighbour, and one long link across. Gates sit near their
    /// region's middle, so a passage saves a long run.
    /// </summary>
    static Dictionary<int, int> PlaceGates(Rng r, List<Star> stars, Region[] regions, Dictionary<int, StationKind> kinds)
    {
        var links = new Dictionary<int, int>();
        if (regions.Length < 2) return links;
        double Apart(int a, int b) => (regions[a].Center - regions[b].Center).Length;
        var pairs = new HashSet<(int, int)>();
        void Pair(int a, int b) { if (a != b) pairs.Add((Math.Min(a, b), Math.Max(a, b))); }
        var joined = new List<int> { 0 };
        while (joined.Count < regions.Length)
        {
            var (from, to) = joined.SelectMany(a => Enumerable.Range(0, regions.Length).Where(b => !joined.Contains(b)).Select(b => (a, b))).MinBy(x => Apart(x.a, x.b));
            Pair(from, to);
            joined.Add(to);
        }
        for (int a = 0; a < regions.Length; a++) Pair(a, Enumerable.Range(0, regions.Length).Where(b => b != a).MinBy(b => Apart(a, b)));
        if (regions.Length >= 4)
        {
            var (x, y) = Enumerable.Range(0, regions.Length).SelectMany(a => Enumerable.Range(a + 1, regions.Length - a - 1).Select(b => (a, b))).MaxBy(p => Apart(p.a, p.b));
            Pair(x, y);
        }
        // Each link gets a star near the middle of each end, leaning toward the other region.
        int GateStar(int region, int toward)
        {
            var mid = regions[region].Center;
            var lean = mid + (regions[toward].Center - mid).Normalized() * 4;
            return stars.Where(s => s.Region == region && !kinds.ContainsKey(s.Id)).MinBy(s => (s.Position - lean).Length)!.Id;
        }
        foreach (var (a, b) in pairs.OrderBy(p => p))
        {
            var ga = GateStar(a, b); kinds[ga] = StationKind.Gate;
            var gb = GateStar(b, a); kinds[gb] = StationKind.Gate;
            links[ga] = gb; links[gb] = ga;
        }
        return links;
    }

    /// <summary>
    /// A region's stations. The guaranteed ones (see Tuning.RegionMinimum) are spread by greedy farthest
    /// point so jobs cross the region, each on a star whose system has the body it works; the rest, a
    /// varying number, go to random stars as kinds their systems suit.
    /// </summary>
    static void PlaceStations(Rng r, List<Star> all, List<Star> mine, Dictionary<int, StationKind> kinds, Func<int, Center> system)
    {
        if (mine.Count == 0) return;
        var economic = Enumerable.Range(0, (int)StationKind.Depot).Select(i => (StationKind)i).ToArray();
        // What the region's stations make and use so far, so the open slots go where it falls short.
        var makes = new Dictionary<string, double>(); var uses = new Dictionary<string, double>();
        void Tally(StationKind kind)
        {
            var (m, u) = Economy.Nominal(kind);
            foreach (var (w, q) in m) makes[w] = makes.GetValueOrDefault(w) + q;
            foreach (var (w, q) in u) uses[w] = uses.GetValueOrDefault(w) + q;
        }
        double Shortfall(StationKind kind) => Economy.Nominal(kind).Makes
            .Sum(x => x.Value * Math.Max(0, uses.GetValueOrDefault(x.Key) - makes.GetValueOrDefault(x.Key)) / Math.Max(uses.GetValueOrDefault(x.Key), 1e-9));
        void Place(int star, StationKind kind) { kinds[star] = kind; Tally(kind); }
        StationKind EconomicFor(int star)
        {
            var fit = economic.Where(k => Fits(k, system(star))).ToArray();
            // Mostly what the region lacks; now and then anything, so no two regions come out alike.
            if (r.Unit() < 0.3) return fit[r.Index(fit.Length)];
            return fit.MaxBy(k => Shortfall(k) + r.Range(0, 0.25));
        }
        foreach (var s in mine.Where(s => kinds.ContainsKey(s.Id))) Tally(kinds[s.Id]);   // the gates
        var guaranteed = Tuning.RegionMinimum.OrderBy(_ => r.Next()).ToList();
        // The gates already here count for the spread, so the rest keep away from them.
        var placed = mine.Where(s => kinds.ContainsKey(s.Id)).Select(s => s.Id).ToList();
        foreach (var kind in guaranteed)
        {
            var free = mine.Where(s => !kinds.ContainsKey(s.Id)).ToList();
            if (free.Count == 0) break;
            var suited = free.Where(s => Fits(kind, system(s.Id))).ToList();
            var pool = suited.Count > 0 ? suited : free;
            var star = placed.Count == 0 ? pool[r.Index(pool.Count)] : pool.MaxBy(s => placed.Min(p => (all[p].Position - s.Position).Length))!;
            Place(star.Id, suited.Count > 0 ? kind : EconomicFor(star.Id));
            placed.Add(star.Id);
        }
        var total = (int)Math.Round(mine.Count * r.Range(Tuning.StationShareMin, Tuning.StationShareMax));
        var rest = mine.Where(s => !kinds.ContainsKey(s.Id)).OrderBy(_ => r.Next()).Take(Math.Max(0, total - placed.Count));
        foreach (var s in rest)
        {
            var roll = r.Unit();
            Place(s.Id, roll < 0.6 ? EconomicFor(s.Id)
                : roll < 0.82 ? StationKind.Depot
                : roll < 0.93 ? StationKind.Colony
                : StationKind.Shipyard);
        }
    }

    /// <summary>
    /// A region has room for only part of the chains, so each economic kind missing from the whole galaxy
    /// takes the place of one that has the most of its kind, keeping each region's guaranteed ones.
    /// Then every ware is made somewhere, and the gaps between regions are what ships carry across.
    /// </summary>
    static void SpreadEveryKind(Rng r, Dictionary<int, StationKind> kinds, Func<int, Center> system)
    {
        var economic = Enumerable.Range(0, (int)StationKind.Depot).Select(i => (StationKind)i).ToArray();
        var must = Tuning.RegionMinimum.ToHashSet();
        foreach (var missing in economic.Where(k => !kinds.ContainsValue(k)).OrderBy(_ => r.Next()).ToList())
        {
            var counts = kinds.Values.GroupBy(k => k).ToDictionary(k => k.Key, k => k.Count());
            var spare = kinds.Where(x => (!must.Contains(x.Value) || counts[x.Value] > 4) && x.Value is not (StationKind.Gate or StationKind.Colony or StationKind.Shipyard)
                    && Fits(missing, system(x.Key)))
                .OrderByDescending(x => x.Value == StationKind.Depot ? 0 : counts[x.Value]).ThenBy(x => x.Key).ToList();
            if (spare.Count == 0) continue;
            kinds[spare[0].Key] = missing;
        }
    }

    /// <summary>
    /// Splits the stars into regions of about the same size: k-means from well spread seeds, so each
    /// region is one connected stretch of the disc and the borders fall between stars.
    /// </summary>
    static (Region[] List, int[] Member) Partition(Rng r, List<Star> stars, HashSet<string> names)
    {
        var k = Math.Min(Tuning.RegionCount, stars.Count);
        var centers = new List<Vec3d> { stars[r.Index(stars.Count)].Position };
        while (centers.Count < k)
            centers.Add(stars.MaxBy(s => centers.Min(c => (c - s.Position).Length))!.Position);
        var member = new int[stars.Count];
        void Assign()
        {
            for (int i = 0; i < stars.Count; i++)
                member[i] = Enumerable.Range(0, k).MinBy(j => (centers[j] - stars[i].Position).Length);
        }
        for (int round = 0; round < 12; round++)
        {
            Assign();
            for (int j = 0; j < k; j++)
            {
                var own = stars.Where(s => member[s.Id] == j).ToList();
                if (own.Count > 0) centers[j] = own.Aggregate(Vec3d.Zero, (a, s) => a + s.Position) * (1.0 / own.Count);
            }
        }
        Assign();   // so every star belongs to the centre nearest it
        var list = new Region[k];
        for (int j = 0; j < k; j++)
        {
            string name;
            do name = MakeName(r); while (!names.Add(name));
            list[j] = new Region(j, name, centers[j]);
        }
        return (list, member);
    }

    public double Density(Vec3d p)
    {
        double d = 1;
        foreach (var n in Nebulae)
            d += n.Density * Math.Exp(-(p - n.Center).LengthSq / (2 * n.Radius * n.Radius));
        return d;
    }
}

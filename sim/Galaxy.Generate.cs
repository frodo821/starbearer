namespace Starbearer.Sim;

/// <summary>Making a galaxy from a seed: stars, regions, gates, stations, factions and the first battlefields.</summary>
public sealed partial class Galaxy
{
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
        CoverEachFaction(galaxy);
        PlaceWar(r, galaxy);
        // Drawn from a stream of their own, so the rest of the galaxy comes out as it did before them.
        var hubs = new Rng(seed ^ 0x5EED_4B1B);
        PlaceHubs(hubs, galaxy);
        PlaceCapitals(hubs, galaxy);
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
        var economic = Economy.EconomicKinds;
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
    /// takes the place of one that has the most of its kind, keeping each region's guaranteed ones and
    /// every fuel plant: a region without one runs dry of what ships burn.
    /// Then every ware is made somewhere, and the gaps between regions are what ships carry across.
    /// </summary>
    static void SpreadEveryKind(Rng r, Dictionary<int, StationKind> kinds, Func<int, Center> system)
    {
        var economic = Economy.EconomicKinds;
        var must = Tuning.RegionMinimum.ToHashSet();
        foreach (var missing in economic.Where(k => !kinds.ContainsValue(k)).OrderBy(_ => r.Next()).ToList())
        {
            var counts = kinds.Values.GroupBy(k => k).ToDictionary(k => k.Key, k => k.Count());
            var spare = kinds.Where(x => (!must.Contains(x.Value) || counts[x.Value] > 4) && x.Value is not (StationKind.Gate or StationKind.Colony or StationKind.Shipyard or StationKind.FuelPlant)
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

    /// <summary>
    /// Goods cross to the enemy only in ships' holds, so each faction makes every ware at home: each
    /// economic kind a faction lacks gets a new station, in its region with the fewest stations, on the
    /// free star there farthest from the rest whose system suits it.
    /// </summary>
    static void CoverEachFaction(Galaxy g)
    {
        var economic = Economy.EconomicKinds;
        foreach (var f in g.Factions)
        {
            var regions = Enumerable.Range(0, g.Regions.Length).Where(r => g.RegionOwners[r] == f.Id).ToList();
            foreach (var missing in economic)
            {
                if (g.Kinds.Any(x => x.Value == missing && g.OwnerOf(x.Key) == f.Id)) continue;
                var placed = g.StationStars.Values.ToHashSet();
                var star = regions.OrderBy(r => g.StationStars.Values.Count(st => g.Stars[st].Region == r)).ThenBy(r => r)
                    .Select(r => g.Stars.Where(st => st.Region == r && !placed.Contains(st.Id) && Fits(missing, g.System(st.Id)))
                        .OrderByDescending(st => placed.Where(p => g.Stars[p].Region == r).Select(p => (g.Stars[p].Position - st.Position).Length).DefaultIfEmpty(0).Min())
                        .ThenBy(st => st.Id).FirstOrDefault())
                    .FirstOrDefault(st => st != null);
                if (star == null) continue;
                g.AddAtStart(star.Id, missing);
            }
        }
    }

    /// <summary>
    /// The war's places, drawn last so the rest of the galaxy stays as it was: an armory in each region
    /// whose faction's weapon is made (a particle beam burns hydrogen as it comes), and the fronts. A
    /// front is a pair of regions of different factions a gate joins; on each side, the system nearest
    /// the other (not a gate's) is a battlefield from the start, with a fortress of each side. Where the
    /// war goes from there is the factions' doing (see War.Build).
    /// </summary>
    static void PlaceWar(Rng r, Galaxy g)
    {
        if (g.Factions.Length < 2) return;
        int Add(int star, StationKind kind, int owner) => g.AddAtStart(star, kind, owner);
        foreach (var region in g.Regions)
        {
            var f = g.RegionOwners[region.Id];
            if (g.Factions[f].Weapon == "hydrogen") continue;
            var star = g.Stars.Where(s => s.Region == region.Id && !g.HasStation(s.Id))
                .OrderBy(s => (s.Position - region.Center).Length).Skip(r.Index(3)).FirstOrDefault();
            if (star != null) Add(star.Id, StationKind.Armory, f);
        }
        var fronts = g.GateLinks.Select(x => (A: g.StarAt(x.Key).Region, B: g.StarAt(x.Value).Region))
            .Where(p => g.RegionOwners[p.A] != g.RegionOwners[p.B]).Select(p => (Math.Min(p.A, p.B), Math.Max(p.A, p.B))).Distinct().OrderBy(p => p).ToList();
        var used = new HashSet<int>();
        foreach (var (a, b) in fronts)
            foreach (var (home, away) in new[] { (a, b), (b, a) })
            {
                var enemy = g.Stars.Where(s => s.Region == away).ToList();
                var near = g.Stars.Where(s => s.Region == home && g.GateIn(s.Id) < 0 && !used.Contains(s.Id))
                    .OrderBy(s => enemy.Min(e => (e.Position - s.Position).Length)).Take(Tuning.Battlefields).ToList();
                foreach (var star in near)
                {
                    used.Add(star.Id);
                    Add(star.Id, StationKind.Fortress, g.RegionOwners[home]);
                    Add(star.Id, StationKind.Fortress, g.RegionOwners[away]);
                }
            }
    }

    /// <summary>
    /// Adds a station while the galaxy is being made, with the next id, owned by the given faction (its
    /// region's by default). Returns its id.
    /// </summary>
    int AddAtStart(int star, StationKind kind, int owner = -1)
    {
        var id = StationStars.Keys.Max() + 1;
        StationStars[id] = star;
        Kinds[id] = kind;
        if (owner >= 0 && RegionOwners.Length > 0 && owner != RegionOwners[Stars[star].Region]) Owners[id] = owner;
        if (kind == StationKind.Depot) { Depots = Depots.Append(id).ToArray(); _depots = null; }
        else if (kind == StationKind.Gate) Gates = Gates.Append(id).ToArray();
        else Ports = Ports.Append(id).ToArray();
        _byStar = null;
        return id;
    }

    /// <summary>
    /// A few systems in each region are hubs, rich in bodies, where several stations work side by side:
    /// one there already, and one to three more of other kinds that deal with it and each other (a
    /// refinery by its mine, a factory by the refinery, a city to feed). Never a gate's system, nor a
    /// battlefield. Ships find short hauls between them, by sublight alone.
    /// </summary>
    static void PlaceHubs(Rng r, Galaxy g)
    {
        foreach (var region in g.Regions)
        {
            var candidates = g.Stars.Where(s => s.Region == region.Id && g.StationsIn(s.Id).Count() == 1 && g.GateIn(s.Id) < 0
                                                && g.StationsIn(s.Id).All(x => g.KindOf(x) is not (StationKind.Fortress or StationKind.Armory)))
                .OrderByDescending(s => g.System(s.Id).Tree.Count() + r.Range(0, 3)).ThenBy(s => s.Id).ToList();
            var hubs = 1 + (r.Unit() < 0.5 ? 1 : 0);
            foreach (var star in candidates.Take(hubs))
            {
                var more = 1 + r.Index(3);
                for (int i = 0; i < more; i++)
                    if (HubKind(r, g, star.Id) is { } kind) g.AddAtStart(star.Id, kind);
            }
        }
    }

    /// <summary>
    /// Each faction's capital: deep in its territory, far from the enemy's regions, in a system rich in
    /// bodies, one with stations already where it can (never a gate's, nor the war's). The capital
    /// system is a hub: with fewer than two other stations there, it gets more that deal with them.
    /// </summary>
    static void PlaceCapitals(Rng r, Galaxy g)
    {
        foreach (var f in g.Factions)
        {
            var enemy = g.Stars.Where(s => g.RegionOwners[s.Region] != f.Id).Select(s => s.Position).ToList();
            var star = g.Stars.Where(s => g.RegionOwners[s.Region] == f.Id && g.GateIn(s.Id) < 0
                                          && g.StationsIn(s.Id).All(x => g.KindOf(x) is not (StationKind.Fortress or StationKind.Armory)))
                .OrderByDescending(s => enemy.Select(e => (e - s.Position).Length).DefaultIfEmpty(0).Min()
                                        + g.System(s.Id).Tree.Count() + (g.HasStation(s.Id) ? 6 : 0) + r.Range(0, 4))
                .ThenBy(s => s.Id).FirstOrDefault();
            if (star == null) continue;
            g.AddAtStart(star.Id, StationKind.Capital, f.Id);
            while (g.StationsIn(star.Id).Count() < 3 && HubKind(r, g, star.Id) is { } kind) g.AddAtStart(star.Id, kind);
        }
    }

    /// <summary>
    /// The kind a hub takes next: one not there yet, that its system suits, dealing most with what is
    /// there (what one makes the other uses), give or take a little luck. Null if none suits.
    /// </summary>
    static StationKind? HubKind(Rng r, Galaxy g, int star)
    {
        var here = g.StationsIn(star).Select(g.KindOf).ToList();
        double Deals(StationKind a, StationKind b)
        {
            var (ma, ua) = Economy.Nominal(a);
            var (mb, ub) = Economy.Nominal(b);
            return ma.Keys.Count(w => w != Wares.ShipFuel && ub.ContainsKey(w)) + mb.Keys.Count(w => w != Wares.ShipFuel && ua.ContainsKey(w));   // all burn fuel
        }
        var pool = Economy.EconomicKinds.Where(k => !here.Contains(k) && Fits(k, g.System(star))).ToList();
        if (pool.Count == 0) return null;
        return pool.MaxBy(k => here.Sum(h => Deals(k, h)) + r.Range(0, 1.5));
    }
}

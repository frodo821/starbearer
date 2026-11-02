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
    /// <summary>Star ids that host a port.</summary>
    public required int[] Ports { get; init; }
    /// <summary>Star ids with a small resupply station. Its board has only small jobs.</summary>
    public int[] Depots { get; init; } = Array.Empty<int>();

    HashSet<int>? _ports, _depots;
    public bool IsPort(int star) => (_ports ??= Ports.ToHashSet()).Contains(star);
    public bool IsDepot(int star) => (_depots ??= Depots.ToHashSet()).Contains(star);

    /// <summary>Each port's station, derived from the seed so it never needs saving.</summary>
    public bool HasStation(int star) => IsPort(star) || IsDepot(star);
    public Station? StationOrNull(int star) => HasStation(star) ? StationAt(star) : null;

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

    /// <summary>What a kind of station circles: mining stations work a belt around the star, cities
    /// circle planets, labs and foundries the moons, anything else the first planet going.</summary>
    Center HostFor(StationKind kind, Center system, Rng r)
    {
        // Only bodies whose sphere leaves room for an orbit clear of the docking range (see PlaceStation).
        var planets = system.Children.Where(p => p.Edge * 0.22 > Tuning.DockRange * 3).ToList();
        var moons = system.Children.SelectMany(p => p.Children).Where(m => m.Edge * 0.25 > Tuning.DockRange * 3).ToList();
        return kind switch
        {
            StationKind.Mine => system,
            StationKind.Lab or StationKind.Foundry when moons.Count > 0 => moons[r.Index(moons.Count)],
            _ when planets.Count > 0 => planets[r.Index(planets.Count)],
            _ => system,
        };
    }

    public Station StationAt(int star)
    {
        lock (_stations)
        {
            if (!_stations.TryGetValue(star, out var st)) _stations[star] = st = PlaceStation(star);
            return st;
        }
    }

    Station PlaceStation(int star)
    {
        var r = new Rng(Seed * 1000003 + (ulong)star);
        var system = System(star);
        var host = HostFor(Missions.KindOf(this, star), system, r);
        var share = host.Kind == BodyKind.Moon ? r.Range(0.25, 0.4) : r.Range(0.22, Bodies.FreeInnerShare - 0.08);
        var radius = host == system ? Bodies.WidestGap(system) : host.Edge * share;
        var node = r.Range(0, 2 * Math.PI);
        var tilt = r.Range(5, 28) * Math.PI / 180 * (r.Unit() < 0.5 ? -1 : 1);
        var u = new Vec3d(Math.Cos(node), 0, Math.Sin(node));
        var w = new Vec3d(-Math.Sin(node), 0, Math.Cos(node)) * Math.Cos(tilt) + new Vec3d(0, Math.Sin(tilt), 0);
        return new Station(star, radius, r.Range(0, 2 * Math.PI), Math.Sqrt(host.Mu / (radius * radius * radius)), u, w) { Host = host };
    }

    static readonly string[] Syllables = "ka ru mi to sa ne ra li on ve th al dor ys ean or tes mar qu ien sol vi ga".Split(' ');

    static string MakeName(Rng r)
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

        // In each region, ports spread by greedy farthest point so contracts cross it, and depots fill the gaps.
        var ports = new List<int>();
        var depots = new List<int>();
        for (int k = 0; k < regions.List.Length; k++)
        {
            var mine = stars.Where(s => s.Region == k).ToList();
            var placed = new List<int> { mine[r.Index(mine.Count)].Id };
            int Farthest() => mine.Where(s => !placed.Contains(s.Id))
                .OrderByDescending(s => placed.Min(p => (stars[p].Position - s.Position).Length)).First().Id;
            while (placed.Count < Math.Min(Tuning.PortsPerRegion, mine.Count)) placed.Add(Farthest());
            ports.AddRange(placed);
            var portCount = placed.Count;
            while (placed.Count < Math.Min(portCount + Tuning.DepotsPerRegion, mine.Count)) placed.Add(Farthest());
            depots.AddRange(placed.Skip(portCount));
        }

        return new Galaxy { Seed = seed, Stars = stars.ToArray(), Nebulae = nebulae, Regions = regions.List, Ports = ports.ToArray(), Depots = depots.ToArray() };
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

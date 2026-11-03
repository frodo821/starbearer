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

/// <summary>
/// The galaxy: its stars and regions, the stations in it and who owns them. Generation is in
/// Galaxy.Generate.cs, and how goods and news get between stations in Galaxy.Routes.cs.
/// </summary>
public sealed partial class Galaxy
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
    public required int[] Ports { get; set; }
    /// <summary>What each station is, by station id.</summary>
    public Dictionary<int, StationKind> Kinds { get; init; } = new();
    /// <summary>Station ids of the small resupply stations. Their boards have only small jobs.</summary>
    public int[] Depots { get; set; } = Array.Empty<int>();
    /// <summary>Station ids of the gates. Gates post no jobs and take no deliveries.</summary>
    public int[] Gates { get; set; } = Array.Empty<int>();
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
        var owners = StationsIn(star).OrderBy(s => War.Built.TryGetValue(s, out var d) ? d : double.NegativeInfinity).ThenBy(s => s)
            .Select(OwnerOf).Where(f => f >= 0).ToList();
        return owners.Count == 0 ? (-1, false) : (owners[0], owners.Distinct().Count() > 1);
    }

    public int StarOf(int station) => StationStars[station];
    public StationKind KindOf(int station) => Kinds[station];
    public bool IsStation(int id) => StationStars.ContainsKey(id);
    /// <summary>The war as it stands: what is down, what is damaged, what has been built. The market keeps it and hands it here (see Bind).</summary>
    public WarState War { get; private set; } = new();
    /// <summary>Stations built during the game, by any market bound so far; the bound one's are those that stand.</summary>
    readonly HashSet<int> _built = new();
    HashSet<int> _inWar = new();
    /// <summary>Whether a station stands: there from the start or built in the war bound now, and not destroyed.</summary>
    public bool Alive(int station) => !War.Down.Contains(station) && (!_built.Contains(station) || _inWar.Contains(station));
    /// <summary>Whether a station was built during the game rather than there from the start.</summary>
    public bool BuiltDuringGame(int station) => _built.Contains(station);
    /// <summary>The first id a station built during the game gets.</summary>
    public int NextStationId => StationStars.Keys.Where(k => !_built.Contains(k)).DefaultIfEmpty(FirstStationId - 1).Max() + 1;

    /// <summary>Takes up a war state: stations it has built that this galaxy hasn't seen yet are added.</summary>
    public void Bind(WarState war)
    {
        War = war;
        var fresh = war.Added.Where(a => !StationStars.ContainsKey(a.Id)).ToList();
        foreach (var a in fresh) Apply(a);
        // Their economies once all are in: a gate's upkeep needs where its exit is.
        if (_economies != null) foreach (var a in fresh) _economies[a.Id] = Economy.Of(this, a.Id);   // appended, so the snapshot order holds
        if (fresh.Count > 0) { _economyOrder = null; RoutesChanged(); }
        foreach (var a in war.Added)
            if (StationStars[a.Id] != a.Star || Kinds[a.Id] != a.Kind) throw new InvalidOperationException($"station {a.Id} differs between wars");
        var inWar = war.Added.Select(a => a.Id).ToHashSet();
        if (!inWar.SetEquals(_inWar)) { _inWar = inWar; RoutesChanged(); }
    }

    /// <summary>Adds a station built during the game, and lets go of what was worked out from the old set.</summary>
    void Apply(AddedStation a)
    {
        StationStars[a.Id] = a.Star;
        Kinds[a.Id] = a.Kind;
        if (RegionOwners.Length > 0 && a.Owner != RegionOwners[Stars[a.Star].Region]) Owners[a.Id] = a.Owner;
        if (a.Link >= 0) { GateLinks[a.Id] = a.Link; GateLinks[a.Link] = a.Id; }
        if (a.Kind == StationKind.Gate) Gates = Gates.Append(a.Id).ToArray();
        else if (a.Kind == StationKind.Depot) { Depots = Depots.Append(a.Id).ToArray(); _depots = null; }
        else Ports = Ports.Append(a.Id).ToArray();
        _built.Add(a.Id);
        _byStar = null;
    }

    /// <summary>Gates went up or down: goods and news find their ways again.</summary>
    public void RoutesChanged() { _neighbours = null; _links = null; _lag = null; }

    /// <summary>Every station standing in a system, in id order.</summary>
    public IEnumerable<int> StationsIn(int star) => AllStationsIn(star).Where(Alive);
    /// <summary>Every station there has been in a system, standing or not, in id order.</summary>
    public int[] AllStationsIn(int star) => (_byStar ??= StationStars.GroupBy(x => x.Value).ToDictionary(k => k.Key, k => k.Select(x => x.Key).OrderBy(x => x).ToArray()))
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
        var index = Array.IndexOf(AllStationsIn(star), station);
        return (ulong)star + (ulong)index * 1_000_003;
    }
    /// <summary>A system's gate, or -1.</summary>
    public int GateIn(int star) => StationsIn(star).Where(GateLinks.ContainsKey).DefaultIfEmpty(-1).First();
    public Star StarAt(int station) => Stars[StarOf(station)];
    /// <summary>A system's first station, the one a path is measured against by default; null without one.</summary>
    public Station? MainStation(int star) => StationsIn(star).Select(StationAt).FirstOrDefault();

    /// <summary>The regions a gate in this one leads to.</summary>
    public IEnumerable<int> LinkedRegions(int region) =>
        Gates.Where(g => Alive(g) && StarAt(g).Region == region).Select(g => StarAt(GateLinks[g]).Region).Distinct();

    /// <summary>The shipyards standing, a faction's or (with none given) everyone's, in station order. They gather what factions build with.</summary>
    public IEnumerable<int> Shipyards(int? faction = null) =>
        Ports.Where(p => Kinds[p] == StationKind.Shipyard && Alive(p) && (faction == null || OwnerOf(p) == faction));

    HashSet<int>? _depots;
    public bool IsDepot(int station) => (_depots ??= Depots.ToHashSet()).Contains(station);

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
    internal static bool Fits(StationKind kind, Center system) => kind switch
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

    public double Density(Vec3d p)
    {
        double d = 1;
        foreach (var n in Nebulae)
            d += n.Density * Math.Exp(-(p - n.Center).LengthSq / (2 * n.Radius * n.Radius));
        return d;
    }
}

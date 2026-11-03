namespace Starbearer.Sim;

/// <summary>
/// What a station does: what it makes and uses, and so which jobs its board offers. The economic kinds
/// come first, up to Depot; the ones after it each have a role of their own. Capital is last so the
/// numbers of the rest stay as saves keep them. Harvesters need the body
/// their work needs: gas a giant, ice a moon; mines work the belt around the star.
/// </summary>
public enum StationKind { Mine, IceMine, GasHarvester, Refinery, ChemPlant, FuelPlant, Farm, Clinic, Factory, City, Depot, Shipyard, Colony, Armory, Fortress, Gate, Capital }

/// <summary>A job from a mission board: carry Cargo to Target for Pay, less whatever the cargo loses.</summary>
public sealed record Mission(string Id, int Origin, int Target, Cargo Cargo, double Pay)
{
    /// <summary>State work from a capital, given only to a company its faction counts a friend.</summary>
    public bool State { get; init; }
}

/// <summary>
/// A load in the hold: a job's cargo, or goods bought to trade (a lot, with no destination: its
/// mission's Target is -1 and what was paid is its Cost).
/// </summary>
public sealed class Consignment
{
    public required Mission Mission { get; init; }
    public double Integrity { get; set; } = 1;
    /// <summary>What was paid for goods bought to trade; zero for a job.</summary>
    public double Cost { get; init; }
    public bool IsJob => Mission.Target >= 0;
    public double Payout => IsJob ? Mission.Pay * Integrity : 0;
    /// <summary>What it is worth, for a gate's toll: a job's pay, goods what they cost, less damage either way.</summary>
    public double Value => IsJob ? Payout : Cost * Integrity;
    public Consignment Copy() => new() { Mission = Mission, Integrity = Integrity, Cost = Cost };
}

public static class Missions
{
    const int PortOffers = 5, SmallOffers = 3;
    /// <summary>Colonies need more than they make: jobs go there more often, and pay more for it.</summary>
    const double ColonyPull = 2, ColonyPay = 1.25;
    /// <summary>A capital draws jobs as a colony does, and pays a little more.</summary>
    const double CapitalPull = 2, CapitalPay = 1.3;
    /// <summary>How many state jobs a capital offers a friend, how much more they pay than a one-off item, and the least distance they go.</summary>
    const int StateOffers = 2;
    const double StatePay = 2, StateMinLy = 15;

    /// <summary>What a capital sends, sealed, to its faction's stations far off.</summary>
    static readonly Cargo[] StateItems =
    {
        new("外交文書", 0.1, 0.7, 180, 1), new("政府の機密資料", 0.2, 0.6, 200, 1), new("勲章と辞令", 0.1, 0.7, 150, 1),
        new("要人の私物", 0.4, 0.55, 190, 2), new("暗号機", 0.5, 0.6, 220, 2), new("戦時国債の証書", 0.1, 0.8, 160, 1),
    };

    static double Pull(StationKind kind) => kind switch { StationKind.Colony => ColonyPull, StationKind.Capital => CapitalPull, _ => 1 };
    static double Demand(StationKind kind) => kind switch { StationKind.Colony => ColonyPay, StationKind.Capital => CapitalPay, _ => 1 };

    /// <summary>One-off things that exist only as jobs: no station makes or stocks them.</summary>
    static readonly Cargo[] Specials =
    {
        new("冷凍種子", 0.3, 0.45, 160, 2), new("観測試料", 0.2, 0.4, 170, 1), new("精密機器", 0.7, 0.7, 180, 2),
        new("美術品", 0.2, 0.5, 200, 1), new("郵便", 0.1, 0.8, 70, 1), new("予備部品", 0.3, 0.85, 60, 1),
        new("入植地の標本", 0.2, 0.5, 150, 1), new("工芸品", 0.2, 0.6, 130, 1), new("帰郷者の荷物", 0.4, 0.75, 90, 2),
        new("航法機器", 0.3, 0.6, 160, 1), new("医療機材", 0.5, 0.75, 120, 2), new("実験装置", 0.5, 0.6, 150, 3),
    };

    public static string Name(StationKind kind) => kind switch
    {
        StationKind.Mine => "小惑星採掘ステーション",
        StationKind.IceMine => "氷採取ステーション",
        StationKind.GasHarvester => "ガス採取ステーション",
        StationKind.Refinery => "精錬所",
        StationKind.ChemPlant => "化学プラント",
        StationKind.FuelPlant => "燃料プラント",
        StationKind.Farm => "農業ステーション",
        StationKind.Clinic => "医療ステーション",
        StationKind.Factory => "工場",
        StationKind.City => "都市ステーション",
        StationKind.Shipyard => "造船所",
        StationKind.Colony => "コロニー",
        StationKind.Armory => "軍需工場",
        StationKind.Fortress => "要塞",
        StationKind.Gate => "ゲート",
        StationKind.Capital => "首都",
        _ => "補給ステーション",
    };

    /// <summary>The station as it is called on the chart.</summary>
    public static string ShortName(StationKind kind) => kind switch
    {
        StationKind.Depot => "補給港",
        StationKind.Shipyard => "造船所",
        StationKind.Colony => "コロニー",
        StationKind.Armory => "軍需工場",
        StationKind.Fortress => "要塞",
        StationKind.Gate => "ゲート",
        StationKind.Capital => "首都",
        _ => "港",
    };

    static string MarkOf(StationKind kind) => kind switch
    {
        StationKind.Depot => "◇",
        StationKind.Shipyard => "▲",
        StationKind.Colony => "●",
        StationKind.Gate => "◎",
        StationKind.Fortress => "■",
        StationKind.Capital => "★",
        _ => "◆",
    };

    /// <summary>The marks after a star's name on the chart, one per station standing there: ◆ port, ◇ depot, ▲ shipyard, ● colony, ■ fortress, ◎ gate, ★ capital; none without a station.</summary>
    public static string Mark(Galaxy g, int star) =>
        g.HasStation(star) ? " " + string.Concat(g.StationsIn(star).Select(id => MarkOf(g.KindOf(id)))) : "";

    /// <summary>A station as the player reads it: its system and what it is, e.g. "Ruka 造船所".</summary>
    public static string Label(Galaxy g, int station) => $"{g.StarAt(station).Name} {KindLabel(g.KindOf(station))}";

    /// <summary>What a station is, told apart from the others in its system: the full name for a plant of the economy, the short one for the rest.</summary>
    public static string KindLabel(StationKind kind) => kind < StationKind.Depot ? Name(kind) : ShortName(kind);

    /// <summary>Pay grows with the straight-line distance. Whether the way there is worth flying is the pilot's call.</summary>
    static double PayFor(Galaxy g, int from, int to, Cargo cargo)
    {
        var ly = (g.StarAt(to).Position - g.StarAt(from).Position).Length;
        return Math.Round(cargo.Reward * Math.Max(0.4, ly / Tuning.StandardHaulLy) * Demand(g.KindOf(to)));
    }

    /// <summary>
    /// What a station offers. First a one-off item, so any ship finds something to carry; then its wares,
    /// each from its spare stock to a station within reach that uses the ware and is short of it, the
    /// widest price gaps first. Pay is that gap, at least a fifth of the ware's price, plus a share for
    /// the distance. What is left over goes to one-off items. Gates post no jobs. A capital adds state
    /// work for a friend of its faction: sealed items to its stations far off, at several times the pay.
    /// </summary>
    /// <param name="firstJob">The company's very first board: its first offer goes to another station of this system, a haul by sublight alone to learn on.</param>
    public static List<Mission> Board(Galaxy g, Market market, int station, int deliveries, IEnumerable<Consignment> hold, bool friend = false, bool firstJob = false)
    {
        var r = new Rng(g.Seed * 7919 + g.SeedKey(station) * 104729 + (ulong)deliveries * 31);
        var kind = g.KindOf(station);
        if (kind is StationKind.Gate or StationKind.Fortress) return new();
        // Within the region, or one a gate from here leads to.
        var region = g.StarAt(station).Region;
        var reach = g.LinkedRegions(region).Append(region).ToHashSet();
        var places = g.Economies.Keys.Where(s => s != station && g.Alive(s) && reach.Contains(g.StarAt(s).Region)).ToArray();
        var stations = places.Where(s => g.KindOf(s) is not (StationKind.Gate or StationKind.Fortress))
            .OrderBy(s => r.Unit() / Pull(g.KindOf(s))).ToArray();
        if (stations.Length == 0) return new();
        var count = kind is StationKind.Depot or StationKind.Shipyard ? SmallOffers : PortOffers;
        var here = g.Economies[station];
        // Candidate hauls: spare stock here to a station short of it.
        var spare = here.Wares.Where(w => here.Makes(w)).ToDictionary(w => w, w => market.StockOf(g, station, w) - here.Target[here.IndexOf(w)] * SpareFloor);
        var hauls = new List<(string Ware, int To, double Gap, double Short)>();
        foreach (var (ware, left) in spare)
        {
            if (left < 1) continue;
            var priceHere = market.Price(g, station, ware)!.Value;
            foreach (var to in stations)   // never to a gate or a fortress: those are kept by their owners, not by hire
                if (g.Economies[to].Uses(ware) && market.Fill(g, to, ware) is { } fill and < ShortFill)
                {
                    var e = g.Economies[to];
                    hauls.Add((ware, to, market.Price(g, to, ware)!.Value - priceHere, e.Target[e.IndexOf(ware)] - market.StockOf(g, to, ware)));
                }
        }
        var offers = new List<Mission>();
        var nextDoor = stations.Where(s => g.StarOf(s) == g.StarOf(station)).ToArray();
        void Special(int i)
        {
            var to = i == 0 && firstJob && nextDoor.Length > 0 ? nextDoor[0] : stations[i % stations.Length];
            var item = Specials[r.Index(Specials.Length)];
            offers.Add(new Mission($"{station}-{deliveries}-{i}", station, to, item, PayFor(g, station, to, item)));
        }
        Special(0);
        foreach (var (ware, to, gap, shortBy) in hauls.OrderByDescending(h => (h.Gap + Wares.Get(h.Ware).Price * 0.1) * (1 + 0.4 * r.Unit())))
        {
            if (offers.Count >= count) break;
            if (offers.Any(o => o.Target == to && o.Cargo.Ware == ware)) continue;
            var units = (int)Math.Floor(Math.Min(Math.Min(spare[ware], shortBy), MaxHaulUnits));
            if (units < 1) continue;
            spare[ware] -= units;
            var w = Wares.Get(ware);
            var ly = (g.StarAt(to).Position - g.StarAt(station).Position).Length;
            var pay = units * (Math.Max(gap, w.Price * GapFloor) + w.Price * DistancePay * Math.Max(0.4, ly / Tuning.StandardHaulLy)) * Demand(g.KindOf(to));
            offers.Add(new Mission($"{station}-{deliveries}-{offers.Count}", station, to, w.Load(units), Math.Round(pay)));
        }
        for (int i = offers.Count; i < count; i++) Special(i);
        if (kind == StationKind.Capital && friend)
        {
            var owner = g.OwnerOf(station);
            var far = g.Economies.Keys.Where(s => g.Alive(s) && g.OwnerOf(s) == owner && g.KindOf(s) is not (StationKind.Gate or StationKind.Fortress or StationKind.Capital)
                                                  && (g.StarAt(s).Position - g.StarAt(station).Position).Length >= StateMinLy)
                .OrderBy(s => r.Unit()).Take(StateOffers).ToList();
            foreach (var to in far)
            {
                var item = StateItems[r.Index(StateItems.Length)];
                offers.Add(new Mission($"{station}-{deliveries}-s{offers.Count}", station, to, item, Math.Round(PayFor(g, station, to, item) * StatePay)) { State = true });
            }
        }
        var taken = hold.Select(c => c.Mission.Id).ToHashSet();
        return offers.Where(m => !taken.Contains(m.Id)).ToList();
    }

    /// <summary>A station keeps this share of its target back from hauls; a station short below this fill asks for more.</summary>
    const double SpareFloor = 0.5, ShortFill = 0.8;
    /// <summary>The most units one haul takes, the least pay per unit as a share of price, and the distance share.</summary>
    const double MaxHaulUnits = 6, GapFloor = 0.2, DistancePay = 0.15;

    /// <summary>
    /// The board as the pilot found it at this dock, kept in the save: taking one job leaves the rest as
    /// they were, and a reload shows the same board. A new dock or a wait posts a new one.
    /// </summary>
    public static List<Mission> OffersFor(SaveData s, Galaxy g)
    {
        var friend = Factions.Toward(s, g, s.PortStation) == Stance.Friendly;
        var key = $"{s.PortStation}:{s.Deliveries}:{s.TotalDays:F4}:{friend}";   // becoming a friend opens a capital's state work
        if (s.OffersAt != key)
        {
            s.Offers = Board(g, s.MarketIn(g), s.PortStation, s.Deliveries, s.Hold, friend, firstJob: s.Deliveries == 0 && s.TotalDays == 0);
            s.OffersAt = key;
        }
        return s.Offers;
    }

    /// <summary>A volume as the player reads it.</summary>
    public static string Cubic(double volume) => $"{volume * Tuning.CubicMetersPerVolume:F0} m³";

    public static double VolumeUsed(IEnumerable<Consignment> hold) => hold.Sum(c => c.Mission.Cargo.Volume);
    public static bool Fits(SaveData s, Mission m) => CargoBays.Of(s).Fits(s.Hold, m.Cargo);

    /// <summary>Giving up a job costs a share of its pay; goods of one's own are just left behind.</summary>
    public static double DropFee(Consignment c) => c.IsJob ? c.Mission.Pay * Tuning.DropPenalty : 0;

    /// <summary>Takes a job: its cargo goes aboard, and a ware's comes out of the station's stock.</summary>
    public static bool Take(SaveData s, Galaxy g, Mission m)
    {
        if (!Fits(s, m)) return false;
        if (m.Cargo.Ware is { } ware)
        {
            var market = s.MarketIn(g);
            if (market.StockOf(g, m.Origin, ware) < m.Cargo.Volume - 1e-9) return false;
            market.Take(g, m.Origin, ware, m.Cargo.Volume);
        }
        s.Hold.Add(new Consignment { Mission = m });
        s.Offers.Remove(m);
        return true;
    }

    /// <summary>
    /// Hands over what is bound here: a ware goes into the station's stock, as much of it as came through,
    /// and the station's owner counts it a service. Returns the change in standing with the owner.
    /// </summary>
    public static double Deliver(SaveData s, Galaxy g, Consignment c)
    {
        if (c.Mission.Cargo.Ware is { } ware) s.MarketIn(g).Put(g, c.Mission.Target, ware, c.Mission.Cargo.Volume * c.Integrity);
        return Factions.Serve(s, g, c.Mission.Target, c.Payout, c.Mission.Cargo.Ware);
    }

    /// <summary>Gives up a job. The fee stops at what the company has, so this alone can't bankrupt it.</summary>
    public static void Drop(SaveData s, Consignment c)
    {
        s.Credits = Math.Max(0, s.Credits - DropFee(c));
        s.Hold.Remove(c);
    }
}

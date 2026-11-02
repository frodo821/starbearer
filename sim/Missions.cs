namespace Starbearer.Sim;

/// <summary>
/// What a station does: what it makes and uses, and so which jobs its board offers. The economic kinds
/// come first, up to Depot; the ones after it each have a role of their own. Harvesters need the body
/// their work needs: gas a giant, ice a moon; mines work the belt around the star.
/// </summary>
public enum StationKind { Mine, IceMine, GasHarvester, Refinery, ChemPlant, FuelPlant, Farm, Clinic, Factory, City, Depot, Shipyard, Colony, Gate }

/// <summary>A job from a mission board: carry Cargo to Target for Pay, less whatever the cargo loses.</summary>
public sealed record Mission(string Id, int Origin, int Target, Cargo Cargo, double Pay);

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

    /// <summary>One-off things that exist only as jobs: no station makes or stocks them.</summary>
    static readonly Cargo[] Specials =
    {
        new("冷凍種子", 0.3, 0.45, 160, 2), new("観測試料", 0.2, 0.4, 170, 1), new("精密機器", 0.7, 0.7, 180, 2),
        new("美術品", 0.2, 0.5, 200, 1), new("郵便", 0.1, 0.8, 70, 1), new("予備部品", 0.3, 0.85, 60, 1),
        new("入植地の標本", 0.2, 0.5, 150, 1), new("工芸品", 0.2, 0.6, 130, 1), new("帰郷者の荷物", 0.4, 0.75, 90, 2),
        new("航法機器", 0.3, 0.6, 160, 1), new("医療機材", 0.5, 0.75, 120, 2), new("実験装置", 0.5, 0.6, 150, 3),
    };

    /// <summary>A ware job's reward per unit, as a share of the ware's base price.</summary>
    const double WareReward = 0.5;

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
        StationKind.Gate => "ゲート",
        _ => "補給ステーション",
    };

    /// <summary>The station as it is called on the chart.</summary>
    public static string ShortName(StationKind kind) => kind switch
    {
        StationKind.Depot => "補給港",
        StationKind.Shipyard => "造船所",
        StationKind.Colony => "コロニー",
        StationKind.Gate => "ゲート",
        _ => "港",
    };

    /// <summary>The mark after a star's name on the chart: ◆ port, ◇ depot, ▲ shipyard, ● colony; none without a station.</summary>
    public static string Mark(Galaxy g, int star) => !g.HasStation(star) ? "" : KindOf(g, star) switch
    {
        StationKind.Depot => " ◇",
        StationKind.Shipyard => " ▲",
        StationKind.Colony => " ●",
        StationKind.Gate => " ◎",
        _ => " ◆",
    };

    /// <summary>The kind comes from the seed, so it never needs saving.</summary>
    public static StationKind KindOf(Galaxy g, int star)
    {
        if (g.Kinds.TryGetValue(star, out var kind)) return kind;
        if (g.IsDepot(star)) return StationKind.Depot;
        return (StationKind)new Rng(g.Seed * 6700417 + (ulong)star).Index((int)StationKind.Depot);
    }

    /// <summary>Pay grows with the straight-line distance. Whether the way there is worth flying is the pilot's call.</summary>
    static double PayFor(Galaxy g, int from, int to, Cargo cargo)
    {
        var ly = (g.Stars[to].Position - g.Stars[from].Position).Length;
        var demand = KindOf(g, to) == StationKind.Colony ? ColonyPay : 1;
        return Math.Round(cargo.Reward * Math.Max(0.4, ly / Tuning.StandardHaulLy) * demand);
    }

    /// <summary>
    /// What a station offers: a one-off item first, so any ship finds something to carry, then lots of what
    /// it makes, each to a station that uses it. It changes with each delivery; jobs in the hold are gone from it.
    /// </summary>
    public static List<Mission> Board(Galaxy g, int star, int deliveries, IEnumerable<Consignment> hold)
    {
        var r = new Rng(g.Seed * 7919 + (ulong)star * 104729 + (ulong)deliveries * 31);
        var kind = KindOf(g, star);
        if (kind == StationKind.Gate) return new();   // a gate posts no jobs
        // Within the region, or one a gate from here leads to.
        var region = g.Stars[star].Region;
        var reach = g.LinkedRegions(region).Append(region).ToHashSet();
        var places = g.Ports.Concat(g.Depots).Where(s => s != star && reach.Contains(g.Stars[s].Region))
            .OrderBy(s => r.Unit() / (KindOf(g, s) == StationKind.Colony ? ColonyPull : 1)).ToArray();
        if (places.Length == 0) return new();
        var count = kind is StationKind.Depot or StationKind.Shipyard ? SmallOffers : PortOffers;
        var made = g.Economies.TryGetValue(star, out var here) ? here.Wares.Where(here.Makes).ToArray() : Array.Empty<string>();
        var offers = new List<Mission>();
        for (int i = 0; i < count; i++)
        {
            var id = $"{star}-{deliveries}-{i}";
            var ware = i > 0 && made.Length > 0 ? made[r.Index(made.Length)] : null;
            var to = ware == null ? places[i % places.Length]
                : places.Where(p => g.Economies[p].Uses(ware)).Cast<int?>().FirstOrDefault(p => offers.All(o => o.Target != p));
            if (ware == null || to == null)
            {
                var item = Specials[r.Index(Specials.Length)];
                offers.Add(new Mission(id, star, places[i % places.Length], item, PayFor(g, star, places[i % places.Length], item)));
                continue;
            }
            var w = Wares.Get(ware);
            var cargo = w.Load(2 + r.Index(3), 0);
            cargo = cargo with { Reward = cargo.Volume * w.Price * WareReward };
            offers.Add(new Mission(id, star, to.Value, cargo, PayFor(g, star, to.Value, cargo)));
        }
        var taken = hold.Select(c => c.Mission.Id).ToHashSet();
        return offers.Where(m => !taken.Contains(m.Id)).ToList();
    }

    /// <summary>A volume as the player reads it.</summary>
    public static string Cubic(double volume) => $"{volume * Tuning.CubicMetersPerVolume:F0} m³";

    public static double VolumeUsed(IEnumerable<Consignment> hold) => hold.Sum(c => c.Mission.Cargo.Volume);
    public static bool Fits(SaveData s, Mission m) => CargoBays.Of(s).Fits(s.Hold, m.Cargo);

    /// <summary>Giving up a job costs a share of its pay; goods of one's own are just left behind.</summary>
    public static double DropFee(Consignment c) => c.IsJob ? c.Mission.Pay * Tuning.DropPenalty : 0;

    public static bool Take(SaveData s, Mission m)
    {
        if (!Fits(s, m)) return false;
        s.Hold.Add(new Consignment { Mission = m });
        return true;
    }

    /// <summary>Gives up a job. The fee stops at what the company has, so this alone can't bankrupt it.</summary>
    public static void Drop(SaveData s, Consignment c)
    {
        s.Credits = Math.Max(0, s.Credits - DropFee(c));
        s.Hold.Remove(c);
    }
}

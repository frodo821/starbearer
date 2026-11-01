namespace Starbearer.Sim;

/// <summary>What a station does. It decides which jobs its board offers.</summary>
public enum StationKind { Farm, Foundry, Lab, Clinic, Mine, City, Depot }

/// <summary>A job from a mission board: carry Cargo to Target for Pay, less whatever the cargo loses.</summary>
public sealed record Mission(string Id, int Origin, int Target, Cargo Cargo, double Pay);

/// <summary>A mission's cargo in the hold.</summary>
public sealed class Consignment
{
    public required Mission Mission { get; init; }
    public double Integrity { get; set; } = 1;
    public double Payout => Mission.Pay * Integrity;
    public Consignment Copy() => new() { Mission = Mission, Integrity = Integrity };
}

public static class Missions
{
    const int PortOffers = 5, DepotOffers = 3;

    static readonly Dictionary<StationKind, Cargo[]> Catalog = new()
    {
        [StationKind.Farm] = new Cargo[] { new("冷凍種子", 0.3, 0.45, 160, 2), new("穀物", 0.8, 0.8, 90, 4), new("培養土", 0.6, 0.9, 70, 3) },
        [StationKind.Foundry] = new Cargo[] { new("合金材", 1.0, 0.95, 120, 2), new("機械部品", 0.6, 0.85, 110, 2), new("封印コンテナ", 0.5, 0.9, 100, 3) },
        [StationKind.Lab] = new Cargo[] { new("精密機器", 0.7, 0.7, 180, 2), new("観測試料", 0.2, 0.4, 170, 1), new("実験装置", 0.5, 0.6, 150, 3) },
        [StationKind.Clinic] = new Cargo[] { new("医薬品", 0.2, 0.55, 130, 1), new("血漿", 0.3, 0.4, 190, 1), new("医療機材", 0.5, 0.75, 120, 2) },
        [StationKind.Mine] = new Cargo[] { new("鉱石", 1.2, 0.95, 100, 3), new("レアメタル", 0.9, 0.9, 170, 1), new("採掘機材", 0.8, 0.85, 110, 3) },
        [StationKind.City] = new Cargo[] { new("生活物資", 0.7, 0.8, 100, 3), new("加工食品", 0.5, 0.7, 110, 2), new("美術品", 0.2, 0.5, 200, 1) },
        [StationKind.Depot] = new Cargo[] { new("予備部品", 0.3, 0.85, 60, 1), new("空タンク", 0.2, 0.95, 40, 2), new("郵便", 0.1, 0.8, 70, 1) },
    };

    public static string Name(StationKind kind) => kind switch
    {
        StationKind.Farm => "農業ステーション",
        StationKind.Foundry => "工業ステーション",
        StationKind.Lab => "研究ステーション",
        StationKind.Clinic => "医療ステーション",
        StationKind.Mine => "小惑星採掘ステーション",
        StationKind.City => "都市ステーション",
        _ => "補給ステーション",
    };

    /// <summary>The kind comes from the seed, so it never needs saving.</summary>
    public static StationKind KindOf(Galaxy g, int star)
    {
        if (g.Depots.Contains(star)) return StationKind.Depot;
        return (StationKind)new Rng(g.Seed * 6700417 + (ulong)star).Index((int)StationKind.Depot);
    }

    /// <summary>Pay grows with the straight-line distance. Whether the way there is worth flying is the pilot's call.</summary>
    static double PayFor(Galaxy g, int from, int to, Cargo cargo)
    {
        var ly = (g.Stars[to].Position - g.Stars[from].Position).Length;
        return Math.Round(cargo.Reward * Math.Max(0.4, ly / Tuning.StandardHaulLy));
    }

    /// <summary>What a station offers. It changes with each delivery; jobs already in the hold are gone from it.</summary>
    public static List<Mission> Board(Galaxy g, int star, int deliveries, IEnumerable<Consignment> hold)
    {
        var r = new Rng(g.Seed * 7919 + (ulong)star * 104729 + (ulong)deliveries * 31);
        var kind = KindOf(g, star);
        var goods = Catalog[kind];
        var places = g.Ports.Concat(g.Depots).Where(s => s != star).OrderBy(_ => r.Next()).ToArray();
        var count = Math.Min(kind == StationKind.Depot ? DepotOffers : PortOffers, places.Length);
        var offers = new List<Mission>();
        for (int i = 0; i < count; i++)
        {
            var cargo = goods[r.Index(goods.Length)];
            offers.Add(new Mission($"{star}-{deliveries}-{i}", star, places[i], cargo, PayFor(g, star, places[i], cargo)));
        }
        var taken = hold.Select(c => c.Mission.Id).ToHashSet();
        return offers.Where(m => !taken.Contains(m.Id)).ToList();
    }

    public static double VolumeUsed(IEnumerable<Consignment> hold) => hold.Sum(c => c.Mission.Cargo.Volume);
    public static bool Fits(IEnumerable<Consignment> hold, Mission m) => VolumeUsed(hold) + m.Cargo.Volume <= Tuning.HoldVolume;

    public static double DropFee(Consignment c) => c.Mission.Pay * Tuning.DropPenalty;

    public static bool Take(SaveData s, Mission m)
    {
        if (!Fits(s.Hold, m)) return false;
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

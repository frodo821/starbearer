using System.Text.Json.Serialization;

namespace Starbearer.Sim;

/// <summary>
/// A hull: its bays, which never change, and how it flies. Size costs nothing in delta-v (dry mass and
/// tank grow together); it costs in hyperspace drag, thrust and whether a gate will take it.
/// </summary>
/// <param name="Price">What a shipyard asks for it new; also its value for gate tolls.</param>
/// <param name="DryMass">Empty, without fuel or cargo.</param>
/// <param name="Tank">Fuel it holds when full.</param>
/// <param name="Drag">Hyperspace drag, as a multiple of a small ship's: the field wraps a big hull poorly.</param>
/// <param name="Thrust">Acceleration, as a multiple of a small ship's.</param>
/// <param name="Gates">Whether gates take it.</param>
public sealed record ShipClass(string Id, string Name, Bay[] Bays, double Price, double DryMass, double Tank, double Drag, double Thrust, bool Gates)
{
    public double Capacity => Bays.Sum(b => b.Volume);
}

public static class Ships
{
    public static readonly ShipClass Courier = new("courier", "小型コンテナ船",
        new[] { new Bay(BayKind.Container, 8) }, 600, 1.0, 1.8, 1.0, 1.0, true);
    public static readonly ShipClass Freighter = new("freighter", "中型混載船",
        new[] { new Bay(BayKind.Container, 6), new Bay(BayKind.Fluid, 4), new Bay(BayKind.Bulk, 4) }, 2500, 1.75, 3.15, 1.3, 0.8, true);
    public static readonly ShipClass Tanker = new("tanker", "大型タンカー",
        new[] { new Bay(BayKind.Fluid, 12), new Bay(BayKind.Fluid, 12) }, 5000, 3.0, 5.4, 2.0, 0.5, false);
    public static readonly ShipClass Bulker = new("bulker", "大型バラ積み船",
        new[] { new Bay(BayKind.Bulk, 24) }, 4500, 3.0, 5.4, 2.0, 0.5, false);

    public static readonly ShipClass[] All = { Courier, Freighter, Tanker, Bulker };
    public static ShipClass Get(string id) => All.FirstOrDefault(s => s.Id == id) ?? Courier;

    /// <summary>
    /// What a shipyard builds, from the seed. Every one builds the small ship; each of the others is built
    /// at about half of them, and at least at one, so somewhere a long way off always has it.
    /// </summary>
    public static ShipClass[] SoldAt(Galaxy g, int star)
    {
        if (Missions.KindOf(g, star) != StationKind.Shipyard) return Array.Empty<ShipClass>();
        var yards = g.Ports.Where(p => Missions.KindOf(g, p) == StationKind.Shipyard).OrderBy(p => p).ToList();
        var index = yards.IndexOf(star);
        var r = new Rng(g.Seed * 2654435761 + (ulong)star);
        return All.Where((c, i) => i == 0 || index % (All.Length - 1) == i - 1 || r.Unit() < 0.4).ToArray();
    }

    /// <summary>What a shipyard pays for a ship: half its price, less its wear, and half what its modules cost.</summary>
    public static double SaleValue(OwnedShip s) => Math.Round(0.5 * (s.Type.Price * s.Hull + Modules.Value(s)));
}

/// <summary>One of the company's ships, where it was last docked. The one flown is SaveData.Ship; the rest wait at their stations with whatever is in their bays.</summary>
public sealed class OwnedShip
{
    public int Id { get; set; }
    public string Class { get; set; } = Ships.Courier.Id;
    /// <summary>The station it is docked at.</summary>
    public int Star { get; set; }
    public double Fuel { get; set; } = Ships.Courier.Tank;
    public double Hull { get; set; } = 1;
    public double Life { get; set; } = Tuning.LifeSupportDays;
    public List<string> Modules { get; set; } = new();
    /// <summary>Indexes of the fluid bays that carry a pressurizer.</summary>
    public List<int> Pressurized { get; set; } = new();
    /// <summary>Jobs in its bays, as of its last dock.</summary>
    public List<Consignment> Hold { get; set; } = new();

    [JsonIgnore] public ShipClass Type => Ships.Get(Class);
    public ShipState State => new(Fuel, Hull, Life);

    /// <summary>A new ship as a shipyard hands it over: full tank and supplies, empty bays.</summary>
    public static OwnedShip Build(ShipClass c, int id, int star) => new() { Id = id, Class = c.Id, Star = star, Fuel = c.Tank };
}

/// <summary>Buying, boarding and selling ships, at the station the flown ship is docked at.</summary>
public static class Hangar
{
    public static bool Buy(SaveData s, Galaxy g, ShipClass c)
    {
        if (!Ships.SoldAt(g, s.PortStar).Contains(c) || s.Credits < c.Price) return false;
        s.Credits -= c.Price;
        s.Fleet.Add(OwnedShip.Build(c, s.Fleet.Max(x => x.Id) + 1, s.PortStar));
        return true;
    }

    /// <summary>Changes ships. The one left keeps its cargo, fuel and modules where it lies.</summary>
    public static bool Board(SaveData s, int id)
    {
        if (s.Fleet.FirstOrDefault(x => x.Id == id) is not { } ship || ship.Star != s.PortStar) return false;
        s.Flown = id;
        return true;
    }

    /// <summary>Why a ship can't be sold here, or null if it can.</summary>
    public static string? SaleRefusal(SaveData s, Galaxy g, OwnedShip ship) =>
        ship.Id == s.Flown ? "乗っている船は売れない"
        : ship.Star != s.PortStar ? "ここにない"
        : Missions.KindOf(g, s.PortStar) != StationKind.Shipyard ? "売れるのは造船所だけ"
        : ship.Hold.Count > 0 ? "積荷がある"
        : null;

    public static bool Sell(SaveData s, Galaxy g, int id)
    {
        if (s.Fleet.FirstOrDefault(x => x.Id == id) is not { } ship || SaleRefusal(s, g, ship) != null) return false;
        s.Credits += Ships.SaleValue(ship);
        s.Fleet.Remove(ship);
        return true;
    }
}

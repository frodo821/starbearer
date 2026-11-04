using System.Text.Json.Serialization;

namespace Starbearer.Sim;

/// <summary>
/// A hull: its bays, which never change, and how it flies. Size costs nothing in delta-v (dry mass and
/// tank grow together); it costs in hyperspace drag, thrust and whether a gate will take it. A ship
/// built for one kind of cargo carries the most of it; a mixed hull carries every kind but less, and is
/// heavier for its separate bays. Pressure vessels weigh, so pressurized bays add dry mass.
/// </summary>
/// <param name="Price">What a shipyard asks for it new; also its value for gate tolls.</param>
/// <param name="DryMass">Empty, without fuel or cargo.</param>
/// <param name="Tank">Fuel it holds when full.</param>
/// <param name="Drag">Hyperspace drag, as a multiple of a small ship's: the field wraps a big hull poorly.</param>
/// <param name="Thrust">Acceleration, as a multiple of a small ship's: in hyperspace as it is, in normal space at full tanks and an empty hold.</param>
/// <param name="Gates">Whether gates take it.</param>
/// <param name="SublightForce">The normal-space engine's force, N; what it does depends on what the ship weighs.</param>
public sealed record ShipClass(string Id, string Name, Bay[] Bays, double Price, double DryMass, double Tank, double Drag, double Thrust, bool Gates, double SublightForce)
{
    public double Capacity => Bays.Sum(b => b.Volume);
    /// <summary>Reaction mass for normal space it holds when full (see Tuning.SublightExhaust); none for a hull with no engine for it.</summary>
    public double ReactionTank => NeedsPilot ? 0 : DryMass * Tuning.ReactionMassPerDryMass;
    /// <summary>
    /// A hull with no normal-space engine: it moves only by FTL, and comes out of a jump on a pilot ship
    /// of the company (see Voyage.Pilot), a beacon sent ahead to where it is to be.
    /// </summary>
    public bool NeedsPilot { get; init; }
}

public static class Ships
{
    /// <summary>What a size of hull flies like, and how much it holds when built for one kind of cargo.</summary>
    sealed record Size(string Id, string Name, double Volume, double DryMass, double Tank, double Drag, double Thrust, bool Gates, double Price);

    static readonly Size Small = new("small", "小型", 8, 1.0, 1.8, 1.0, 1.0, true, 600);
    static readonly Size Medium = new("medium", "中型", 16, 1.75, 3.15, 1.3, 0.8, true, 2200);
    static readonly Size Large = new("large", "大型", 24, 3.0, 5.4, 2.0, 0.5, true, 4500);

    /// <summary>A mixed hull holds this share of a specialist's room, and is this much heavier.</summary>
    const double MixedRoom = 0.8, MixedMass = 1.2;

    static double Round(double price) => Math.Round(price / 50) * 50;

    static ShipClass Make(Size z, string id, string name, Bay[] bays, double priceFactor, double dryFactor = 1) =>
        new(id, z.Name + name, bays, Round(z.Price * priceFactor),
            z.DryMass * dryFactor + bays.Where(b => b.Pressurized).Sum(b => b.Volume) * Tuning.PressureMassPerVolume,
            z.Tank, z.Drag, z.Thrust, z.Gates, EngineForce(z));

    /// <summary>One engine per size of hull, sized to push the plain hull of that size, tanks full and hold empty, at its acceleration: heavier variants of it are slower.</summary>
    static double EngineForce(Size z) =>
        Tuning.SublightAccelerationSi * z.Thrust * Units.Kg(z.DryMass * (1 + Tuning.ReactionMassPerDryMass) + z.Tank);

    static ShipClass ContainerShip(Size z, string? id = null) => Make(z, id ?? $"container-{z.Id}", "コンテナ船", new[] { new Bay(BayKind.Container, z.Volume) }, 1);
    static ShipClass TankerShip(Size z) => Make(z, $"tanker-{z.Id}", "タンカー", new[] { new Bay(BayKind.Fluid, z.Volume / 2), new Bay(BayKind.Fluid, z.Volume / 2) }, 1.1);
    static ShipClass GasShip(Size z) => Make(z, $"gas-{z.Id}", "ガス運搬船", new[] { new Bay(BayKind.Fluid, z.Volume / 2, true), new Bay(BayKind.Fluid, z.Volume / 2, true) }, 1.5);
    static ShipClass BulkShip(Size z) => Make(z, $"bulk-{z.Id}", "バラ積み船", new[] { new Bay(BayKind.Bulk, z.Volume) }, 1);
    static ShipClass MixedShip(Size z)
    {
        var room = z.Volume * MixedRoom;
        return Make(z, $"mixed-{z.Id}", "混載船", new[] { new Bay(BayKind.Container, room / 2), new Bay(BayKind.Fluid, room / 4), new Bay(BayKind.Bulk, room / 4) }, 1.3, MixedMass);
    }

    public static readonly ShipClass Courier = ContainerShip(Small, "courier");

    /// <summary>
    /// The rental shuttle: a hull a third of a small ship's, no bays at all, only to move the pilot. Its
    /// tank keeps a small ship's share, so it jumps as far; it is hired, never built or sold (see Rentals).
    /// </summary>
    static readonly Size Tiny = new("tiny", "超小型", 0, 0.3, 0.54, 0.8, 1.2, true, 0);
    public static readonly ShipClass Shuttle = new("shuttle", "レンタルシャトル", Array.Empty<Bay>(), 0, Tiny.DryMass, Tiny.Tank, Tiny.Drag, Tiny.Thrust, Tiny.Gates, EngineForce(Tiny));
    /// <summary>
    /// The super-large ship, a station that moves: four large hulls' room in all three kinds of bay, too
    /// big for any gate, no engine for normal space. It jumps like any ship, and comes out on its pilot.
    /// Built at one shipyard of the galaxy's for each other hull, rarely and dearly.
    /// </summary>
    static readonly Size Huge = new("huge", "超大型", 96, 12, 21.6, 4.0, 0.3, false, 30000);
    public static readonly ShipClass Carrier = Make(Huge, "carrier-huge", "母艦",
        new[] { new Bay(BayKind.Container, 32), new Bay(BayKind.Fluid, 32), new Bay(BayKind.Bulk, 32) }, 1) with { NeedsPilot = true, SublightForce = 0 };
    public static readonly ShipClass Tanker = TankerShip(Large);
    public static readonly ShipClass Bulker = BulkShip(Large);

    /// <summary>Every hull, the small container ship first. Big hulls are only built for one kind of cargo.</summary>
    public static readonly ShipClass[] All =
    {
        Courier, TankerShip(Small), GasShip(Small), BulkShip(Small), MixedShip(Small),
        ContainerShip(Medium), TankerShip(Medium), GasShip(Medium), BulkShip(Medium), MixedShip(Medium),
        ContainerShip(Large), Tanker, GasShip(Large), Bulker, Carrier,
    };

    public static ShipClass Get(string id) => id == Shuttle.Id ? Shuttle : All.FirstOrDefault(s => s.Id == id) ?? Courier;

    /// <summary>
    /// What a shipyard builds, from the seed. Every one builds the small container ship; each of the others
    /// is built at about a third of them, and at least at one, so somewhere a long way off always has it.
    /// </summary>
    public static ShipClass[] SoldAt(Galaxy g, int station)
    {
        if (!g.IsStation(station) || g.KindOf(station) != StationKind.Shipyard) return Array.Empty<ShipClass>();
        var yards = g.Ports.Where(p => g.KindOf(p) == StationKind.Shipyard).OrderBy(p => p).ToList();
        var index = yards.IndexOf(station);
        var r = new Rng(g.Seed * 2654435761 + g.SeedKey(station));
        return All.Where((c, i) => i == 0 || (i - 1) % yards.Count == index || !c.NeedsPilot && r.Unit() < 0.3).ToArray();
    }

    /// <summary>What a shipyard pays for a ship: half its price, less its wear, and half what its modules cost.</summary>
    public static double SaleValue(OwnedShip s) => Math.Round(0.5 * (s.Type.Price * s.Hull + Modules.Value(s)));
}

/// <summary>One of the company's ships, where it was last docked. The one flown is SaveData.Ship; the rest wait at their stations with whatever is in their bays.</summary>
public sealed class OwnedShip
{
    public int Id { get; set; }
    public string Class { get; set; } = Ships.Courier.Id;
    /// <summary>The station it is docked at, by station id; -1 while it is left in orbit (see Parked).</summary>
    public int Station { get; set; }
    /// <summary>Where it was left in orbit, if it was: it coasts on from there. Null for a ship at a station.</summary>
    public ParkedState? Parked { get; set; }
    /// <summary>The ship carrying it aboard, by id; 0 if none. Only a rental shuttle rides in another ship.</summary>
    public int Carrier { get; set; }
    /// <summary>Its hire, if it is a rental shuttle.</summary>
    public RentalState? Rental { get; set; }
    public double Fuel { get; set; } = Ships.Courier.Tank;
    /// <summary>Reaction mass for normal space. Below zero (a save from before it was kept apart) reads as full.</summary>
    public double ReactionMass { get; set; } = -1;
    public double Hull { get; set; } = 1;
    public double Life { get; set; } = Tuning.LifeSupportDays;
    public List<string> Modules { get; set; } = new();
    /// <summary>Indexes of the fluid bays that carry a pressurizer.</summary>
    public List<int> Pressurized { get; set; } = new();
    /// <summary>Jobs in its bays, as of its last dock.</summary>
    public List<Consignment> Hold { get; set; } = new();

    [JsonIgnore] public ShipClass Type => Ships.Get(Class);
    /// <summary>The hull as it flies: its class, plus the weight of the pressurizers fitted to it.</summary>
    [JsonIgnore] public ShipClass Fitted => Type with { DryMass = Type.DryMass + Pressurized.Sum(i => Type.Bays[i].Volume) * Tuning.PressureMassPerVolume };
    public ShipState State => new(Fuel, Hull, Life, ReactionMass < 0 ? null : ReactionMass);
    /// <summary>What it weighs with its tanks as they are: the load it is to a ship carrying it.</summary>
    [JsonIgnore] public double Mass => Fitted.DryMass + Fuel + (ReactionMass < 0 ? Fitted.ReactionTank : ReactionMass) + Hold.Sum(c => c.Mission.Cargo.Mass);

    /// <summary>A new ship as a shipyard hands it over: full tank and supplies, empty bays.</summary>
    public static OwnedShip Build(ShipClass c, int id, int station) => new() { Id = id, Class = c.Id, Station = station, Fuel = c.Tank, ReactionMass = c.ReactionTank };
}

/// <summary>Buying, boarding and selling ships, at the station the flown ship is docked at.</summary>
public static class Hangar
{
    public static bool Buy(SaveData s, Galaxy g, ShipClass c)
    {
        if (!Ships.SoldAt(g, s.PortStation).Contains(c) || s.Credits < c.Price) return false;
        s.Credits -= c.Price;
        s.Fleet.Add(OwnedShip.Build(c, s.Fleet.Max(x => x.Id) + 1, s.PortStation));
        return true;
    }

    /// <summary>Changes ships. The one left keeps its cargo, fuel and modules where it lies.</summary>
    public static bool Board(SaveData s, int id)
    {
        if (s.Fleet.FirstOrDefault(x => x.Id == id) is not { } ship || ship.Station != s.PortStation) return false;
        s.Flown = id;
        return true;
    }

    /// <summary>Why a ship can't be sold here, or null if it can.</summary>
    public static string? SaleRefusal(SaveData s, Galaxy g, OwnedShip ship) =>
        ship.Id == s.Flown ? "乗っている船は売れない"
        : ship.Station != s.PortStation ? "ここにない"
        : g.KindOf(s.PortStation) != StationKind.Shipyard ? "売れるのは造船所だけ"
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

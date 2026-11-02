namespace Starbearer.Sim;

/// <summary>How a load is carried. A bay takes only its own kind, except that a fluid bay with a pressurizer also takes pressurized fluid.</summary>
public enum CargoKind { Fluid, PressurizedFluid, Bulk, Container }

public enum BayKind { Fluid, Bulk, Container }

public sealed record Bay(BayKind Kind, double Volume);

/// <summary>A hull and the bays built into it. A ship's bays never change; only a pressurizer can be fitted to a fluid bay.</summary>
public sealed record ShipClass(string Id, string Name, Bay[] Bays);

public static class Ships
{
    public static readonly ShipClass Courier = new("courier", "小型コンテナ船", new[] { new Bay(BayKind.Container, 8) });
    public static readonly ShipClass[] All = { Courier };
    public static ShipClass Get(string id) => All.FirstOrDefault(s => s.Id == id) ?? Courier;
}

/// <summary>The ship's bays as fitted: which fluid bays carry a pressurizer.</summary>
public sealed record CargoBays(ShipClass Ship, IReadOnlyCollection<int> Pressurized)
{
    public static CargoBays Of(SaveData s) => new(Ships.Get(s.Ship), s.Pressurized);

    public double Capacity => Ship.Bays.Sum(b => b.Volume);
    double Room(BayKind kind) => Ship.Bays.Where(b => b.Kind == kind).Sum(b => b.Volume);
    double PressurizedRoom => Ship.Bays.Select((b, i) => (b, i)).Where(x => x.b.Kind == BayKind.Fluid && Pressurized.Contains(x.i)).Sum(x => x.b.Volume);

    /// <summary>Room for this kind of load, empty. Pressurized fluid has only the pressurized bays; plain fluid has every fluid bay.</summary>
    public double RoomFor(CargoKind kind) => kind switch
    {
        CargoKind.Container => Room(BayKind.Container),
        CargoKind.Bulk => Room(BayKind.Bulk),
        CargoKind.Fluid => Room(BayKind.Fluid),
        _ => PressurizedRoom,
    };

    static double Used(IEnumerable<Cargo> load, CargoKind kind) => load.Where(c => c.Kind == kind).Sum(c => c.Volume);

    /// <summary>Whether everything fits at once. Pressurized fluid goes in the pressurized bays first, and plain fluid fills what the fluid bays have left.</summary>
    public bool Fits(IEnumerable<Cargo> load)
    {
        var all = load.ToList();
        const double eps = 1e-9;
        var pressurized = Used(all, CargoKind.PressurizedFluid);
        return Used(all, CargoKind.Container) <= RoomFor(CargoKind.Container) + eps
            && Used(all, CargoKind.Bulk) <= RoomFor(CargoKind.Bulk) + eps
            && pressurized <= PressurizedRoom + eps
            && pressurized + Used(all, CargoKind.Fluid) <= Room(BayKind.Fluid) + eps;
    }

    public bool Fits(IEnumerable<Consignment> hold, Cargo more) => Fits(hold.Select(c => c.Mission.Cargo).Append(more));

    /// <summary>Why a load can't go aboard, or null if it can.</summary>
    public string? Refusal(IEnumerable<Consignment> hold, Cargo more) =>
        Fits(hold, more) ? null
        : RoomFor(more.Kind) <= 0 ? more.Kind == CargoKind.PressurizedFluid && Room(BayKind.Fluid) > 0 ? "加圧モジュールがない" : $"{Cargos.BayName(more.Kind)}がない"
        : $"{Cargos.BayName(more.Kind)}の空きが足りない";

    /// <summary>Fluid bays a pressurizer could still be fitted to.</summary>
    public IEnumerable<int> Unpressurized => Ship.Bays.Select((b, i) => (b, i)).Where(x => x.b.Kind == BayKind.Fluid && !Pressurized.Contains(x.i)).Select(x => x.i);
}

public static class Cargos
{
    public static string Name(CargoKind kind) => kind switch
    {
        CargoKind.Fluid => "液体",
        CargoKind.PressurizedFluid => "加圧液体",
        CargoKind.Bulk => "バラ積み",
        _ => "コンテナ",
    };

    public static string Name(BayKind kind) => kind switch
    {
        BayKind.Fluid => "液体ベイ",
        BayKind.Bulk => "バラ積みベイ",
        _ => "コンテナベイ",
    };

    /// <summary>The bay a kind of load needs, as the player reads it.</summary>
    public static string BayName(CargoKind kind) => kind switch
    {
        CargoKind.PressurizedFluid => "加圧液体ベイ",
        CargoKind.Fluid => "液体ベイ",
        CargoKind.Bulk => "バラ積みベイ",
        _ => "コンテナベイ",
    };
}

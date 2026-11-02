using System.Text.Json;
using System.Text.Json.Serialization;
namespace Starbearer.Sim;

/// <summary>Checkpoint taken while docked. A flight is never saved: that could lock in a doomed trajectory.</summary>
/// <summary>Quality-of-life automation bought at ports. Nothing here is required to finish a mission.</summary>
public static class Modules
{
    public const string AutoBurn = "autoburn";
    public const string Transfer = "transfer";
    public static readonly (string Id, string Name, double Price)[] Shop =
    {
        (AutoBurn, "自動噴射装置", 150),
        (Transfer, "転送計算機", 250),
    };

    /// <summary>Fitted to one fluid bay, it lets that bay carry pressurized fluid. Sold per bay, so it is not in the shop list.</summary>
    public const string PressurizerName = "加圧モジュール";
    public const double PressurizerPrice = 200;

    /// <summary>What the modules aboard are worth, pressurizers included.</summary>
    public static double Value(OwnedShip s) =>
        s.Modules.Sum(id => Shop.FirstOrDefault(m => m.Id == id).Price) + s.Pressurized.Count * PressurizerPrice;

    /// <summary>Fits a pressurizer to the first fluid bay of the ship flown without one. False if there is none, or not the money.</summary>
    public static bool FitPressurizer(SaveData s)
    {
        var bay = CargoBays.Of(s).Unpressurized.Cast<int?>().FirstOrDefault();
        if (bay is not int i || s.Credits < PressurizerPrice) return false;
        s.Credits -= PressurizerPrice;
        s.Pressurized.Add(i);
        return true;
    }
}

/// <summary>What a stop on the way sells. Purchases go straight to the checkpoint.</summary>
public static class Supply
{
    static double Spend(SaveData s, double room, double price)
    {
        var amount = Math.Clamp(s.Credits / price, 0, Math.Max(room, 0));
        s.Credits -= amount * price;
        return amount;
    }

    public static void BuyFuel(SaveData s) => s.HeldFuel += Spend(s, s.Ship.Type.Tank - s.HeldFuel, Tuning.FuelPrice);
    public static void BuyLife(SaveData s) => s.HeldLife += Spend(s, Tuning.LifeSupportDays - s.HeldLife, Tuning.LifePricePerDay);
    public static void RepairHull(SaveData s) => s.HeldHull += Spend(s, 1 - s.HeldHull, Tuning.HullRepairPrice);

    /// <summary>Even spending every credit on fuel, no ship docked here could leave for another station. Ships elsewhere are out of reach.</summary>
    public static bool Bankrupt(SaveData s) =>
        s.Fleet.Where(x => x.Star == s.PortStar).All(x => x.Fuel + s.Credits / Tuning.FuelPrice < Tuning.DepartureFuelFloor);
}

public sealed class SaveData
{
    public ulong Seed { get; set; }
    public int Deliveries { get; set; }
    public int Retries { get; set; }
    public double Credits { get; set; } = Tuning.StartingCredits;
    public double TotalDays { get; set; }
    public double[] ClosestApproach { get; set; } = Array.Empty<double>();
    /// <summary>Every ship the company owns, each where it was last docked.</summary>
    public List<OwnedShip> Fleet { get; set; } = new() { new OwnedShip { Id = 1 } };
    /// <summary>Id of the ship flown. The checkpoint is where it is docked.</summary>
    public int Flown { get; set; } = 1;
    /// <summary>The star the instruments steer for. Falls back when no cargo is bound there.</summary>
    public int Course { get; set; } = -1;

    [JsonIgnore] public OwnedShip Ship => Fleet.First(x => x.Id == Flown);
    // The ship flown, as the rest of the game reads it.
    [JsonIgnore] public int PortStar { get => Ship.Star; set => Ship.Star = value; }
    [JsonIgnore] public List<string> Modules { get => Ship.Modules; set => Ship.Modules = value; }
    [JsonIgnore] public List<int> Pressurized { get => Ship.Pressurized; set => Ship.Pressurized = value; }
    /// <summary>Jobs in the bays of the ship flown, as of the last dock.</summary>
    [JsonIgnore] public List<Consignment> Hold { get => Ship.Hold; set => Ship.Hold = value; }
    [JsonIgnore] public double HeldFuel { get => Ship.Fuel; set => Ship.Fuel = value; }
    [JsonIgnore] public double HeldHull { get => Ship.Hull; set => Ship.Hull = value; }
    [JsonIgnore] public double HeldLife { get => Ship.Life; set => Ship.Life = value; }

    /// <summary>The ships docked at a station, the one flown first.</summary>
    public IEnumerable<OwnedShip> ShipsAt(int star) => Fleet.Where(x => x.Star == star).OrderBy(x => x.Id != Flown).ThenBy(x => x.Id);

    static readonly JsonSerializerOptions Options = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static SaveData? FromJson(string json) => JsonSerializer.Deserialize<SaveData>(json, Options);

    public static (SaveData Save, Galaxy Galaxy) NewGalaxy(ulong seed)
    {
        var g = Galaxy.Generate(seed);
        return (new SaveData
        {
            Seed = seed, Fleet = new() { new OwnedShip { Id = 1, Star = g.Ports[0] } },
            ClosestApproach = Enumerable.Repeat(double.MaxValue, g.Stars.Length).ToArray(),
        }, g);
    }
}

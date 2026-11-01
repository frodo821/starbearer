using System.Text.Json;
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
}

/// <summary>What a stop on the way sells. Purchases go straight to the checkpoint.</summary>
public static class Supply
{
    public static double FullTank => Tuning.TankCapacity * Tuning.FuelPrice;

    static double Spend(SaveData s, double room, double price)
    {
        var amount = Math.Clamp(s.Credits / price, 0, Math.Max(room, 0));
        s.Credits -= amount * price;
        return amount;
    }

    public static void BuyFuel(SaveData s) => s.HeldFuel += Spend(s, Tuning.TankCapacity - s.HeldFuel, Tuning.FuelPrice);
    public static void BuyLife(SaveData s) => s.HeldLife += Spend(s, Tuning.LifeSupportDays - s.HeldLife, Tuning.LifePricePerDay);
    public static void RepairHull(SaveData s) => s.HeldHull += Spend(s, 1 - s.HeldHull, Tuning.HullRepairPrice);

    /// <summary>Even spending every credit on fuel, the ship could not leave for another station.</summary>
    public static bool Bankrupt(SaveData s) => s.HeldFuel + s.Credits / Tuning.FuelPrice < Tuning.DepartureFuelFloor;
}

public sealed class SaveData
{
    public ulong Seed { get; set; }
    public int PortStar { get; set; }
    public int Deliveries { get; set; }
    public int Retries { get; set; }
    public double Credits { get; set; } = Tuning.StartingCredits;
    public double TotalDays { get; set; }
    public double[] ClosestApproach { get; set; } = Array.Empty<double>();
    public List<string> Modules { get; set; } = new();
    /// <summary>Jobs in the cargo hold, as of the last dock.</summary>
    public List<Consignment> Hold { get; set; } = new();
    /// <summary>The star the instruments steer for. Falls back when no cargo is bound there.</summary>
    public int Course { get; set; } = -1;
    public double HeldFuel { get; set; } = Tuning.TankCapacity;
    public double HeldHull { get; set; } = 1;
    public double HeldLife { get; set; } = Tuning.LifeSupportDays;

    static readonly JsonSerializerOptions Options = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static SaveData? FromJson(string json) => JsonSerializer.Deserialize<SaveData>(json, Options);

    public static (SaveData Save, Galaxy Galaxy) NewGalaxy(ulong seed)
    {
        var g = Galaxy.Generate(seed);
        return (new SaveData
        {
            Seed = seed, PortStar = g.Ports[0],
            ClosestApproach = Enumerable.Repeat(double.MaxValue, g.Stars.Length).ToArray(),
        }, g);
    }
}

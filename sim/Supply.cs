namespace Starbearer.Sim;

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

namespace Starbearer.Sim;

/// <summary>What a stop on the way sells besides goods (fuel is a ware: see Trade). Purchases go straight to the checkpoint.</summary>
public static class Supply
{
    static double Spend(SaveData s, double room, double price)
    {
        var amount = Math.Clamp(s.Credits / price, 0, Math.Max(room, 0));
        s.Credits -= amount * price;
        return amount;
    }

    public static void BuyLife(SaveData s) => s.HeldLife += Spend(s, Tuning.LifeSupportDays - s.HeldLife, Tuning.LifePricePerDay);
    public static void RepairHull(SaveData s) => s.HeldHull += Spend(s, 1 - s.HeldHull, Tuning.HullRepairPrice);
    public static void BuyReactionMass(SaveData s) => s.HeldReaction += Spend(s, s.Ship.Fitted.ReactionTank - s.HeldReaction, Tuning.ReactionMassPrice);
    public static double ReactionMassCost(SaveData s) => Math.Max(0, s.Ship.Fitted.ReactionTank - s.HeldReaction) * Tuning.ReactionMassPrice;

    /// <summary>
    /// Even spending every credit on fuel at today's price here, no ship docked here could leave for
    /// another station. Ships elsewhere are out of reach. A station out of fuel is not bankruptcy: the
    /// stock comes back with time.
    /// </summary>
    public static bool Bankrupt(SaveData s, Galaxy g) =>
        s.Fleet.Where(x => x.Station == s.PortStation).All(x => x.Fuel + s.Credits / Trade.FuelPricePerMass(g, s.MarketIn(g), s.PortStation) < Tuning.DepartureFuelFloor);
}

namespace Starbearer.Sim;

public static class Gates
{
    /// <summary>What a passage costs: a share of what goes through, the ship, its modules and its cargo.</summary>
    public static double Toll(SaveData s, Voyage v) =>
        Math.Round(Tuning.GateTollRate * (Tuning.HullValue * v.Hull
            + Modules.Value(s)
            + v.Hold.Sum(c => c.Payout)));
}

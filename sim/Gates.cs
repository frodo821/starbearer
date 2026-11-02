namespace Starbearer.Sim;

public static class Gates
{
    /// <summary>What a passage costs: a share of what goes through, the ship, its modules and its cargo.</summary>
    public static double Toll(SaveData s, Voyage v) =>
        Math.Round(Tuning.GateTollRate * (s.Ship.Type.Price * v.Hull
            + Modules.Value(s.Ship)
            + v.Hold.Sum(c => c.Value)));
}

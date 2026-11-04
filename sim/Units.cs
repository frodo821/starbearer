namespace Starbearer.Sim;

/// <summary>
/// What the sim's units are in SI. Orbits are reckoned in AU and days, hyperspace in ly and days (its
/// physics is the game's own, so it has no SI reading), and a ship's mass in units of 100 t: a 20 ft
/// container of the heaviest goods, steel or ore at 0.25 to 0.3 a unit, comes to 25 to 30 t, about
/// what a real one may gross. The ship's physics is set in SI in Tuning and carried into these here.
/// </summary>
public static class Units
{
    public const double MetresPerAu = 149_597_870_700, SecondsPerDay = 86_400;
    /// <summary>Kilograms in a unit of ship mass (100 t).</summary>
    public const double KgPerMass = 100_000;
    /// <summary>Standard gravity, for specific impulse.</summary>
    public const double G0 = 9.80665;

    public static double MetresPerSecond(double auPerDay) => auPerDay * MetresPerAu / SecondsPerDay;
    public static double AuPerDay(double metresPerSecond) => metresPerSecond * SecondsPerDay / MetresPerAu;
    public static double MetresPerSecond2(double auPerDay2) => auPerDay2 * MetresPerAu / (SecondsPerDay * SecondsPerDay);
    public static double AuPerDay2(double metresPerSecond2) => metresPerSecond2 * SecondsPerDay * SecondsPerDay / MetresPerAu;
    public static double Kg(double mass) => mass * KgPerMass;
}

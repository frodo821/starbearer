namespace Starbearer.Sim;

/// <summary>A rental shuttle's hire: the deposit paid, and the galaxy day it was hired on.</summary>
public sealed class RentalState
{
    public double Deposit { get; set; }
    public double Since { get; set; }
}

/// <summary>
/// Rental shuttles: hired at any station for a deposit, returned at any station for what the deposit
/// has left after the rent. One rides aboard a ship of the company, for the pilot to leave that ship in
/// orbit and fly off. A shuttle whose rent has run past its deposit is overdue: the next station it
/// calls at takes it back, and what it owes beyond the deposit is a debt, paid before the next hire.
/// Days are galaxy days.
/// </summary>
public static class Rentals
{
    public static double Rent(RentalState r, double today) => Math.Max(0, today - r.Since) * Tuning.ShuttleRentPerDay;

    /// <summary>What returning it would give back: the deposit less the rent, never below nothing.</summary>
    public static double Refund(RentalState r, double today) => Math.Max(0, r.Deposit - Rent(r, today));

    public static bool Overdue(OwnedShip ship, double today) => ship.Rental is { } r && Rent(r, today) > r.Deposit;

    /// <summary>The shuttle a ship carries aboard, if any.</summary>
    public static OwnedShip? CarriedBy(SaveData s, int carrier) => s.Fleet.FirstOrDefault(x => x.Carrier == carrier && carrier != 0);

    public static double CarriedMass(SaveData s, int carrier) => CarriedBy(s, carrier)?.Mass ?? 0;

    /// <summary>Why no shuttle can be hired here now, or null if one can. Aboard: to ride in the ship flown.</summary>
    public static string? Refusal(SaveData s, bool aboard) =>
        s.RentDebt > 0 ? $"延滞金 {s.RentDebt:F0} を払うまで借りられない"
        : s.Credits < Tuning.ShuttleDeposit ? $"保証金 {Tuning.ShuttleDeposit:F0} が足りない"
        : aboard && s.Ship.Rental != null ? "シャトルにシャトルは載らない"
        : aboard && CarriedBy(s, s.Flown) != null ? "もう1隻載せている"
        : null;

    /// <summary>Hires a shuttle at the station the ship flown is docked at: aboard it, or left at the station to be boarded.</summary>
    public static OwnedShip? Hire(SaveData s, double today, bool aboard)
    {
        if (Refusal(s, aboard) != null) return null;
        s.Credits -= Tuning.ShuttleDeposit;
        var ship = OwnedShip.Build(Ships.Shuttle, s.Fleet.Max(x => x.Id) + 1, aboard ? -1 : s.PortStation);
        ship.Rental = new RentalState { Deposit = Tuning.ShuttleDeposit, Since = today };
        if (aboard) ship.Carrier = s.Flown;
        s.Fleet.Add(ship);
        return ship;
    }

    /// <summary>Gives a shuttle back: the deposit's remainder is paid out and it leaves the fleet. Returns what was paid.</summary>
    public static double Return(SaveData s, OwnedShip shuttle, double today)
    {
        var refund = Refund(shuttle.Rental!, today);
        s.Credits += refund;
        s.Fleet.Remove(shuttle);
        return refund;
    }

    /// <summary>An overdue shuttle taken back: what it owes past its deposit goes on the debt. Returns that.</summary>
    public static double Confiscate(SaveData s, OwnedShip shuttle, double today)
    {
        var over = Math.Max(0, Rent(shuttle.Rental!, today) - shuttle.Rental!.Deposit);
        s.RentDebt += over;
        s.Fleet.Remove(shuttle);
        return over;
    }

    /// <summary>
    /// At a port call: shuttles past their deposit are taken back, the one flown and one riding aboard
    /// it. The pilot of a taken shuttle boards another ship of the company docked here; with none, the
    /// company is stranded (the shuttle stays on the books, so the save still reads). Returns the lines
    /// for the port's report.
    /// </summary>
    public static List<string> TakeBack(SaveData s, int station, double day)
    {
        var report = new List<string>();
        if (CarriedBy(s, s.Flown) is { } aboard && Overdue(aboard, day))
            report.Add($"延滞のシャトルを没収された。延滞金 {Confiscate(s, aboard, day):F0}（払うまで次は借りられない）");
        if (s.Ship.Rental is not { } hire || !Overdue(s.Ship, day)) return report;
        var shuttle = s.Ship;
        if (s.Fleet.Where(x => x.Station == station && x.Id != shuttle.Id).OrderBy(x => x.Id).FirstOrDefault() is not { } other)
        {
            s.RentDebt += Math.Max(0, Rent(hire, day) - hire.Deposit);
            s.Stranded = true;
            report.Add("延滞のシャトルを没収された。この港に乗れる船がない");
            return report;
        }
        s.Flown = other.Id;
        report.Add($"延滞のシャトルを没収された。延滞金 {Confiscate(s, shuttle, day):F0}（払うまで次は借りられない）。{other.Type.Name} に乗り換えた");
        return report;
    }

    /// <summary>The star a ship of the fleet is at: its station's, its orbit's, or that of the ship carrying it.</summary>
    public static int StarOf(SaveData s, Galaxy g, OwnedShip ship) =>
        ship.Parked is { } p ? p.Star
        : ship.Carrier != 0 && s.Fleet.FirstOrDefault(x => x.Id == ship.Carrier) is { } carrier ? StarOf(s, g, carrier)
        : g.StarOf(ship.Station);

    /// <summary>Where a ship of the fleet is, in words: its station, the body it was left circling, or the ship carrying it.</summary>
    public static string Place(SaveData s, Galaxy g, OwnedShip ship) =>
        ship.Parked is { } p ? $"{g.System(p.Star).Tree.First(c => c.Index == p.Body).Name} の軌道上"
        : ship.Carrier != 0 && s.Fleet.FirstOrDefault(x => x.Id == ship.Carrier) is { } carrier ? $"{carrier.Type.Name}に搭載"
        : Missions.Label(g, ship.Station);

    /// <summary>Pays as much of the debt as the credits allow. Returns what was paid.</summary>
    public static double PayDebt(SaveData s)
    {
        var paid = Math.Min(s.RentDebt, s.Credits);
        s.Credits -= paid;
        s.RentDebt -= paid;
        if (s.RentDebt < 1e-9) s.RentDebt = 0;
        return paid;
    }
}

using Starbearer.Sim;
using Xunit;

/// <summary>Rental shuttles: the deposit and the rent, taking one back overdue, the debt, and a pilot stranded.</summary>
public class RentalTests
{
    static SaveData Company(out Galaxy g)
    {
        (var s, g) = SaveData.NewGalaxy(5);
        s.Credits = 500;
        return s;
    }

    [Fact]
    public void AShuttleIsHiredForADepositAndGivesBackWhatTheRentLeaves()
    {
        var s = Company(out _);
        var shuttle = Rentals.Hire(s, 100, aboard: true)!;
        Assert.Equal(500 - Tuning.ShuttleDeposit, s.Credits);
        Assert.Equal(s.Flown, shuttle.Carrier);
        Assert.Empty(shuttle.Type.Bays);
        Assert.Equal(shuttle.Mass, Rentals.CarriedMass(s, s.Flown));
        Assert.Equal("もう1隻載せている", Rentals.Refusal(s, aboard: true));
        var refund = Rentals.Return(s, shuttle, 200);
        Assert.Equal(Tuning.ShuttleDeposit - 100 * Tuning.ShuttleRentPerDay, refund, 9);
        Assert.DoesNotContain(shuttle, s.Fleet);
        Assert.Equal(0, Rentals.CarriedMass(s, s.Flown));
    }

    [Fact]
    public void AnOverdueShuttleRidingAboardIsTakenBackAndItsDebtBarsTheNextHire()
    {
        var s = Company(out _);
        var shuttle = Rentals.Hire(s, 0, aboard: true)!;
        var overdue = Tuning.ShuttleDeposit / Tuning.ShuttleRentPerDay + 100;
        Assert.False(Rentals.Overdue(shuttle, overdue - 200));
        var report = Rentals.TakeBack(s, s.PortStation, overdue);
        Assert.Single(report);
        Assert.DoesNotContain(shuttle, s.Fleet);
        Assert.Equal(100 * Tuning.ShuttleRentPerDay, s.RentDebt, 9);
        Assert.NotNull(Rentals.Refusal(s, aboard: false));
        Rentals.PayDebt(s);
        Assert.Equal(0, s.RentDebt);
        Assert.Null(Rentals.Refusal(s, aboard: false));
        Assert.False(s.Stranded);
    }
    [Fact]
    public void APilotInAnOverdueShuttleBoardsAnotherShipHereOrIsStranded()
    {
        var s = Company(out var gal);
        var port = s.PortStation;
        var first = s.Ship;
        var shuttle = Rentals.Hire(s, 0, aboard: false)!;
        s.Flown = shuttle.Id;
        var overdue = Tuning.ShuttleDeposit / Tuning.ShuttleRentPerDay + 1;
        Rentals.TakeBack(s, port, overdue);                       // the first ship is docked here: board it
        Assert.Equal(first.Id, s.Flown);
        Assert.False(s.Stranded);

        var again = Rentals.Hire(s, overdue, aboard: false);
        Assert.Null(again);                                        // the debt first
        Rentals.PayDebt(s);
        again = Rentals.Hire(s, overdue, aboard: false)!;
        s.Flown = again.Id;
        first.Station = -1;
        first.Parked = ParkedState.Of(gal.System(gal.StarOf(port)), new Vec3d(1, 0, 0), new Vec3d(0, 0, 0.01), overdue);
        Rentals.TakeBack(s, port, overdue * 3);                   // nothing else here: stranded
        Assert.True(s.Stranded);
        Assert.Equal(again.Id, s.Ship.Id);                        // the save still has a ship to read
    }

    [Fact]
    public void ChangingShipMidVoyageKeepsTheClockAndThePlace()
    {
        var gal = Galaxy.Generate(3);
        var v = new Voyage(gal, gal.Ports[0], gal.StarOf(gal.Ports[1]), Array.Empty<Consignment>(), Enumerable.Repeat(double.MaxValue, gal.Stars.Length).ToArray(), 1);
        v.Undock();
        v.AdvanceSublight(5, Vec3d.Zero);
        var (t, p, life) = (v.AbsoluteTime, v.SubPosition, v.LifeRemaining);
        v.Reship(Ships.Shuttle, new ShipState(0.3, 0.9, 500), Array.Empty<Consignment>(), 0);
        Assert.Equal(t, v.AbsoluteTime);
        Assert.Equal(p, v.SubPosition);
        Assert.Equal(500, v.LifeRemaining, 9);
        Assert.Equal(Ships.Shuttle, v.Class);
        Assert.True(life > 500);
        v.CarriedMass = 1;
        Assert.Equal(Ships.Shuttle.DryMass + 0.3 + Ships.Shuttle.ReactionTank + 1, v.Mass, 9);
    }
}

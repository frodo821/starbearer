using Starbearer.Sim;
using Xunit;

/// <summary>Docking, the port, supplies, life support while docked, and what a save keeps.</summary>
public partial class SimTests
{
    [Fact]
    public void SaveRoundTripsUnvisitedStars()
    {
        var s = SaveData.NewGalaxy(11).Save;
        s.Deliveries = 2; s.Retries = 3;
        var back = SaveData.FromJson(s.ToJson())!;
        Assert.Equal(s.Seed, back.Seed);
        Assert.Equal(2, back.Deliveries);
        Assert.Equal(double.MaxValue, back.ClosestApproach[0]);
    }

    [Fact]
    public void AnyStationTakesAShipThatMatchesIt()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        var depot = g.Depots[0];
        var st = g.StationAt(depot);
        v.EnterFtl();
        v.PlaceForTest(g.Stars[depot].Position + (st.Host!.StarPosition(v.AbsoluteTime) + st.Position(v.AbsoluteTime)) * Sublight.Scale, st.Host.StarVelocity(v.AbsoluteTime) + st.Velocity(v.AbsoluteTime));
        Assert.True(v.Descend());
        v.AdvanceSublight(0.01, Vec3d.Zero);
        Assert.Equal(Phase.Sublight, v.Phase);   // close, but docking is the pilot's call
        // The last of the speed is trimmed with the assist, as a pilot would with X.
        for (int i = 0; i < 400 && !v.CanDock; i++) v.AdvanceSublight(0.002, v.AssistTowardStation(approach: true)!.Value);
        Assert.Equal(Phase.Sublight, v.Phase);
        Assert.True(v.RequestDock());
        Assert.Equal(Phase.Arrived, v.Phase);
    }

    [Fact]
    public void LeavingAStationDoesNotDockStraightBack()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.AdvanceSublight(5, Vec3d.Zero);
        Assert.Equal(Phase.Sublight, v.Phase);
    }

    [Fact]
    public void HeldCargoSurvivesASave()
    {
        var job = new Consignment { Mission = new Mission("a", 0, 3, Box, 120), Integrity = 0.7 };
        var s = new SaveData { Hold = new() { job }, Course = 3 };
        var back = SaveData.FromJson(s.ToJson())!;
        Assert.Equal(job.Mission, back.Hold.Single().Mission);
        Assert.Equal(0.7, back.Hold.Single().Integrity);
        Assert.Equal(3, back.Course);
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], 3, back.Hold, Unseen(g), 1, ship: new ShipState(0.4, 0.5, 100));
        Assert.Equal(0.7, v.CargoIntegrity);
        Assert.Equal(0.4, v.Fuel);
        Assert.Equal(0.5, v.Hull);
        Assert.Equal(100, v.LifeRemaining);
    }

    [Fact]
    public void FuelComesOutOfTheStationsStockAtItsPrice()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        var m = s.MarketIn(g);
        var fuel = Wares.ShipFuel;
        var stock = m.StockOf(g, s.PortStar, fuel);
        s.HeldFuel = 0.5; s.Credits = 1000;
        var (mass, cost) = Trade.FuelQuote(s, g, m);
        Assert.Equal(Ships.Courier.Tank - 0.5, mass, 9);
        Trade.BuyFuel(s, g, m);
        Assert.Equal(Ships.Courier.Tank, s.HeldFuel, 9);
        Assert.Equal(1000 - cost, s.Credits, 9);
        Assert.Equal(stock - Trade.FuelUnits(mass), m.StockOf(g, s.PortStar, fuel), 9);
        // Short of credits: as much as they pay for.
        s.HeldFuel = 0.5; s.Credits = 10;
        Trade.BuyFuel(s, g, m);
        Assert.InRange(s.HeldFuel, 0.5 + 1e-6, Ships.Courier.Tank - 1e-6);
        Assert.Equal(0, s.Credits, 6);
        // Out of stock: nothing to buy, whatever the credits.
        m.Take(g, s.PortStar, fuel, 1e9);
        s.HeldFuel = 0.5; s.Credits = 1000;
        Trade.BuyFuel(s, g, m);
        Assert.Equal(0.5, s.HeldFuel, 9);
        Assert.Equal(1000, s.Credits, 9);
    }

    [Fact]
    public void LifeSupportAndRepairsStopAtWhatYouCanAfford()
    {
        var s = new SaveData { HeldLife = 100, Credits = 10 };
        Supply.BuyLife(s);
        Assert.Equal(200, s.HeldLife, 9);
        Assert.Equal(0, s.Credits, 9);
    }

    [Fact]
    public void BankruptcyMeansTheFuelToLeaveCannotBeBought()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        (s.HeldFuel, s.Credits) = (0.1, 0);
        Assert.True(Supply.Bankrupt(s, g));
        (s.HeldFuel, s.Credits) = (0.1, 100);
        Assert.False(Supply.Bankrupt(s, g));
        (s.HeldFuel, s.Credits) = (1.0, 0);
        Assert.False(Supply.Bankrupt(s, g));
    }

    [Fact]
    public void WaitingAtThePortCostsNoLifeSupport()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1, 0, new ShipState(Ships.Courier.Tank, 1, 100));
        v.AdvanceSublight(300, Vec3d.Zero);
        Assert.True(v.Docked);
        Assert.Equal(100, v.LifeRemaining, 6);
        Assert.Equal(300, v.Time, 6);
        Assert.True((v.LocalStation!.Position(v.AbsoluteTime) - v.SubPosition).Length < Tuning.DockRange);

        v.AdvanceSublight(1, v.SubVelocity.Normalized());   // the first burn, prograde, leaves the port
        Assert.False(v.Docked);
        v.AdvanceSublight(10, Vec3d.Zero);
        Assert.Equal(89, v.LifeRemaining, 6);   // 1 day of burn and 10 of coasting
    }

    [Fact]
    public void LeavingByFtlEndsTheDockedState()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.AdvanceSublight(20, Vec3d.Zero);
        v.EnterFtl();
        v.Burn(new Vec3d(0, 1, 0), 0.5);
        v.Advance(5, 0, false);
        Assert.False(v.Docked);
        Assert.Equal(Tuning.LifeSupportDays - 5, v.LifeRemaining, 6);
    }

    [Fact]
    public void UndockingStartsLifeSupportAndTheShipCanTieBackUp()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1, 0, new ShipState(Ships.Courier.Tank, 1, 100));
        Assert.False(v.RequestDock());              // already docked
        v.Undock();
        Assert.True(v.CanDock);
        v.AdvanceSublight(5, Vec3d.Zero);
        Assert.Equal(95, v.LifeRemaining, 6);
        Assert.Equal(Phase.Sublight, v.Phase);      // never left, so no automatic docking
        Assert.True(v.RequestDock());
        Assert.Equal(Phase.Arrived, v.Phase);
    }

    [Fact]
    public void RestockOnlyWorksDockedAndKeepsLifeAccounting()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1, 0, new ShipState(0.5, 0.5, 100));
        v.AdvanceSublight(20, Vec3d.Zero);
        v.Restock(new ShipState(1.5, 1, 600), v.Hold);
        Assert.Equal(1.5, v.Fuel);
        Assert.Equal(1, v.Hull);
        Assert.Equal(600, v.LifeRemaining, 6);
        v.Undock();
        v.AdvanceSublight(10, Vec3d.Zero);
        Assert.Equal(590, v.LifeRemaining, 6);
        v.Restock(new ShipState(0, 0, 0), v.Hold);
        Assert.Equal(1.5, v.Fuel);
    }

    [Fact]
    public void DistantStationDoesNotDock()
    {
        var g = Galaxy.Generate(5);
        var v = new Voyage(g, g.Ports[0], g.Ports[1], Box, Unseen(g), 1);
        v.AdvanceSublight(10, Vec3d.Zero);
        Assert.Equal(Phase.Sublight, v.Phase);
    }
}

using Starbearer.Sim;
using Xunit;

/// <summary>Hulls, the shipyard, and the fleet: buying, boarding, selling, and what each class flies like.</summary>
public partial class SimTests
{
    [Fact]
    public void ABigHullDragsHarderAndPushesLessButKeepsItsDeltaV()
    {
        var g = WithPorts();
        Voyage Fly(ShipClass c)
        {
            var v = new Voyage(g, From, 1, Array.Empty<Consignment>(), Unseen(g), 1, shipClass: c);
            v.EnterFtl();
            v.PlaceForTest(new Vec3d(4, 0, 0), new Vec3d(0.5, 0, 0));
            return v;
        }
        var (small, big) = (Fly(Ships.Courier), Fly(Ships.Tanker));
        Assert.Equal(small.DeltaVRemaining, big.DeltaVRemaining, 9);   // dry mass and tank grow together
        small.Advance(2, Vec3d.Zero, false);
        big.Advance(2, Vec3d.Zero, false);
        Assert.True(big.Velocity.Length < small.Velocity.Length);
        var (s2, b2) = (Fly(Ships.Courier), Fly(Ships.Tanker));
        s2.Advance(0.1, new Vec3d(0, 1, 0), false);
        b2.Advance(0.1, new Vec3d(0, 1, 0), false);
        Assert.Equal(Ships.Tanker.Thrust, b2.Velocity.Y / s2.Velocity.Y, 2);
    }

    static double EmptyDeltaV(ShipClass c) => Tuning.ExhaustVelocity * Math.Log((c.DryMass + c.Tank) / c.DryMass);

    [Fact]
    public void AMixedHullCarriesEveryKindButLessThanASpecialistOfItsSize()
    {
        foreach (var size in new[] { "small", "medium" })
        {
            var mixed = Ships.Get($"mixed-{size}");
            Assert.Equal($"mixed-{size}", mixed.Id);
            var specialists = Ships.All.Where(c => c.Id.EndsWith(size) && c != mixed || size == "small" && c == Ships.Courier).ToList();
            Assert.Equal(4, specialists.Count);
            foreach (var c in specialists)
            {
                Assert.True(mixed.Capacity < c.Capacity);
                Assert.True(EmptyDeltaV(mixed) < EmptyDeltaV(c));
                Assert.Equal(c.Drag, mixed.Drag);
            }
            Assert.True(mixed.Price > specialists.Where(c => !c.Id.StartsWith("gas")).Max(c => c.Price));
            var bays = new CargoBays(mixed, Array.Empty<int>());
            Assert.All(new[] { CargoKind.Container, CargoKind.Bulk, CargoKind.Fluid }, k => Assert.True(bays.RoomFor(k) > 0));
        }
        Assert.DoesNotContain(Ships.All, c => c.Id == "mixed-large");
    }

    [Fact]
    public void AGasCarrierIsBuiltPressurizedAndPressureWeighs()
    {
        var gas = Ships.Get("gas-small");
        var tanker = Ships.Get("tanker-small");
        var built = new CargoBays(gas, Array.Empty<int>());
        Assert.True(built.Fits(new[] { Load(CargoKind.PressurizedFluid, 8) }));
        Assert.True(built.Fits(new[] { Load(CargoKind.Fluid, 8) }));
        Assert.Empty(built.Unpressurized);                       // nothing left to fit a pressurizer to
        Assert.True(gas.DryMass > tanker.DryMass);
        var fitted = new OwnedShip { Class = tanker.Id, Pressurized = { 0, 1 } };
        Assert.Equal(gas.DryMass, fitted.Fitted.DryMass, 9);     // a tanker pressurized throughout weighs the same
        Assert.Equal(tanker.DryMass, new OwnedShip { Class = tanker.Id }.Fitted.DryMass, 9);
    }

    [Fact]
    public void ALargeHullPassesAGate()
    {
        var g = Galaxy.Generate(7);
        var gate = g.Gates[0];
        var v = new Voyage(g, gate, g.StarOf(gate), Array.Empty<Consignment>(), Unseen(g), 1, shipClass: Ships.Bulker);
        v.Undock();
        v.BookGate(10);
        Assert.NotNull(v.GateBooked);
        Assert.All(Ships.All, c => Assert.True(c.Gates));
    }

    [Fact]
    public void EveryShipyardBuildsTheSmallShipAndEveryHullIsBuiltSomewhere()
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var g = Galaxy.Generate(seed);
            var yards = g.Ports.Where(p => g.KindOf(p) == StationKind.Shipyard).ToList();
            Assert.All(yards, y => Assert.Contains(Ships.Courier, Ships.SoldAt(g, y)));
            Assert.All(Ships.All, c => Assert.Contains(yards, y => Ships.SoldAt(g, y).Contains(c)));
            Assert.Empty(Ships.SoldAt(g, g.Depots[0]));
        }
    }

    static (SaveData S, Galaxy G, ShipClass Sold) AtAShipyard()
    {
        var g = Galaxy.Generate(5);
        var yard = g.Ports.First(p => g.KindOf(p) == StationKind.Shipyard && Ships.SoldAt(g, p).Length > 1);
        var s = new SaveData { Seed = 5, Credits = 10000 };
        s.PortStation = yard;
        return (s, g, Ships.SoldAt(g, yard)[1]);
    }

    [Fact]
    public void ABoughtShipWaitsWhereItWasBuiltAndTheOneLeftKeepsItsCargo()
    {
        var (s, g, sold) = AtAShipyard();
        var job = new Consignment { Mission = new Mission("a", s.PortStation, 0, Box, 100) };
        s.Hold.Add(job);
        Assert.True(Hangar.Buy(s, g, sold));
        Assert.Equal(10000 - sold.Price, s.Credits);
        var bought = s.Fleet.Single(x => x.Id != s.Flown);
        Assert.Equal(s.PortStation, bought.Station);
        Assert.Equal(sold.Tank, bought.Fuel);
        Assert.True(Hangar.Board(s, bought.Id));
        Assert.Equal(sold, s.Ship.Type);
        Assert.Empty(s.Hold);                                          // the new ship's bays
        Assert.Same(job, s.Fleet.Single(x => x.Id != s.Flown).Hold.Single());   // still in the old one
        var back = SaveData.FromJson(s.ToJson())!;
        Assert.Equal(2, back.Fleet.Count);
        Assert.Equal(sold.Id, back.Ship.Class);
        Assert.Equal(s.PortStation, back.PortStation);
    }

    [Fact]
    public void AShipCanOnlyBeBoardedOrSoldWhereItIs()
    {
        var (s, g, sold) = AtAShipyard();
        Hangar.Buy(s, g, sold);
        var other = s.Fleet.Single(x => x.Id != s.Flown);
        other.Station = g.Depots[0];
        Assert.False(Hangar.Board(s, other.Id));
        Assert.False(Hangar.Sell(s, g, other.Id));
        other.Station = s.PortStation;
        other.Hold.Add(new Consignment { Mission = new Mission("a", 0, 1, Box, 100) });
        Assert.NotNull(Hangar.SaleRefusal(s, g, other));                // not with cargo aboard
        other.Hold.Clear();
        Assert.NotNull(Hangar.SaleRefusal(s, g, s.Ship));              // nor the one flown
        var credits = s.Credits;
        Assert.True(Hangar.Sell(s, g, other.Id));
        Assert.Equal(credits + sold.Price / 2, s.Credits, 6);
        Assert.Single(s.Fleet);
    }

    [Fact]
    public void BankruptcyCountsOnlyTheShipsDockedHere()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        (s.HeldFuel, s.Credits) = (0, 0);
        Assert.True(Supply.Bankrupt(s, g));
        var spare = new OwnedShip { Id = 2, Station = g.Ports[1], Fuel = Ships.Courier.Tank };
        s.Fleet.Add(spare);
        Assert.True(Supply.Bankrupt(s, g));    // out of reach
        spare.Station = s.PortStation;
        Assert.False(Supply.Bankrupt(s, g));
    }
}

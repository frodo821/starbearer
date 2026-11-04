using Xunit;
using Starbearer.Sim;

/// <summary>Ships left in orbit: where they are later, what they are saved as, and which orbits will hold one.</summary>
public class ParkingTests
{
    static (Galaxy G, Center Body, Vec3d P, Vec3d V) Orbit(double eccentricityKick = 0.1)
    {
        var g = Galaxy.Generate(3);
        var body = g.System(g.StarOf(g.Ports[0])).Tree.First(c => c.Parent != null && c.Kind != BodyKind.Moon);
        var r = body.Edge * 0.2;
        var p = new Vec3d(r, 0, 0);
        var v = new Vec3d(0, 0, Math.Sqrt(body.Mu / r) * (1 + eccentricityKick));
        return (g, body, p, v);
    }

    [Fact]
    public void AParkedShipCoastsOnItsOrbit()
    {
        var (g, body, p, v) = Orbit();
        var orbit = new ParkedOrbit(body, p, v, 100, 7);
        var (q, w) = (p, v);
        Sublight.Kepler(body.Mu, ref q, ref w, 37.5);
        Assert.True((orbit.Position(137.5) - q).Length < 1e-9 * p.Length);
        Assert.True((orbit.Velocity(137.5) - w).Length < 1e-9 * v.Length);
        // A whole lap later it is back; before its epoch it is where a lap earlier would have it.
        Assert.True((orbit.Position(100 + orbit.Period) - p).Length < 1e-6 * p.Length);
        Assert.True((orbit.Position(100 - orbit.Period) - p).Length < 1e-6 * p.Length);
        Assert.Equal(body.Star, orbit.Star);
        Assert.True(Math.Abs(orbit.PlaneNormal.Dot(p.Cross(v).Normalized()) - 1) < 1e-12);
    }

    [Fact]
    public void AParkedShipIsSavedWithItsOrbit()
    {
        var (g, body, p, v) = Orbit();
        var ship = new OwnedShip { Id = 2, Station = -1, Parked = ParkedState.Of(body, p, v, 42) };
        var s = new SaveData { Fleet = { ship } };
        var back = SaveData.FromJson(s.ToJson())!.Fleet.Single(x => x.Id == 2);
        Assert.Equal(-1, back.Station);
        var orbit = back.Parked!.Orbit(g, 2);
        Assert.Same(body, orbit.Host);
        Assert.True((orbit.Position(42) - p).Length < 1e-15);
        Assert.Null(SaveData.FromJson(new SaveData().ToJson())!.Ship.Parked);   // a ship at a station has none
    }

    [Fact]
    public void OnlyAClosedOrbitClearOfTheBodyHoldsAParkedShip()
    {
        var (_, body, p, v) = Orbit(0);
        Assert.Null(Parking.Refusal(body, p, v));
        Assert.NotNull(Parking.Refusal(body, p, v * 1.5));                       // escaping
        Assert.NotNull(Parking.Refusal(body, p, v * 1.38));                      // bound, but out past its reach
        Assert.NotNull(Parking.Refusal(body, p, v * 0.1));                       // falling into it
    }
}

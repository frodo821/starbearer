using Starbearer.Sim;
using Xunit;

/// <summary>Generation: regions, stations, gates between regions, systems and their bodies.</summary>
public partial class SimTests
{
    [Fact]
    public void EveryRegionHasTheStationsItIsPromised()
    {
        var counts = new HashSet<int>();
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var g = Galaxy.Generate(seed);
            Assert.Equal(Tuning.RegionCount, g.Regions.Length);
            Assert.Equal(g.Regions.Length, g.Regions.Select(r => r.Name).Distinct().Count());
            foreach (var region in g.Regions)
            {
                Assert.InRange(g.Stars.Count(s => s.Region == region.Id), Tuning.StarsPerRegion / 3, Tuning.StarsPerRegion * 3);
                var kinds = g.Ports.Concat(g.Depots).Where(p => g.StarAt(p).Region == region.Id).Select(p => g.KindOf(p)).ToList();
                Assert.True(kinds.Count(k => k < StationKind.Depot) >= 2, "two production stations");
                Assert.Contains(StationKind.Colony, kinds);
                Assert.Contains(StationKind.Shipyard, kinds);
                counts.Add(kinds.Count);
            }
            Assert.All(g.Ports, p => Assert.NotEqual(StationKind.Depot, g.KindOf(p)));
            Assert.All(g.Depots, p => Assert.Equal(StationKind.Depot, g.KindOf(p)));
        }
        Assert.True(counts.Count > 1, "the number of stations varies from region to region");
        var g7 = Galaxy.Generate(7);
        // A region is one stretch of the disc: each star is nearer its own region's centre than any other's.
        Assert.All(g7.Stars, s => Assert.Equal(s.Region, g7.Regions.MinBy(r => (r.Center - s.Position).Length)!.Id));
    }

    [Fact]
    public void EachRegionHasHubsOfStationsThatDealWithEachOther()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var g = Galaxy.Generate(seed);
            foreach (var region in g.Regions)
            {
                var hubs = g.Stars.Where(s => s.Region == region.Id && g.StationsIn(s.Id).Count(x => g.KindOf(x) != StationKind.Fortress) > 1).ToList();
                Assert.InRange(hubs.Count, 1, 3);
                foreach (var hub in hubs)
                {
                    var kinds = g.StationsIn(hub.Id).Select(g.KindOf).ToList();
                    Assert.True(kinds.Count <= 4, "at most four stations");
                    Assert.Equal(kinds.Count, kinds.Distinct().Count());   // told apart by what they are
                    Assert.True(g.GateIn(hub.Id) < 0, "never a gate's system");
                }
            }
        }
    }

    [Fact]
    public void AShipDealsWithTheStationItsJobIsBoundForInASystemOfSeveral()
    {
        var g = Galaxy.Generate(2);
        var hub = g.Stars.First(s => g.StationsIn(s.Id).Count(x => g.KindOf(x) != StationKind.Fortress) > 1).Id;
        var (a, b) = (g.StationsIn(hub).First(), g.StationsIn(hub).Last());
        // Leaving one with a job for the other, the other is the one to rendezvous with.
        var job = new Consignment { Mission = new Mission("x", a, b, Box, 10) };
        var v = new Voyage(g, a, hub, new[] { job }, Unseen(g), 1);
        Assert.Equal(b, v.LocalStation!.Id);
        Assert.Equal(g.StarOf(a), v.SystemStar);
        // Picking the first again as target makes it the one dealt with.
        v.SetGoal(g.StationAt(a));
        Assert.Equal(a, v.LocalStation!.Id);
    }

    [Fact]
    public void ACompanyStartsInAHubWithItsFirstJobNextDoor()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var (s, g) = SaveData.NewGalaxy(seed);
            var here = g.StarOf(s.PortStation);
            Assert.True(g.StationsIn(here).Count() > 1, "a hub");
            Assert.False(g.Claim(here).Contested);
            Assert.DoesNotContain(g.StationsIn(here), x => g.KindOf(x) is StationKind.Capital or StationKind.Fortress);
            Assert.True(g.GateIn(here) < 0);
            var first = Missions.OffersFor(s, g)[0];
            Assert.Equal(here, g.StarOf(first.Target));
            Assert.NotEqual(s.PortStation, first.Target);
            // Only the first board: after a delivery, the first offer goes anywhere.
            s.Deliveries = 1;
            Assert.Contains(Missions.OffersFor(s, g), m => g.StarOf(m.Target) != here);
        }
    }

    [Fact]
    public void GatesPairRegionsSoAllCanBeReached()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var g = Galaxy.Generate(seed);
            Assert.All(g.Gates.Where(g.Alive), gate =>
            {
                var exit = g.GateLinks[gate];
                Assert.Equal(gate, g.GateLinks[exit]);
                Assert.NotEqual(g.StarAt(gate).Region, g.StarAt(exit).Region);
                Assert.Equal(StationKind.Gate, g.KindOf(gate));
                Assert.Empty(Missions.Board(g, Market.Open(g), gate, 0, Array.Empty<Consignment>()));
                Assert.DoesNotContain(gate, g.Ports.Concat(g.Depots));
                Assert.True(g.IsStation(gate) && g.HasStation(g.StarOf(gate)));
            });
            var seen = new HashSet<int> { 0 };
            var queue = new Queue<int>(seen);
            while (queue.Count > 0)
                foreach (var next in g.LinkedRegions(queue.Dequeue()))
                    if (seen.Add(next)) queue.Enqueue(next);
            Assert.Equal(g.Regions.Length, seen.Count);
        }
    }

    [Fact]
    public void SameSeedGivesSameGalaxy()
    {
        var a = Galaxy.Generate(42); var b = Galaxy.Generate(42);
        Assert.Equal(a.Stars.Select(s => s.Position), b.Stars.Select(s => s.Position));
        Assert.Equal(a.Stars.Select(s => s.Name), b.Stars.Select(s => s.Name));
        Assert.NotEqual(a.Stars[0].Position, Galaxy.Generate(43).Stars[0].Position);
    }

    [Fact]
    public void GenerationRespectsSeparationAndCount()
    {
        var g = Galaxy.Generate(7);
        Assert.Equal(Tuning.StarCount, g.Stars.Length);
        foreach (var a in g.Stars) foreach (var b in g.Stars)
            if (a.Id != b.Id) Assert.True((a.Position - b.Position).Length >= Tuning.MinStarSeparation);
        Assert.Equal(g.Ports.Length, g.Ports.Distinct().Count());
    }

    [Fact]
    public void StationsAreInclinedAndStayOnTheirCircle()
    {
        var g = Galaxy.Generate(5);
        var tilts = g.Ports.Select(p => g.StationAt(p)).ToList();
        Assert.Contains(tilts, s => Math.Abs(s.PlaneNormal.Y) < 0.998);
        foreach (var s in tilts)
        {
            Assert.Equal(s.Radius, s.Position(3.3).Length, 9);
            Assert.Equal(0, s.Position(3.3).Dot(s.Velocity(3.3)), 9);
            var (pa, va) = (s.Position(0), s.Velocity(0));
            Sublight.Propagate(s.Host!.Mu, ref pa, ref va, 4);
            Assert.True((pa - s.Position(4)).Length < 1e-3);
        }
    }

    [Fact]
    public void EverySystemHasPlanetsAndEveryStationAHost()
    {
        var g = Galaxy.Generate(5);
        foreach (var star in g.Ports.Concat(g.Depots))
        {
            var system = g.System(g.StarOf(star));
            Assert.NotEmpty(system.Children);
            Assert.All(system.Children, p => Assert.True(p.OrbitRadius + p.Edge < system.Edge));
            Assert.All(system.Children.SelectMany(p => p.Children), m => Assert.True(m.OrbitRadius + m.Edge < m.Parent!.Edge));
            var st = g.StationAt(star);
            Assert.True(system.Holds(st.Host!));
            Assert.True(st.Radius > st.Host!.BodyRadius * Tuning.HostClearance && st.Radius < st.Host!.Edge);
            Assert.True(st.Radius > st.Host!.BodyRadius);
        }
        Assert.Contains(Enumerable.Range(1, 8).Select(i => Galaxy.Generate((ulong)i)).SelectMany(x => x.Ports.Select(x.StationAt)), s => s.Host!.Kind == BodyKind.Star);
    }

    [Fact]
    public void SpheresOfInfluenceNeverOverlap()
    {
        int stars = 0, planets = 0, moons = 0, belts = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var g = Galaxy.Generate(seed);
            foreach (var a in g.Stars)
                foreach (var b in g.Stars)
                    if (a.Id < b.Id && (a.Position - b.Position).Length < a.Soi + b.Soi) stars++;
            foreach (var star in g.Stars)
            {
                var system = g.System(star.Id);
                bool Clash(Center x, Center y) => x.OrbitRadius < y.OrbitRadius && x.OrbitRadius + x.Edge > y.OrbitRadius - y.Edge;
                planets += system.Children.Sum(x => system.Children.Count(y => Clash(x, y)));
                moons += system.Children.Sum(p => p.Children.Sum(x => p.Children.Count(y => Clash(x, y))));
            }
            foreach (var port in g.Ports.Concat(g.Depots))
            {
                var st = g.StationAt(port);
                if (st.Host!.Kind == BodyKind.Star && g.System(g.StarOf(port)).Children.Any(c => Math.Abs(c.OrbitRadius - st.Radius) < c.Edge)) belts++;
            }
        }
        Assert.True(stars + planets + moons + belts == 0, $"stars {stars} planets {planets} moons {moons} belts {belts}");
    }
}

public partial class SimTests
{
    [Fact]
    public void ASystemCanHoldSeveralStations()
    {
        const int Second = Galaxy.FirstStationId + 2;
        var g = WithPorts();
        g = new Galaxy
        {
            Seed = g.Seed, Stars = g.Stars, Nebulae = g.Nebulae, Ports = new[] { From, To, Second },
            StationStars = new() { [From] = 0, [To] = 1, [Second] = 0 },
            Kinds = new() { [From] = StationKind.City, [To] = StationKind.City, [Second] = StationKind.Shipyard },
        };
        Assert.Equal(new[] { From, Second }, g.StationsIn(0));
        Assert.Equal(" ◆▲", Missions.Mark(g, 0));
        Assert.NotEqual(g.SeedKey(From), g.SeedKey(Second));
        Assert.Equal(0UL, g.SeedKey(From));                       // the first keeps its system's look
        var (a, b) = (g.StationAt(From), g.StationAt(Second));
        Assert.Equal(0, a.Star); Assert.Equal(0, b.Star);
        Assert.True(a.Radius != b.Radius || a.Phase != b.Phase);
        // A ship deals with the station it set out from, and with another once it picks it.
        var v = new Voyage(g, Second, 1, Box, Unseen(g), 1);
        Assert.Equal(Second, v.LocalStation!.Id);
        v.SetGoal(a);
        Assert.Equal(From, v.LocalStation!.Id);
        Assert.Equal(StationKind.Shipyard, g.KindOf(Second));
        Assert.NotEmpty(Ships.SoldAt(g, Second));
        Assert.Empty(Ships.SoldAt(g, From));
    }
}

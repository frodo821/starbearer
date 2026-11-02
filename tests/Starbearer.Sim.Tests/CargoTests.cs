using Starbearer.Sim;
using Xunit;

/// <summary>Cargo kinds, bays and pressurizers, the mission board and the hold.</summary>
public partial class SimTests
{
    [Fact]
    public void ABoardHaulsSpareStockToStationsShortOfIt()
    {
        var g = Galaxy.Generate(5);
        var m = Market.Open(g);
        m.AdvanceTo(g, 120);   // let the shortages show
        var wareJobs = 0;
        foreach (var port in g.Ports.Concat(g.Depots))
            foreach (var job in Missions.Board(g, m, port, 0, Array.Empty<Consignment>()))
            {
                Assert.Equal(port, job.Origin);
                Assert.NotEqual(job.Origin, job.Target);
                Assert.True(g.HasStation(job.Target));
                Assert.True(job.Pay > 0);
                if (job.Cargo.Ware is not { } ware) continue;
                wareJobs++;
                var w = Wares.Get(ware);
                Assert.True(g.Economies[port].Makes(ware));
                Assert.True(g.Economies[job.Target].Uses(ware));
                Assert.True(m.Fill(g, job.Target, ware) < 0.8);
                Assert.True(m.StockOf(g, port, ware) >= job.Cargo.Volume);
                Assert.Equal(w.Kind, job.Cargo.Kind);
                Assert.Equal(w.Mass * job.Cargo.Volume, job.Cargo.Mass, 9);
                Assert.True(job.Pay >= job.Cargo.Volume * w.Price * 0.2);
            }
        Assert.True(wareJobs > 10, $"{wareJobs} ware jobs");
        var board = Missions.Board(g, m, g.Ports[0], 0, Array.Empty<Consignment>());
        Assert.NotEqual(board.Select(x => x.Id), Missions.Board(g, m, g.Ports[0], 1, Array.Empty<Consignment>()).Select(x => x.Id));
    }

    /// <summary>A docked save whose board has a ware job a courier can take.</summary>
    static (SaveData S, Galaxy G, Mission Job) WithAWareJob()
    {
        var (s, g) = SaveData.NewGalaxy(5);
        s.TotalDays = 120;
        foreach (var port in g.Ports)
        {
            s.PortStar = port;
            if (Missions.OffersFor(s, g).FirstOrDefault(j => j.Cargo.Ware != null && Missions.Fits(s, j)) is { } job) return (s, g, job);
        }
        throw new InvalidOperationException("no ware job a courier can take");
    }

    [Fact]
    public void TakingAWareJobLoadsItFromStockAndDeliveringFillsTheStationShortOfIt()
    {
        var (s, g, job) = WithAWareJob();
        var m = s.MarketIn(g);
        var ware = job.Cargo.Ware!;
        var (origin, target) = (m.StockOf(g, job.Origin, ware), m.StockOf(g, job.Target, ware));
        var others = Missions.OffersFor(s, g).Where(j => j != job).ToList();
        Assert.True(Missions.Take(s, g, job));
        Assert.Equal(origin - job.Cargo.Volume, m.StockOf(g, job.Origin, ware), 9);
        Assert.Equal(others, Missions.OffersFor(s, g));            // the rest of the board stays put
        var back = SaveData.FromJson(s.ToJson())!;
        Assert.Equal(others, Missions.OffersFor(back, g));         // and a reload shows the same
        var c = s.Hold.Single(x => x.Mission == job);
        c.Integrity = 0.5;
        Missions.Deliver(s, g, c);
        Assert.Equal(target + job.Cargo.Volume * 0.5, m.StockOf(g, job.Target, ware), 9);
        s.TotalDays += 10;                                         // a new day posts a new board
        Assert.NotEqual(others, Missions.OffersFor(s, g));
    }

    [Fact]
    public void AHoldTakesJobsUntilItsVolumeRunsOut()
    {
        var cargo = new Cargo("crate", 0.5, 0.9, 100, 3);
        Mission Job(int i) => new($"{i}", 0, 1, cargo, 100);
        var s = new SaveData();
        var g = Galaxy.Generate(5);
        Assert.True(Missions.Take(s, g, Job(0)));
        Assert.True(Missions.Take(s, g, Job(1)));
        Assert.False(Missions.Take(s, g, Job(2)));
        Assert.Equal(6, Missions.VolumeUsed(s.Hold));
        var board = Missions.Board(g, Market.Open(g), g.Ports[0], 0, Array.Empty<Consignment>());
        var taken = new[] { new Consignment { Mission = board[0] } };
        Assert.DoesNotContain(Missions.Board(g, Market.Open(g), g.Ports[0], 0, taken), m => m.Id == board[0].Id);
    }

    static readonly ShipClass Tanker = Ships.Courier with { Id = "test", Bays = new[] { new Bay(BayKind.Fluid, 4), new Bay(BayKind.Fluid, 4), new Bay(BayKind.Bulk, 3), new Bay(BayKind.Container, 2) } };
    static Cargo Load(CargoKind kind, double volume) => new($"{kind}", 0.5, 0.9, 100, volume, kind);

    [Fact]
    public void ABayCarriesOnlyItsOwnKind()
    {
        var courier = new CargoBays(Ships.Courier, Array.Empty<int>());
        Assert.True(courier.Fits(new[] { Load(CargoKind.Container, 8) }));
        foreach (var kind in new[] { CargoKind.Bulk, CargoKind.Fluid, CargoKind.PressurizedFluid })
        {
            Assert.False(courier.Fits(new[] { Load(kind, 1) }));
            Assert.NotNull(courier.Refusal(Array.Empty<Consignment>(), Load(kind, 1)));
        }
        var tanker = new CargoBays(Tanker, Array.Empty<int>());
        Assert.True(tanker.Fits(new[] { Load(CargoKind.Fluid, 8), Load(CargoKind.Bulk, 3), Load(CargoKind.Container, 2) }));
        Assert.False(tanker.Fits(new[] { Load(CargoKind.Bulk, 4) }));            // the fluid bays' room is no use to it
        Assert.False(tanker.Fits(new[] { Load(CargoKind.Container, 3) }));
        Assert.Equal("加圧モジュールがない", tanker.Refusal(Array.Empty<Consignment>(), Load(CargoKind.PressurizedFluid, 1)));
    }

    [Fact]
    public void APressurizedFluidBayTakesBothKindsOfFluid()
    {
        var one = new CargoBays(Tanker, new[] { 1 });
        Assert.True(one.Fits(new[] { Load(CargoKind.PressurizedFluid, 4), Load(CargoKind.Fluid, 4) }));
        Assert.False(one.Fits(new[] { Load(CargoKind.PressurizedFluid, 5) }));                              // only one bay is pressurized
        Assert.False(one.Fits(new[] { Load(CargoKind.PressurizedFluid, 2), Load(CargoKind.Fluid, 7) }));   // they share the fluid bays
        Assert.True(one.Fits(new[] { Load(CargoKind.Fluid, 8) }));                                          // plain fluid goes in either
        Assert.True(new CargoBays(Tanker, new[] { 0, 1 }).Fits(new[] { Load(CargoKind.PressurizedFluid, 8) }));
    }

    [Fact]
    public void APressurizerIsFittedPerFluidBayAndCountsInTheToll()
    {
        var courier = new SaveData { Credits = 1000 };
        Assert.False(Modules.FitPressurizer(courier));   // no fluid bay to fit it to
        Assert.Equal(1000, courier.Credits);
        var s = new SaveData { Credits = 1000 };
        var (v, _, _) = AtGate();
        var bare = Gates.Toll(s, v);
        s.Pressurized.Add(0);
        Assert.Equal(Math.Round(Tuning.GateTollRate * (Ships.Courier.Price + Modules.PressurizerPrice)), Gates.Toll(s, v));
        Assert.True(Gates.Toll(s, v) > bare);
        var back = SaveData.FromJson(s.ToJson())!;
        Assert.Equal(new[] { 0 }, back.Pressurized);
        Assert.Equal(Ships.Courier.Id, back.Ship.Class);
    }

    [Fact]
    public void EveryBoardOpensWithAJobInContainers()
    {
        var g = Galaxy.Generate(5);
        var kinds = new HashSet<CargoKind>();
        foreach (var port in g.Ports.Concat(g.Depots))
            for (int d = 0; d < 4; d++)
            {
                var board = Missions.Board(g, Market.Open(g), port, d, Array.Empty<Consignment>());
                if (board.Count == 0) continue;   // a gate
                Assert.Equal(CargoKind.Container, board[0].Cargo.Kind);
                kinds.UnionWith(board.Select(m => m.Cargo.Kind));
            }
        Assert.Equal(4, kinds.Count);
    }

    [Fact]
    public void EachLoadWeighsOnTheShipAndTakesItsOwnHeat()
    {
        var g = Galaxy.Generate(5);
        Consignment Load(string id, double heat) => new() { Mission = new Mission(id, g.Ports[0], g.Ports[1], new Cargo(id, 0.5, heat, 100), 100) };
        var one = new Voyage(g, g.Ports[0], g.Ports[1], new[] { Load("a", 0.4) }, Unseen(g), 1);
        var two = new Voyage(g, g.Ports[0], g.Ports[1], new[] { Load("a", 0.4), Load("b", 0.9) }, Unseen(g), 1);
        Assert.Equal(one.Mass + 0.5, two.Mass, 9);
        Assert.True(two.DeltaVRemaining < one.DeltaVRemaining);
        Assert.Equal(0.4, two.HeatTolerance);
    }

    [Fact]
    public void DroppingAJobCostsAPartOfItsPay()
    {
        var job = new Consignment { Mission = new Mission("a", 0, 1, Box, 100) };
        var s = new SaveData { Credits = 40, Hold = new() { job } };
        Missions.Drop(s, job);
        Assert.Empty(s.Hold);
        Assert.Equal(40 - 100 * Tuning.DropPenalty, s.Credits, 9);
        var broke = new SaveData { Credits = 5, Hold = new() { job } };
        Missions.Drop(broke, job);
        Assert.Equal(0, broke.Credits);
    }
}

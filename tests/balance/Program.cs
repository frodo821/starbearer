using Starbearer.Sim;

// Flies many contracts with the autopilot and prints whether the routing problem has teeth, hull by hull.
// Each hull carries cargo in proportion to its dry mass, so its delta-v matches the small ship's and only
// drag and thrust tell them apart. Arguments: class ids to fly (default all), then --seeds N (default 30).
var ids = args.TakeWhile(a => !a.StartsWith("--")).ToList();
var seeds = args.SkipWhile(a => a != "--seeds").Skip(1).Select(ulong.Parse).DefaultIfEmpty(30UL).First();
var classes = ids.Count == 0 ? Ships.All : ids.Select(Ships.Get).ToArray();
double Pct(List<double> v, double q) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? double.NaN : s[(int)((s.Count - 1) * q)]; }

foreach (var ship in classes)
{
    var cargo = new Cargo("Sealed containers", 0.5 * ship.DryMass, 0.8, 100);
    int n = 0, ok = 0; var fuel = new List<double>(); var days = new List<double>(); var legs = new List<double>();
    var notes = new Dictionary<string, int>();
    int directImpossible = 0; var ratios = new List<double>();
    for (ulong seed = 1; seed <= seeds; seed++)
    {
        var g = Galaxy.Generate(seed);
        for (int a = 0; a + 1 < g.Ports.Length; a++)
        {
            int o = g.Ports[a], t = g.Ports[a + 1];
            if (g.StarAt(o).Region != g.StarAt(t).Region) continue;   // jobs stay in their region
            n++;
            var r = Autopilot.Fly(g, o, g.StarOf(t), cargo, ship: ship);
            var key = r.Delivered ? "delivered" : $"{r.Death}/{(r.Note.StartsWith("overshot") ? "overshot" : r.Note)}";
            notes[key] = notes.GetValueOrDefault(key) + 1;
            if (r.Delivered) { ok++; fuel.Add(r.FuelLeft / ship.Tank); days.Add(r.Days); legs.Add(r.Legs); }

            var direct = Autopilot.SpeedForLeg(g, g.StarAt(o).Position, g.StarAt(t).Position, 1.1, ship.Drag);
            var budget = Tuning.ExhaustVelocity * Math.Log((ship.DryMass + cargo.Mass + ship.Tank) / (ship.DryMass + cargo.Mass));
            ratios.Add(direct / budget);
            if (direct > budget) directImpossible++;
        }
    }
    Console.WriteLine($"== {ship.Name} ({ship.Id})  drag x{ship.Drag} thrust x{ship.Thrust}");
    Console.WriteLine($"contracts {n}, delivered {ok} ({100.0 * ok / n:F0}%)");
    foreach (var kv in notes.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Key}: {kv.Value}");
    Console.WriteLine($"fuel left (of tank) p10/50/90: {Pct(fuel, .1):F2} {Pct(fuel, .5):F2} {Pct(fuel, .9):F2}");
    Console.WriteLine($"days       p10/50/90: {Pct(days, .1):F0} {Pct(days, .5):F0} {Pct(days, .9):F0}");
    Console.WriteLine($"legs p50/90: {Pct(legs, .5):F0} {Pct(legs, .9):F0}");
    Console.WriteLine($"direct flight beyond tank: {directImpossible}/{n}; direct speed / budget p50 {Pct(ratios, .5):F2} p90 {Pct(ratios, .9):F2}");
}

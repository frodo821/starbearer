using Starbearer.Sim;

// Flies many contracts with the autopilot and prints whether the routing problem has teeth.
var cargo = new Cargo("Sealed containers", 0.5, 0.8, 100);
int n = 0, ok = 0; var fuel = new List<double>(); var days = new List<double>(); var legs = new List<int>();
var notes = new Dictionary<string, int>();
int directImpossible = 0; var ratios = new List<double>();
for (ulong seed = 1; seed <= 60; seed++)
{
    var g = Galaxy.Generate(seed);
    for (int a = 0; a + 1 < g.Ports.Length; a++)
    {
        int o = g.Ports[a], t = g.Ports[a + 1];
        if (g.Stars[o].Region != g.Stars[t].Region) continue;   // jobs stay in their region
        n++;
        var r = Autopilot.Fly(g, o, t, cargo);
        var key = r.Delivered ? "delivered" : $"{r.Death}/{(r.Note.StartsWith("overshot") ? "overshot" : r.Note)}";
        notes[key] = notes.GetValueOrDefault(key) + 1;
        if (r.Delivered) { ok++; fuel.Add(r.FuelLeft); days.Add(r.Days); legs.Add(r.Legs); }

        var direct = Autopilot.SpeedForLeg(g, g.Stars[o].Position, g.Stars[t].Position, 1.1);
        var budget = Tuning.ExhaustVelocity * Math.Log((Tuning.DryMass + cargo.Mass + Tuning.TankCapacity) / (Tuning.DryMass + cargo.Mass));
        ratios.Add(direct / budget);
        if (direct > budget) directImpossible++;
    }
}
double Pct(List<double> v, double q) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? double.NaN : s[(int)((s.Count - 1) * q)]; }
Console.WriteLine($"contracts {n}, delivered {ok} ({100.0 * ok / n:F0}%)");
foreach (var kv in notes.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Key}: {kv.Value}");
Console.WriteLine($"fuel left  p10/50/90: {Pct(fuel, .1):F2} {Pct(fuel, .5):F2} {Pct(fuel, .9):F2} of {Tuning.TankCapacity}");
Console.WriteLine($"days       p10/50/90: {Pct(days, .1):F0} {Pct(days, .5):F0} {Pct(days, .9):F0}");
Console.WriteLine($"legs mean: {(legs.Count > 0 ? legs.Average() : 0):F1}");
Console.WriteLine($"direct flight beyond tank: {directImpossible}/{n}; direct speed / budget p50 {Pct(ratios, .5):F2} p90 {Pct(ratios, .9):F2}");

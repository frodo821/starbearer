namespace Starbearer.Sim;

/// <summary>
/// Gates: what a passage costs, and what keeps one running. Each end burns fuel, and electronics and
/// fission fuel by how far the pair reaches: the field's parts and its power. An end that can't meet a
/// day of it stands still, and with either end still the pair passes neither ships nor goods.
/// </summary>
public static class Gates
{
    /// <summary>Per end, a day: fuel, and per light year the pair spans, electronics and fission fuel (before Economy.Scale).</summary>
    public const double UpkeepFuel = 0.15, PartsPerLy = 0.001, PowerPerLy = 0.001;

    public static (string, double)[] Upkeep(double span) =>
        new[] { ("fusion_fuel", UpkeepFuel), ("electronics", PartsPerLy * span), ("fission_fuel", PowerPerLy * span) };

    /// <summary>Whether a gate end has a day of its upkeep in stock.</summary>
    public static bool Powered(Galaxy g, Market m, int gate)
    {
        if (!g.Economies.TryGetValue(gate, out var e) || !m.Stock.TryGetValue(gate, out var stock)) return true;
        return e.Upkeep.All(u => stock[e.IndexOf(u.Ware)] >= u.Rate - 1e-9);
    }

    /// <summary>Notes each gate pair that stops or starts again, for the news (the lower id of the pair stands for it).</summary>
    public static void Watch(Galaxy g, Market m)
    {
        if (m.Day < 0) return;
        var w = m.War;
        foreach (var (gate, exit) in g.GateLinks.Where(x => x.Key < x.Value).OrderBy(x => x.Key))
        {
            if (!g.Alive(gate) || !g.Alive(exit)) continue;
            var open = Open(g, m, gate);
            if (open == !w.Stopped.Contains(gate)) continue;
            if (open) w.Stopped.Remove(gate); else w.Stopped.Add(gate);
            var dead = Powered(g, m, gate) ? exit : gate;
            w.Log.Add(new WarEvent(m.Day, open ? "gate up" : "gate down", open ? gate : dead, g.StarOf(open ? gate : dead), g.OwnerOf(open ? gate : dead)));
        }
    }

    /// <summary>Whether a gate pair passes ships and goods: both ends powered.</summary>
    public static bool Open(Galaxy g, Market m, int gate) =>
        g.Alive(gate) && Powered(g, m, gate) && (!g.GateLinks.TryGetValue(gate, out var exit) || g.Alive(exit) && Powered(g, m, exit));

    /// <summary>
    /// What a passage through this system's gate costs: a share of the fuel it saves, by how far the
    /// gate reaches and how heavy the ship is as it goes through (cargo and fuel aboard), more for a
    /// hull that drags more. Zero where the ship is at no gate.
    /// </summary>
    public static double Toll(Galaxy g, Voyage v)
    {
        if (v.LocalStation is not { } st || !g.GateLinks.TryGetValue(st.Id, out _)) return 0;
        var saved = Tuning.FtlFuelPerLyMass * g.GateSpan(st.Id) * v.Mass * v.Class.Drag;
        return Math.Round(Tuning.GateTollShare * Tuning.FuelPrice * saved);
    }
}

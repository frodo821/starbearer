namespace Starbearer.Sim;

public sealed record Star(int Id, string Name, Vec3d Position, double Mass, double Temperature, Vec3d ChartOffset)
{
    public double Mu => Mass * Tuning.MuPerMass;
    public double Soi => Tuning.SoiPerSqrtMass * Math.Sqrt(Mass);
    public double Radius => Tuning.StarRadiusPerSqrtMass * Math.Sqrt(Mass);
}

public sealed record Nebula(Vec3d Center, double Radius, double Density);

public sealed class Galaxy
{
    public required ulong Seed { get; init; }
    public required Star[] Stars { get; init; }
    public required Nebula[] Nebulae { get; init; }
    /// <summary>Star ids that host a port.</summary>
    public required int[] Ports { get; init; }
    /// <summary>Star ids with a small resupply station. Its board has only small jobs.</summary>
    public int[] Depots { get; init; } = Array.Empty<int>();

    /// <summary>Each port's station, derived from the seed so it never needs saving.</summary>
    public bool HasStation(int star) => Ports.Contains(star) || Depots.Contains(star);
    public Station? StationOrNull(int star) => HasStation(star) ? StationAt(star) : null;

    public Station StationAt(int star)
    {
        var r = new Rng(Seed * 1000003 + (ulong)star);
        var s = Stars[star];
        var radius = r.Range(0.7, 1.6);
        var node = r.Range(0, 2 * Math.PI);
        var tilt = r.Range(5, 28) * Math.PI / 180 * (r.Unit() < 0.5 ? -1 : 1);
        var u = new Vec3d(Math.Cos(node), 0, Math.Sin(node));
        var w = new Vec3d(-Math.Sin(node), 0, Math.Cos(node)) * Math.Cos(tilt) + new Vec3d(0, Math.Sin(tilt), 0);
        return new Station(star, radius, r.Range(0, 2 * Math.PI), Math.Sqrt(Sublight.Mu(s) / (radius * radius * radius)), u, w);
    }

    static readonly string[] Syllables = "ka ru mi to sa ne ra li on ve th al dor ys ean or tes mar qu ien sol vi ga".Split(' ');

    static string MakeName(Rng r)
    {
        var s = "";
        for (int i = 0, n = 2 + r.Index(2); i < n; i++) s += Syllables[r.Index(Syllables.Length)];
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static Galaxy Generate(ulong seed)
    {
        var r = new Rng(seed);
        var stars = new List<Star>();
        var names = new HashSet<string>();
        for (int attempts = 0; stars.Count < Tuning.StarCount && attempts < 20000; attempts++)
        {
            var p = new Vec3d(r.Range(-1, 1), r.Range(-1, 1), r.Range(-1, 1));
            if (p.LengthSq > 1) continue;
            p = new Vec3d(p.X * Tuning.GalaxyRadius, p.Y * Tuning.GalaxyThickness, p.Z * Tuning.GalaxyRadius);
            if (stars.Any(s => (s.Position - p).Length < Tuning.MinStarSeparation)) continue;
            string name;
            do name = MakeName(r); while (!names.Add(name));
            var mass = Math.Pow(r.Range(0.25, 1.25), 2.2) + 0.35;       // mostly small, few heavy
            var temp = 3000 + 4800 * Math.Pow(mass, 0.8);
            stars.Add(new Star(stars.Count, name, p, mass, temp, r.NormalVec() * Tuning.ChartErrorLy));
        }

        var nebulae = new Nebula[Tuning.NebulaCount];
        for (int i = 0; i < nebulae.Length; i++)
        {
            var c = new Vec3d(r.Range(-1, 1) * Tuning.GalaxyRadius, r.Range(-1, 1) * Tuning.GalaxyThickness, r.Range(-1, 1) * Tuning.GalaxyRadius);
            nebulae[i] = new Nebula(c, r.Range(5, 10), r.Range(0.8, 2.5));
        }

        // Ports: greedy farthest-point spread, so contracts cross the map.
        var ports = new List<int> { r.Index(stars.Count) };
        while (ports.Count < 4)
            ports.Add(stars.OrderByDescending(s => ports.Min(p => (stars[p].Position - s.Position).Length)).First().Id);

        // Depots fill the gaps between ports. They take no contracts, only ships that need supplies.
        var stations = new List<int>(ports);
        while (stations.Count < ports.Count + Tuning.DepotCount)
            stations.Add(stars.OrderByDescending(s => stations.Min(p => (stars[p].Position - s.Position).Length)).First().Id);

        return new Galaxy { Seed = seed, Stars = stars.ToArray(), Nebulae = nebulae, Ports = ports.ToArray(), Depots = stations.Skip(ports.Count).ToArray() };
    }

    public double Density(Vec3d p)
    {
        double d = 1;
        foreach (var n in Nebulae)
            d += n.Density * Math.Exp(-(p - n.Center).LengthSq / (2 * n.Radius * n.Radius));
        return d;
    }
}

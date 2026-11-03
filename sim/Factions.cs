namespace Starbearer.Sim;

/// <summary>A power that owns stations. Every faction is at war with every other, for good.</summary>
public sealed record Faction(int Id, string Name, string Weapon = "shells");

/// <summary>How a faction treats the company, by its standing with it.</summary>
public enum Stance { Hostile, Neutral, Friendly }

/// <summary>
/// Who owns what, and how the company stands with each owner. A station's owner never changes. A system
/// belongs to the faction whose station there came first; with more than one faction there it is
/// contested. Standing runs from Floor to Ceiling: serving a faction (jobs delivered to it, goods sold
/// to it) raises it, and serving its enemy lowers it. A hostile faction turns the ship away from its
/// stations and its gates; the far side of a gate doesn't ask.
/// </summary>
public static class Factions
{
    public const double Floor = -8, Ceiling = 8, FriendlyAt = 2, HostileAt = -2;
    /// <summary>Standing per credit of service, the share an enemy loses of it, and how much more war goods count.</summary>
    public const double PerCredit = 1.0 / 1500, EnemyShare = 0.5, MilitaryWeight = 4;

    static readonly string[] Suffixes = { "連邦", "共和国", "同盟", "帝国", "連合" };

    /// <summary>What a war is fought with. Supplying them moves standing the most.</summary>
    public static readonly HashSet<string> Military = new() { "missiles", "shells", "slugs", "laser_optics", "explosives" };

    public static Stance StanceOf(double standing) => standing <= HostileAt ? Stance.Hostile : standing >= FriendlyAt ? Stance.Friendly : Stance.Neutral;

    public static string Name(Stance stance) => stance switch { Stance.Hostile => "敵対", Stance.Friendly => "友好", _ => "中立" };

    /// <summary>A station's owner by name, or "" for none.</summary>
    public static string OwnerName(Galaxy g, int station) => g.OwnerOf(station) is var f and >= 0 ? g.Factions[f].Name : "";

    /// <summary>A system's hold as the chart reads it: its holder, "係争中" with the holder after it, or "" for none.</summary>
    public static string ClaimText(Galaxy g, int star) => g.Claim(star) switch
    {
        (< 0, _) => "",
        (var f, true) => $"係争中（{g.Factions[f].Name} 領）",
        (var f, false) => $"{g.Factions[f].Name} 領",
    };

    /// <summary>Each faction's standing, as the port shows it.</summary>
    public static string Summary(SaveData s, Galaxy g) => string.Join("   ", g.Factions.Select(f =>
        $"{f.Name} {Standing(s, f.Id):+0.0;-0.0;0.0}（{Name(StanceOf(Standing(s, f.Id)))}）"));

    public static double Standing(SaveData s, int faction) => faction >= 0 && faction < s.Standing.Length ? s.Standing[faction] : 0;

    /// <summary>How a station's owner treats the company; neutral for a station no one owns.</summary>
    public static Stance Toward(SaveData s, Galaxy g, int station) =>
        g.OwnerOf(station) is var f and >= 0 ? StanceOf(Standing(s, f)) : Stance.Neutral;

    /// <summary>Whether the station lets the ship in: to dock, or into its gate.</summary>
    public static bool Admits(SaveData s, Galaxy g, int station) => Toward(s, g, station) != Stance.Hostile;

    /// <summary>
    /// Credits a service worth value to the station's owner, and debits its enemies their share.
    /// Returns the owner's change (zero for a station no one owns).
    /// </summary>
    public static double Serve(SaveData s, Galaxy g, int station, double value, string? ware)
    {
        var owner = g.OwnerOf(station);
        if (owner < 0 || value <= 0) return 0;
        if (s.Standing.Length < g.Factions.Length) s.Standing = s.Standing.Concat(new double[g.Factions.Length - s.Standing.Length]).ToArray();
        var gain = value * PerCredit * (ware != null && Military.Contains(ware) ? MilitaryWeight : 1);
        var before = s.Standing[owner];
        for (int f = 0; f < g.Factions.Length; f++)
            s.Standing[f] = Math.Clamp(s.Standing[f] + (f == owner ? gain : -gain * EnemyShare), Floor, Ceiling);
        return s.Standing[owner] - before;
    }

    /// <summary>
    /// Splits the regions between the factions, about the same number each, each share one run of regions
    /// a gate joins where the gates allow it. Names them too. Drawn last, so the rest of the galaxy comes
    /// out the same as before factions were added.
    /// </summary>
    public static (Faction[] List, int[] RegionOwners) Divide(Rng r, Galaxy g, HashSet<string> names, int count)
    {
        var n = g.Regions.Length;
        var owners = Enumerable.Repeat(-1, n).ToArray();
        count = Math.Min(count, n);
        var list = new Faction[count];
        var suffixes = Suffixes.OrderBy(_ => r.Next()).ToArray();
        var weapons = War.Weapons.OrderBy(_ => r.Next()).ToArray();   // each its own doctrine, so fronts differ in what they burn
        for (int f = 0; f < count; f++)
        {
            string name;
            do name = Galaxy.MakeName(r); while (!names.Add(name));
            list[f] = new Faction(f, name + suffixes[f % suffixes.Length], weapons[f % weapons.Length]);
        }
        if (count == 0) return (list, owners);
        // Every way of dealing the regions out evenly, kept where each faction's share is one run a gate
        // joins; one of those at random. There are few regions, so all of them can be tried.
        bool Joined(int[] deal, int f)
        {
            var mine = Enumerable.Range(0, n).Where(i => deal[i] == f).ToList();
            if (mine.Count == 0) return true;
            var seen = new HashSet<int> { mine[0] };
            var queue = new Queue<int>(seen);
            while (queue.Count > 0)
                foreach (var next in g.LinkedRegions(queue.Dequeue()))
                    if (deal[next] == f && seen.Add(next)) queue.Enqueue(next);
            return seen.Count == mine.Count;
        }
        var even = new List<int[]>();
        var all = new List<int[]>();
        var total = (long)Math.Pow(count, n);
        for (long code = 0; code < total && n <= 12; code++)
        {
            var deal = new int[n];
            for (int i = 0; i < n; i++) deal[i] = (int)(code / (long)Math.Pow(count, i) % count);
            var sizes = Enumerable.Range(0, count).Select(f => deal.Count(x => x == f)).ToArray();
            if (sizes.Max() - sizes.Min() > 1 || sizes.Min() == 0) continue;
            all.Add(deal);
            if (Enumerable.Range(0, count).All(f => Joined(deal, f))) even.Add(deal);
        }
        var pool = even.Count > 0 ? even : all;
        if (pool.Count == 0) { for (int i = 0; i < n; i++) owners[i] = i % count; return (list, owners); }
        owners = pool[r.Index(pool.Count)];
        return (list, owners);
    }
}

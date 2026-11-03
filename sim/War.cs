namespace Starbearer.Sim;

/// <summary>A station a faction built during the game: where, what, whose, and the gate it pairs with (-1 for none).</summary>
public sealed record AddedStation(int Id, int Star, StationKind Kind, int Owner, int Link = -1);

/// <summary>Something the war did on a day: a station destroyed or built, a system changing hands.</summary>
public sealed record WarEvent(double Day, string Kind, int Station, int Star, int Faction);

/// <summary>
/// The war as it stands: which stations are down (fortress sites not yet built, and anything destroyed),
/// how damaged the rest are, when each fortress went up, and how far each faction's next one has come.
/// Saved with the market, and shared with the galaxy so every lookup sees only what stands.
/// </summary>
public sealed class WarState
{
    /// <summary>Stations destroyed. (They never come back; a faction builds anew instead.)</summary>
    public HashSet<int> Down { get; set; } = new();
    /// <summary>Stations built during the game, in the order they went up.</summary>
    public List<AddedStation> Added { get; set; } = new();
    /// <summary>The id the next station built gets.</summary>
    public int NextId { get; set; }
    /// <summary>Structure left, 1 at full; a station missing from it is whole.</summary>
    public Dictionary<int, double> Hull { get; set; } = new();
    /// <summary>The galaxy day each fortress built during the game went up; one there from the start has none.</summary>
    public Dictionary<int, double> Built { get; set; } = new();
    /// <summary>Per faction, building materials gathered toward its next fortress, as a share of one.</summary>
    public double[] Progress { get; set; } = Array.Empty<double>();
    /// <summary>Stations destroyed in the fighting (Down also holds them).</summary>
    public HashSet<int> Destroyed { get; set; } = new();
    public List<WarEvent> Log { get; set; } = new();
    /// <summary>Per faction, materials gathered toward its next station of the economy, as a share of one (see Industry).</summary>
    public double[] Industry { get; set; } = Array.Empty<double>();
    /// <summary>Per faction, how long each ware has been short across it, in days, by ware id.</summary>
    public List<Dictionary<string, double>> Shortage { get; set; } = new();
    /// <summary>Per faction, how long each ware has been stranded: plenty where it is made, short where it is wanted.</summary>
    public List<Dictionary<string, double>> Stranded { get; set; } = new();
    /// <summary>Per faction, materials gathered toward its next gate, as a share of one.</summary>
    public double[] GateProgress { get; set; } = Array.Empty<double>();
    /// <summary>Gate pairs standing still for want of upkeep, by the lower id of the pair.</summary>
    public HashSet<int> Stopped { get; set; } = new();
    /// <summary>Per faction, the day it last built a station of the economy.</summary>
    public double[] LastBuilt { get; set; } = Array.Empty<double>();

    public double HullOf(int station) => Hull.TryGetValue(station, out var h) ? h : 1;

    public WarState Copy() => new()
    {
        Down = Down.ToHashSet(), Added = Added.ToList(), NextId = NextId, Hull = new(Hull), Built = new(Built), Progress = (double[])Progress.Clone(), Destroyed = Destroyed.ToHashSet(), Log = Log.ToList(),
        Stopped = Stopped.ToHashSet(), Industry = (double[])Industry.Clone(),
        Stranded = Stranded.Select(x => new Dictionary<string, double>(x)).ToList(), GateProgress = (double[])GateProgress.Clone(), Shortage = Shortage.Select(x => new Dictionary<string, double>(x)).ToList(), LastBuilt = (double[])LastBuilt.Clone(),
    };

    public static WarState Start(Galaxy g) => new()
    {
        NextId = g.NextStationId, Progress = new double[g.Factions.Length], Industry = new double[g.Factions.Length],
        Shortage = g.Factions.Select(_ => new Dictionary<string, double>()).ToList(),
        Stranded = g.Factions.Select(_ => new Dictionary<string, double>()).ToList(), GateProgress = new double[g.Factions.Length], LastBuilt = Enumerable.Repeat(double.NegativeInfinity, g.Factions.Length).ToArray(),
    };
}

/// <summary>
/// The war, abstractly. Every faction is at war with every other. In a system where more than one has a
/// station, their fortresses trade fire each day: each side hits the other's weakest fortress, or with
/// none left its other stations, as hard as its fortresses are supplied with their weapon's ammunition
/// and fuel, give or take the day's luck. A supplied fortress repairs most of what an equal one does to
/// it, so even sides wear each other down slowly, and the better supplied wins.
/// A station at no structure is destroyed, and the ships left there with it. Steel the fortresses and
/// shipyards take in builds the faction's next one, at its sites near the front.
/// </summary>
public static class War
{
    /// <summary>Structure a fully supplied fortress knocks off a day; it repairs this share of that on itself.</summary>
    public const double FireRate = 0.01, RepairShare = 0.75;
    /// <summary>How much a day's fire varies, either way: the fortunes of war.</summary>
    public const double FireSpread = 0.4;
    /// <summary>Steel a fortress goes through toward the next one; how much makes one.</summary>
    public const double SteelPerFortress = 40 * Economy.Scale;
    /// <summary>Steel a shipyard puts toward its faction's next fortress a day, as far as its stock goes.</summary>
    public const double YardSteel = 0.1 * Economy.Scale;
    /// <summary>A faction keeps no more fortresses standing than this many times what it started with.</summary>
    public const double FortressCap = 1.5;

    /// <summary>What a weapon is called, by the ware it burns.</summary>
    public static string WeaponName(string ware) => ware switch
    {
        "missiles" => "ミサイル",
        "shells" => "火砲",
        "slugs" => "質量投射砲",
        "laser_optics" => "レーザー",
        "hydrogen" => "粒子ビーム",
        _ => ware,
    };

    /// <summary>The weapons a faction may field, by the ware each burns.</summary>
    public static readonly string[] Weapons = { "missiles", "shells", "slugs", "laser_optics", "hydrogen" };

    /// <summary>How well a fortress can fight, 0 to 1: its ammunition, and its fuel to a lesser part.</summary>
    public static double Strength(Galaxy g, Market m, int fortress)
    {
        var weapon = g.Factions[g.OwnerOf(fortress)].Weapon;
        var ammo = Math.Min(1, m.Fill(g, fortress, weapon) ?? 0);
        var fuel = Math.Min(1, m.Fill(g, fortress, Wares.ShipFuel) ?? 0);
        return ammo * (0.4 + 0.6 * fuel);
    }

    /// <summary>The factions with a station standing in a system.</summary>
    public static int[] Sides(Galaxy g, int star) => g.StationsIn(star).Select(g.OwnerOf).Where(f => f >= 0).Distinct().ToArray();

    public static IEnumerable<int> Fortresses(Galaxy g) => g.Ports.Where(p => g.KindOf(p) == StationKind.Fortress && g.Alive(p));

    /// <summary>One day of war, after the day's production and trade.</summary>
    public static void Step(Galaxy g, Market m)
    {
        var w = m.War;
        if (g.Factions.Length == 0 || m.Day < 0) return;   // the war breaks out as the game begins
        if (w.Progress.Length < g.Factions.Length) w.Progress = w.Progress.Concat(new double[g.Factions.Length - w.Progress.Length]).ToArray();
        var strength = Fortresses(g).ToDictionary(f => f, f => Strength(g, m, f));
        // Upkeep: each line of a fortress burns what it can of its rate, its weapon only while it fights,
        // and its steel goes toward the next one until there is enough for it.
        foreach (var f in strength.Keys)
        {
            var e = g.Economies[f];
            var stock = m.Stock[f];
            var fighting = Sides(g, g.StarOf(f)).Length > 1;
            foreach (var (ware, rate) in e.Lines.SelectMany(l => l.In).Concat(e.Upkeep))
            {
                if (ware == g.Factions[g.OwnerOf(f)].Weapon && !fighting) continue;
                if (ware == "steel" && w.Progress[g.OwnerOf(f)] >= 1) continue;
                var i = e.IndexOf(ware);
                var used = Math.Min(rate, stock[i]);
                stock[i] -= used;
                if (ware == "steel") w.Progress[g.OwnerOf(f)] += used / SteelPerFortress;
            }
        }
        // Shipyards turn steel into fortresses too, so a faction driven from the front can come back.
        foreach (var yard in g.Ports.Where(p => g.KindOf(p) == StationKind.Shipyard && g.Alive(p) && g.OwnerOf(p) >= 0))
        {
            if (w.Progress[g.OwnerOf(yard)] >= 1) continue;
            var i = g.Economies[yard].IndexOf("steel");
            var used = Math.Min(YardSteel, m.Stock[yard][i]);
            m.Stock[yard][i] -= used;
            w.Progress[g.OwnerOf(yard)] += used / SteelPerFortress;
        }
        // Fire, in every system where more than one faction stands.
        var hit = new Dictionary<int, double>();
        foreach (var star in g.StationStars.Values.Distinct().OrderBy(s => s))
        {
            var sides = Sides(g, star);
            if (sides.Length < 2) continue;
            var here = g.StationsIn(star).ToList();
            var luck = new Rng(g.Seed * 6364136223846793005UL + (ulong)star * 1442695040888963407UL + (ulong)(m.Day + 1000));
            foreach (var side in sides)
            {
                var fire = here.Where(s => g.OwnerOf(s) != side && strength.ContainsKey(s)).Sum(s => strength[s]) * FireRate * (1 + luck.Range(-FireSpread, FireSpread));
                if (fire <= 0) continue;
                var mine = here.Where(s => g.OwnerOf(s) == side).ToList();
                var forts = mine.Where(strength.ContainsKey).ToList();
                var target = (forts.Count > 0 ? forts : mine).OrderBy(w.HullOf).ThenBy(s => s).First();
                hit[target] = hit.GetValueOrDefault(target) + fire;
            }
        }
        foreach (var f in strength.Keys) w.Hull[f] = Math.Min(1, w.HullOf(f) + FireRate * RepairShare * strength[f]);
        foreach (var (s, damage) in hit.OrderBy(x => x.Key))
        {
            var left = w.HullOf(s) - damage;
            if (left > 0) { w.Hull[s] = Math.Min(1, left); continue; }
            Destroy(g, m, s);
        }
        foreach (var key in w.Hull.Where(x => x.Value >= 1).Select(x => x.Key).ToList()) w.Hull.Remove(key);
        Build(g, m, strength.Keys.ToList());
    }

    /// <summary>
    /// The company's ships left at stations that have fallen are lost with them, and what they carried.
    /// The ship flown is never among them: it is where the pilot is. Returns the ships lost.
    /// </summary>
    public static List<OwnedShip> Casualties(SaveData s, Galaxy g)
    {
        var lost = s.Fleet.Where(x => x.Id != s.Flown && g.IsStation(x.Station) && !g.Alive(x.Station)).ToList();
        foreach (var x in lost) s.Fleet.Remove(x);
        return lost;
    }

    /// <summary>A line of war news.</summary>
    public static string Describe(Galaxy g, WarEvent e) => e.Kind switch
    {
        "destroyed" => $"{Missions.Label(g, e.Station)}（{g.Factions[e.Faction].Name}）が破壊された",
        "built" => $"{g.Factions[e.Faction].Name} が {g.Stars[e.Star].Name} に{(g.KindOf(e.Station) == StationKind.Fortress ? "要塞" : Missions.Name(g.KindOf(e.Station)))}を建てた",
        "taken" => $"{g.Stars[e.Star].Name} 星系が {g.Factions[e.Faction].Name} 領になった",
        "gate down" => $"{g.Stars[e.Star].Name} のゲートが止まった（維持の品物が尽きた）。{g.Stars[g.StarOf(g.GateLinks[e.Station])].Name} との間は通れない",
        "collapsed" => $"{g.Stars[e.Star].Name} のゲートが、対の端を失って崩れた",
        "gate built" => $"{g.Factions[e.Faction].Name} が {g.Stars[e.Star].Name} と {g.Stars[g.StarOf(g.GateLinks[e.Station])].Name} の間にゲートを建てた",
        "gate up" => $"{g.Stars[e.Star].Name} と {g.Stars[g.StarOf(g.GateLinks[e.Station])].Name} の間のゲートが動き出した",
        _ => e.Kind,
    };

    /// <summary>The news of what the war did after one day up to another.</summary>
    public static IEnumerable<WarEvent> Between(Market m, double after, double upTo) => m.War.Log.Where(e => e.Day > after - 1e-9 && e.Day <= upTo + 1e-9);

    /// <summary>The systems being fought over, nearest the given star first.</summary>
    public static IEnumerable<int> Fronts(Galaxy g) => g.StationStars.Values.Distinct().Where(s => Sides(g, s).Length > 1).OrderBy(s => s);

    /// <summary>
    /// A station falls: it is down for good, its stock gone, and the system may change hands. A gate
    /// takes its other end with it: the pair is one field, and half of it holds nothing open.
    /// </summary>
    static void Destroy(Galaxy g, Market m, int station, string kind = "destroyed")
    {
        var w = m.War;
        var star = g.StarOf(station);
        var before = g.Claim(star).Holder;
        w.Down.Add(station);
        w.Hull.Remove(station);
        w.Built.Remove(station);
        w.Destroyed.Add(station);
        if (g.KindOf(station) == StationKind.Gate) g.RoutesChanged();   // goods and news lose the way through it
        Array.Clear(m.Stock[station]);
        w.Log.Add(new WarEvent(m.Day, kind, station, star, g.OwnerOf(station)));
        var after = g.Claim(star).Holder;
        if (after != before && after >= 0) w.Log.Add(new WarEvent(m.Day, "taken", station, star, after));
        if (g.GateLinks.TryGetValue(station, out var exit) && g.Alive(exit)) Destroy(g, m, exit, "collapsed");
    }

    /// <summary>
    /// A faction with materials for a fortress builds one where the war needs it most: in a system it
    /// is losing, then one the enemy holds within reach of one of its own (an attack: the front moves
    /// on as systems fall), then one of its own within reach of the enemy. Never where it already has one.
    /// </summary>
    static void Build(Galaxy g, Market m, List<int> standing)
    {
        var w = m.War;
        var systems = g.StationStars.Values.Distinct().Where(g.HasStation).OrderBy(x => x).ToList();
        double Apart(int a, int b) => (g.Stars[a].Position - g.Stars[b].Position).Length;
        foreach (var faction in g.Factions)
        {
            var f = faction.Id;
            if (w.Progress[f] < 1) continue;
            var count = standing.Count(x => g.OwnerOf(x) == f);
            var start = g.Ports.Count(p => g.KindOf(p) == StationKind.Fortress && g.OwnerOf(p) == f && !g.BuiltDuringGame(p));
            if (count >= Math.Max(1, start * FortressCap)) { w.Progress[f] = 1; continue; }
            var ours = systems.Where(x => Sides(g, x).Contains(f)).ToList();
            var enemy = systems.Where(x => Sides(g, x).Any(o => o != f)).ToList();
            double Need(int star)
            {
                var here = g.StationsIn(star).ToList();
                if (here.Any(x => g.OwnerOf(x) == f && g.KindOf(x) == StationKind.Fortress)) return -1;
                var sides = Sides(g, star);
                if (sides.Length > 1 && sides.Contains(f))
                {
                    double Power(bool mine) => here.Where(x => (g.OwnerOf(x) == f) == mine && g.KindOf(x) == StationKind.Fortress).Sum(x => Strength(g, m, x) * w.HullOf(x));
                    return Power(false) > Power(true) ? 3 : 0.5;
                }
                if (!sides.Contains(f)) return ours.Any(o => Apart(o, star) <= Tuning.FrontReachLy) ? 2 : -1;
                return enemy.Any(o => Apart(o, star) <= Tuning.FrontReachLy) ? 1 : -1;
            }
            var pick = systems.Select(x => (Star: x, Need: Need(x))).Where(x => x.Need > 0)
                .OrderByDescending(x => x.Need).ThenBy(x => ours.Count == 0 ? 0 : ours.Min(o => Apart(o, x.Star))).ThenBy(x => x.Star).FirstOrDefault((-1, 0));
            if (pick.Star < 0) continue;
            var before = g.Claim(pick.Star).Holder;
            w.Progress[f] -= 1;
            var id = m.Build(g, pick.Star, StationKind.Fortress, f);
            w.Hull[id] = 0.5;
            w.Log.Add(new WarEvent(m.Day, "built", id, pick.Star, f));
            if (before < 0) w.Log.Add(new WarEvent(m.Day, "taken", id, pick.Star, f));
        }
    }
}

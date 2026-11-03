namespace Starbearer.Sim;

/// <summary>
/// The war, abstractly. Every faction is at war with every other. In a system where more than one has a
/// station, their fortresses trade fire each day: each side hits the other's weakest fortress, or with
/// none left its other stations, as hard as its fortresses are supplied with their weapon's ammunition
/// and fuel, give or take the day's luck. A supplied fortress repairs most of what an equal one does to
/// it, so even sides wear each other down slowly, and the better supplied wins.
/// A station at no structure is destroyed, and the ships left there with it. Steel the fortresses and
/// shipyards take in builds the faction's next one, where the war needs it (see Build).
///
/// A faction's capital commands its fortresses: they fire at full only while it stands whole. It is
/// armoured and mends slowly, but it can fall; then the faction's fire drops until, some months on, it
/// moves its seat to the safest system it still holds.
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
    /// <summary>Fire on a capital is divided by this; it mends this much structure a day.</summary>
    public const double CapitalArmor = 2, CapitalRepair = 0.002;
    /// <summary>How hard a faction's fortresses fire with its capital fallen, and days until it moves its seat.</summary>
    public const double FallenCommand = 0.7, RelocateDays = 60;

    /// <summary>A faction's capital standing, or -1.</summary>
    public static int Capital(Galaxy g, int faction) =>
        g.Ports.Where(p => g.KindOf(p) == StationKind.Capital && g.OwnerOf(p) == faction && g.Alive(p)).DefaultIfEmpty(-1).First();

    /// <summary>
    /// How hard a faction's fortresses fire, by its capital: from 0.8 to full as its structure goes, and
    /// FallenCommand with it gone. A faction that never had one (a galaxy made by hand) fires at full.
    /// </summary>
    public static double Command(Galaxy g, Market m, int faction)
    {
        var capital = Capital(g, faction);
        if (capital >= 0) return 0.8 + 0.2 * m.War.HullOf(capital);
        return m.War.CapitalFell.ContainsKey(faction) ? FallenCommand : 1;
    }

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
        foreach (var yard in g.Shipyards())
        {
            var f = g.OwnerOf(yard);
            if (f >= 0 && w.Progress[f] < 1) w.Progress[f] += m.Take(g, yard, "steel", YardSteel) / SteelPerFortress;
        }
        // Fire, in every system where more than one faction stands.
        var command = g.Factions.Select(f => Command(g, m, f.Id)).ToArray();
        var hit = new Dictionary<int, double>();
        foreach (var star in g.StationStars.Values.Distinct().OrderBy(s => s))
        {
            var sides = Sides(g, star);
            if (sides.Length < 2) continue;
            var here = g.StationsIn(star).ToList();
            var luck = new Rng(g.Seed * 6364136223846793005UL + (ulong)star * 1442695040888963407UL + (ulong)(m.Day + 1000));
            foreach (var side in sides)
            {
                var fire = here.Where(s => g.OwnerOf(s) != side && strength.ContainsKey(s)).Sum(s => strength[s] * command[g.OwnerOf(s)]) * FireRate * (1 + luck.Range(-FireSpread, FireSpread));
                if (fire <= 0) continue;
                var mine = here.Where(s => g.OwnerOf(s) == side).ToList();
                var forts = mine.Where(strength.ContainsKey).ToList();
                var target = (forts.Count > 0 ? forts : mine).OrderBy(w.HullOf).ThenBy(s => s).First();
                hit[target] = hit.GetValueOrDefault(target) + fire / (g.KindOf(target) == StationKind.Capital ? CapitalArmor : 1);
            }
        }
        foreach (var f in strength.Keys) w.Hull[f] = Math.Min(1, w.HullOf(f) + FireRate * RepairShare * strength[f]);
        foreach (var c in g.Ports.Where(p => g.KindOf(p) == StationKind.Capital && g.Alive(p) && w.HullOf(p) < 1)) w.Hull[c] = Math.Min(1, w.HullOf(c) + CapitalRepair);
        foreach (var (s, damage) in hit.OrderBy(x => x.Key))
        {
            var left = w.HullOf(s) - damage;
            if (left > 0) { w.Hull[s] = Math.Min(1, left); continue; }
            Destroy(g, m, s);
        }
        foreach (var key in w.Hull.Where(x => x.Value >= 1).Select(x => x.Key).ToList()) w.Hull.Remove(key);
        Build(g, m, strength.Keys.ToList());
        Relocate(g, m);
    }

    /// <summary>
    /// A faction whose capital fell long enough ago moves its seat: to the system it holds alone that is
    /// farthest from the enemy, among those with a station of its own, more stations first where it is a close call.
    /// </summary>
    static void Relocate(Galaxy g, Market m)
    {
        var w = m.War;
        foreach (var (f, fell) in w.CapitalFell.OrderBy(x => x.Key).ToList())
        {
            if (m.Day - fell < RelocateDays || Capital(g, f) >= 0) continue;
            var systems = g.StationStars.Values.Distinct().Where(g.HasStation).OrderBy(x => x).ToList();
            var enemy = systems.Where(x => War.Sides(g, x).Any(o => o != f)).Select(x => g.Stars[x].Position).ToList();
            var star = systems.Where(x => g.Claim(x) is (var holder, false) && holder == f && g.GateIn(x) < 0)
                .Select(x => (Star: x, Score: enemy.Select(e => (e - g.Stars[x].Position).Length).DefaultIfEmpty(0).Min() + 3 * g.StationsIn(x).Count()))
                .OrderByDescending(x => x.Score).ThenBy(x => x.Star).Select(x => x.Star).DefaultIfEmpty(-1).First();
            if (star < 0) continue;   // nothing of its own left to move to
            var id = m.Build(g, star, StationKind.Capital, f);
            w.Hull[id] = 0.5;
            w.CapitalFell.Remove(f);
            w.Log.Add(new WarEvent(m.Day, "capital moved", id, star, f));
        }
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
        "capital fell" => $"{g.Factions[e.Faction].Name} の首都 {g.Stars[e.Star].Name} が陥落した。遷都まで要塞の火力が落ちる",
        "capital moved" => $"{g.Factions[e.Faction].Name} が {g.Stars[e.Star].Name} に遷都した",
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
        if (g.KindOf(station) == StationKind.Capital) { kind = "capital fell"; w.CapitalFell[g.OwnerOf(station)] = m.Day; }
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
    /// is losing (its capital's above all), then one the enemy holds within reach of one of its own (an attack: the front moves
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
                    if (here.Any(x => g.OwnerOf(x) == f && g.KindOf(x) == StationKind.Capital)) return 4;   // the seat comes first
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

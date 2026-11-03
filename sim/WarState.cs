namespace Starbearer.Sim;

/// <summary>A station a faction built during the game: where, what, whose, and the gate it pairs with (-1 for none).</summary>
public sealed record AddedStation(int Id, int Star, StationKind Kind, int Owner, int Link = -1);

/// <summary>Something the war did on a day: a station destroyed or built, a system changing hands.</summary>
public sealed record WarEvent(double Day, string Kind, int Station, int Star, int Faction);

/// <summary>
/// The state of the world beyond the market: the stations built during the game and those destroyed,
/// how damaged the rest are, when each went up, what each faction has gathered toward its next
/// fortress, station and gate, what it is short of, which gates stand still, and the news. Saved with
/// the market, and bound to the galaxy so every lookup sees only what stands.
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
    /// <summary>The galaxy day each station built during the game went up; one there from the start has none.</summary>
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
    /// <summary>Factions whose capital has fallen and who have not moved their seat yet, with the day it fell.</summary>
    public Dictionary<int, double> CapitalFell { get; set; } = new();

    public double HullOf(int station) => Hull.TryGetValue(station, out var h) ? h : 1;

    public WarState Copy() => new()
    {
        Down = Down.ToHashSet(), Added = Added.ToList(), NextId = NextId, Hull = new(Hull), Built = new(Built), Progress = (double[])Progress.Clone(), Destroyed = Destroyed.ToHashSet(), Log = Log.ToList(),
        Stopped = Stopped.ToHashSet(), Industry = (double[])Industry.Clone(),
        Stranded = Stranded.Select(x => new Dictionary<string, double>(x)).ToList(), GateProgress = (double[])GateProgress.Clone(), Shortage = Shortage.Select(x => new Dictionary<string, double>(x)).ToList(), LastBuilt = (double[])LastBuilt.Clone(),
        CapitalFell = new(CapitalFell),
    };

    public static WarState Start(Galaxy g) => new()
    {
        NextId = g.NextStationId, Progress = new double[g.Factions.Length], Industry = new double[g.Factions.Length],
        Shortage = g.Factions.Select(_ => new Dictionary<string, double>()).ToList(),
        Stranded = g.Factions.Select(_ => new Dictionary<string, double>()).ToList(), GateProgress = new double[g.Factions.Length], LastBuilt = Enumerable.Repeat(double.NegativeInfinity, g.Factions.Length).ToArray(),
    };
}

using System.Text.Json;
using System.Text.Json.Serialization;
namespace Starbearer.Sim;

/// <summary>Checkpoint taken while docked. A flight is never saved: that could lock in a doomed trajectory.</summary>
public sealed class SaveData
{
    /// <summary>
    /// What this build's saves are. A galaxy is made again from its seed on loading, so a change to how
    /// galaxies are made (or to what a save holds) leaves older saves describing another galaxy: raise
    /// this with any such change. A save from before the field is format 0.
    /// </summary>
    public const int CurrentFormat = 1;
    public int Format { get; set; }
    public ulong Seed { get; set; }
    public int Deliveries { get; set; }
    public int Retries { get; set; }
    public double Credits { get; set; } = Tuning.StartingCredits;
    public double TotalDays { get; set; }
    public double[] ClosestApproach { get; set; } = Array.Empty<double>();
    /// <summary>Every ship the company owns, each where it was last docked.</summary>
    public List<OwnedShip> Fleet { get; set; } = new() { new OwnedShip { Id = 1 } };
    /// <summary>Id of the ship flown. The checkpoint is where it is docked.</summary>
    public int Flown { get; set; } = 1;
    /// <summary>The star the instruments steer for. Falls back when no cargo is bound there.</summary>
    public int Course { get; set; } = -1;
    /// <summary>The board at the port of the checkpoint, as posted (see Missions.OffersFor).</summary>
    public List<Mission> Offers { get; set; } = new();
    public string OffersAt { get; set; } = "";
    /// <summary>How each faction regards the company, by faction id (see Factions).</summary>
    public double[] Standing { get; set; } = new double[Tuning.FactionCount];
    /// <summary>The galaxy's stock, as of the checkpoint.</summary>
    public Market? Market { get; set; }

    /// <summary>The market brought up to the checkpoint's day, opened from the seed if this save has none yet.</summary>
    public Market MarketIn(Galaxy g)
    {
        Market ??= Market.Open(g);
        Market.AdvanceTo(g, TotalDays);
        return Market;
    }

    [JsonIgnore] public OwnedShip Ship => Fleet.First(x => x.Id == Flown);
    // The ship flown, as the rest of the game reads it.
    [JsonIgnore] public int PortStation { get => Ship.Station; set => Ship.Station = value; }
    [JsonIgnore] public List<string> Modules { get => Ship.Modules; set => Ship.Modules = value; }
    [JsonIgnore] public List<int> Pressurized { get => Ship.Pressurized; set => Ship.Pressurized = value; }
    /// <summary>Jobs in the bays of the ship flown, as of the last dock.</summary>
    [JsonIgnore] public List<Consignment> Hold { get => Ship.Hold; set => Ship.Hold = value; }
    [JsonIgnore] public double HeldFuel { get => Ship.Fuel; set => Ship.Fuel = value; }
    [JsonIgnore] public double HeldHull { get => Ship.Hull; set => Ship.Hull = value; }
    [JsonIgnore] public double HeldLife { get => Ship.Life; set => Ship.Life = value; }

    /// <summary>The ships docked at a station, the one flown first.</summary>
    public IEnumerable<OwnedShip> ShipsAt(int station) => Fleet.Where(x => x.Station == station).OrderBy(x => x.Id != Flown).ThenBy(x => x.Id);

    static readonly JsonSerializerOptions Options = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static SaveData? FromJson(string json) => JsonSerializer.Deserialize<SaveData>(json, Options);

    /// <summary>Whether this build can carry on from the save: one made by an older build describes another galaxy.</summary>
    public bool Current => Format == CurrentFormat;

    public static (SaveData Save, Galaxy Galaxy) NewGalaxy(ulong seed)
    {
        var g = Galaxy.Generate(seed);
        return (new SaveData
        {
            Format = CurrentFormat, Seed = seed, Fleet = new() { new OwnedShip { Id = 1, Station = g.Ports[0] } },
            ClosestApproach = Enumerable.Repeat(double.MaxValue, g.Stars.Length).ToArray(),
            Market = Market.Open(g),
        }, g);
    }
}

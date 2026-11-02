namespace Starbearer.Sim;

/// <summary>A station on a circular orbit. U and W span its orbital plane.</summary>
/// <param name="Id">The station's own id (see Galaxy.FirstStationId); Star is the system it is in.</param>
public sealed record Station(int Id, int Star, double Radius, double Phase, double Omega, Vec3d U, Vec3d W) : IOrbiter
{
    /// <summary>The body it circles. Null only for stations built by hand around a bare star.</summary>
    public Center? Host { get; init; }
    public Vec3d PlaneNormal => U.Cross(W);
    public double OrbitRadius => Radius;

    public Vec3d Position(double t)
    {
        var a = Phase + Omega * t;
        return (U * Math.Cos(a) + W * Math.Sin(a)) * Radius;
    }

    public Vec3d Velocity(double t)
    {
        var a = Phase + Omega * t;
        return (W * Math.Cos(a) - U * Math.Sin(a)) * (Radius * Omega);
    }
}

/// <summary>Planned impulse. Prograde and radial are relative to the velocity at Time.</summary>
public sealed class Maneuver
{
    public double Time, Prograde, Radial, Normal;
    public double DeltaV => Math.Sqrt(Prograde * Prograde + Radial * Radial + Normal * Normal);
}

public readonly record struct Approach(double Time, double Distance, double RelativeSpeed, Vec3d Ship, Vec3d Station);
/// <summary>Where the ship first reaches the station's orbit, and how far off the station is then.</summary>
public readonly record struct Crossing(double Time, Vec3d Ship, Vec3d Station, double LeadDegrees);

/// <summary>One planned burn as the forecast sees it: the state it starts from and the orbit it leaves.</summary>
public readonly record struct Stage(Maneuver Node, Vec3d Position, Vec3d Velocity, Elements After, int TrackIndex);

/// <summary>Where a planned burn was to happen and the velocity it was to leave the ship with.</summary>
public readonly record struct BurnTarget(double Time, Vec3d Position, Vec3d Before, Vec3d After);

/// <summary>A periapsis or apoapsis on a path. Radius is from the centre of the leg's body.</summary>
public readonly record struct Apsis(double Time, Vec3d Position, double Radius, bool Apo);

public readonly record struct Elements(double Periapsis, double Apoapsis, double Period, bool Bound);

/// <summary>Where the path leaves the star's sphere of influence.</summary>
public readonly record struct Disengage(double Time, Vec3d Position, Vec3d Velocity);
/// <summary>Where the path enters a body's sphere of influence. State is relative to that body.</summary>
public readonly record struct Entry(Center Body, double Time, Vec3d Position, Vec3d Velocity);
/// <summary>The next star whose sphere of influence a ship in open space runs into. Point is in ly.</summary>
public readonly record struct Handover(int Star, double Time, Vec3d Point);

public sealed class OrbitForecast
{
    /// <summary>The whole searched path. Later laps matter for meeting a station but aren't drawn.</summary>
    public List<(double Time, Vec3d Position)> Track { get; } = new();
    /// <summary>Points of Track to show: up to one lap past the last burn, or to where the star lets go.</summary>
    public int DrawnCount { get; set; }
    public Disengage? Exit { get; set; }
    public Entry? Enter { get; set; }
    /// <summary>The body this leg is relative to.</summary>
    public Center? Center { get; set; }
    public Handover? Next { get; set; }
    /// <summary>The star whose frame Track and Stages are in.</summary>
    public int Star { get; set; }
    /// <summary>True for the straight run through open space between two stars.</summary>
    public bool IsDrift { get; init; }
    /// <summary>The run from Exit to Next. Burns can be planned on it.</summary>
    public OrbitForecast? Drift { get; set; }
    /// <summary>The path on from Next, around the star that takes over.</summary>
    public OrbitForecast? Beyond { get; set; }

    /// <summary>Every piece of the path in flight order. Each has its own star's frame.</summary>
    public IEnumerable<OrbitForecast> Legs
    {
        get
        {
            yield return this;
            if (Drift != null) yield return Drift;
            if (Beyond != null) foreach (var leg in Beyond.Legs) yield return leg;
        }
    }

    public IEnumerable<Stage> AllStages => Legs.SelectMany(l => l.Stages);
    public OrbitForecast? LegOf(Maneuver node) => Legs.FirstOrDefault(l => l.Stages.Any(s => s.Node == node));
    public Stage? StageOf(Maneuver node) => LegOf(node)?.Stages.First(s => s.Node == node);
    public List<Stage> Stages { get; } = new();
    /// <summary>Points where the path crosses the station's plane: ascending when the flag is true.</summary>
    public List<(Vec3d Position, bool Ascending)> PlaneCrossings { get; } = new();
    /// <summary>What Closest, Crossing and PlaneCrossings are measured against: a station or a body circling Center.</summary>
    public IOrbiter? Watched { get; init; }
    public Approach? Closest { get; set; }
    /// <summary>Periapses and apoapses on the drawn path, one of each per coast between burns.</summary>
    public List<Apsis> Apsides { get; } = new();
    public Crossing? Crossing { get; set; }
    /// <summary>Orbit after the last planned burn.</summary>
    public Elements After { get; set; }
    /// <summary>State after the last planned burn, where a jump would start from.</summary>
    public Vec3d AfterPosition { get; set; }
    public Vec3d AfterVelocity { get; set; }
    public bool Impact { get; set; }
}

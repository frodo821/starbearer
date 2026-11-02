namespace Starbearer.Sim;

/// <summary>All balance numbers. Units: ly, day, ship mass units.</summary>
public static class Tuning
{
    // Wells
    public const double MuPerMass = 0.02;
    public const double SoiPerSqrtMass = 1.3;
    public const double StarRadiusPerSqrtMass = 0.03;

    // Hyperspace medium: quadratic drag, so speed decays per ly travelled.
    public const double BaseDrag = 0.030;
    public const double StallSpeed = 0.15;

    // Ship
    public const double DryMass = 1.0;
    public const double TankCapacity = 1.8;
    public const double ExhaustVelocity = 1.6;
    public const double MaxThrust = 1.0;      // ly/day^2, used during manual flyby burns
    public const double LifeSupportDays = 600;
    public const double HoldVolume = 8;
    public const double CubicMetersPerVolume = 20;   // for display: one unit is about a 20ft container
    public const double StartingCredits = 200;

    // Resupply at a stop on the way. A full tank is about a third of a contract's pay.
    public const double FuelPrice = 30;            // per unit of fuel
    public const double LifePricePerDay = 0.1;
    public const double HullRepairPrice = 60;      // for a whole hull
    public const double DepartureFuelFloor = 0.8;  // below this a ship can't reach another station

    // Normal space
    public const double SublightThrust = 0.2;      // AU/day^2
    public const double SublightFuelFactor = 0.5;  // sublight delta-v is cheaper than FTL delta-v
    public const double DockRange = 0.01;          // AU
    public const double DockRelativeSpeed = 0.02;  // AU/day
    public const double RendezvousFuelReserve = 0.5;  // covers about 90% of rendezvous, flown well

    // Flyby hazards
    public const double HeatCoefficient = 0.02;
    public const double ShieldFactor = 0.25;
    public const double CoolingPerDay = 0.6;
    public const double HullDamagePerExcessHeatDay = 3.0;
    public const double CargoDamagePerExcessHeatDay = 4.0;
    public const double ScoopCoefficient = 0.012; // fuel/day at r=1 per solar mass

    // Navigation error
    public const double PositionDriftPerSqrtDay = 0.015;
    public const double SightError = 0.008;
    public const double SightDays = 0.25;
    public const double BurnPointingErrorRad = 0.0015;
    public const double ChartErrorLy = 0.18;
    public const double ChartRefineRangeLy = 9;

    // Missions
    public const double StandardHaulLy = 20;       // a mission pays its cargo's Reward over this distance
    public const double DropPenalty = 0.25;        // fraction of the pay lost by giving up a job

    // Generation. A region is about what the whole galaxy used to be: 28 stars in 24 ly, 4 ports, 8 depots.
    public const int RegionCount = 4;
    public const int StarsPerRegion = 30;
    public const int StarCount = RegionCount * StarsPerRegion;
    /// <summary>The disc grows with the star count so the stars stay as far apart as before.</summary>
    public static readonly double GalaxyRadius = 24 * Math.Sqrt(StarCount / 28.0);
    public const double GalaxyThickness = 6;
    public const double MinStarSeparation = 4.0;
    public const int NebulaCount = 6 * StarCount / 28;
    /// <summary>Stations every region has. Farm stands for any production station.</summary>
    public static readonly StationKind[] RegionMinimum = { StationKind.Farm, StationKind.Farm, StationKind.Colony, StationKind.Shipyard };
    /// <summary>Share of a region's stars with a station, drawn per region; the old galaxy had 12 in 28.</summary>
    public const double StationShareMin = 0.25, StationShareMax = 0.45;
}

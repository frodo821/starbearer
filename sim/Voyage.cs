namespace Starbearer.Sim;

public enum Phase { Sublight, Flight, Stalled, Arrived, Dead }
public enum DeathCause { None, Collision, HullFailure, Stranded, LifeSupport }

/// <param name="Reward">Pay for a standard haul; a mission scales it by distance.</param>
/// <param name="Volume">Room it takes in the hold.</param>
/// <param name="Kind">Which bays can carry it.</param>
public sealed record Cargo(string Name, double Mass, double HeatTolerance, double Reward, double Volume = 1, CargoKind Kind = CargoKind.Container)
{
    /// <summary>The traded good it is, or null for a one-off item that exists only as a job.</summary>
    public string? Ware { get; init; }
}
/// <summary>The ship as the last dock left it.</summary>
public sealed record ShipState(double Fuel, double Hull, double Life);

/// <summary>Everything that changes during a voyage. Sky rendering reads true state, the planner reads the estimate.</summary>
public sealed class Voyage
{
    public Galaxy G { get; }
    /// <summary>The station the voyage set out from (a station id).</summary>
    public int OriginStation { get; }
    /// <summary>The star the instruments steer for.</summary>
    public int TargetStar { get; set; }
    public List<Consignment> Hold { get; private set; }
    public double CargoMass => Hold.Sum(c => c.Mission.Cargo.Mass);
    /// <summary>The most heat everything in the hold can take.</summary>
    public double HeatTolerance => Hold.Count == 0 ? double.PositiveInfinity : Hold.Min(c => c.Mission.Cargo.HeatTolerance);
    /// <summary>The worst state of anything in the hold.</summary>
    public double CargoIntegrity => Hold.Count == 0 ? 1 : Hold.Min(c => c.Integrity);
    public Phase Phase { get; private set; } = Phase.Sublight;
    public DeathCause Death { get; private set; }

    public Vec3d TruePosition { get; private set; }
    public Vec3d Velocity { get; private set; }
    Vec3d _navError = Vec3d.Zero;
    public double NavSigma { get; private set; } = Tuning.SightError;

    /// <summary>The hull flown: its mass, tank, drag, thrust and whether gates take it.</summary>
    public ShipClass Class { get; }
    public double Fuel { get; private set; }
    public double Hull { get; private set; } = 1;
    public double Heat { get; private set; }
    public double Time { get; private set; }
    public double LifeSupply { get; private set; }
    public double LifeRemaining => LifeSupply - (Time - _dockedDays);
    /// <summary>On the port's supply: waiting here for a departure window costs no life support.
    /// A voyage starts docked. The game keeps the ship's controls locked until Undock.</summary>
    public bool Docked { get; private set; } = true;
    double _dockedDays;
    public bool Thrusting { get; private set; }

    public double[] ClosestApproach { get; }
    readonly Rng _rng;
    Vec3d[] _chart;
    readonly Vec3d[] _truth;

    public Vec3d EstimatedPosition => TruePosition + _navError;
    public Vec3d[] TruePositions => _truth;
    public Vec3d[] ChartPositions => _chart;
    public double Mass => Class.DryMass + CargoMass + Fuel;
    public double DeltaVRemaining => Tuning.ExhaustVelocity * Math.Log(Mass / (Class.DryMass + CargoMass));
    public double SublightThrust => Tuning.SublightThrust * Class.Thrust;
    public double FlightThrust => Tuning.MaxThrust * Class.Thrust;

    // Normal space
    public double Epoch { get; }
    public double AbsoluteTime => Epoch + Time;
    /// <summary>The body the ship's sublight state is relative to.</summary>
    public Center Primary { get; private set; } = null!;
    public int SystemStar => Primary.Star;
    public Vec3d SubPosition { get; private set; }
    public Vec3d SubVelocity { get; private set; }
    public List<Maneuver> Nodes { get; } = new();
    /// <summary>Whether the autopilot module flies the nodes. The burn itself is ordinary thrust.</summary>
    public bool AutoBurn { get; set; }
    /// <summary>Applies each node as an instant impulse, so tests can check a plan apart from how it is flown.</summary>
    public bool IdealNodesForTest { get; set; }
    public Star System => G.Stars[SystemStar];
    /// <summary>
    /// The station the ship deals with in this system: the one picked as target, else the one it set out
    /// from or came through, else the one a job in the hold is bound for, else the system's first that
    /// isn't a fortress. Null in a system without one.
    /// </summary>
    public Station? LocalStation => _station is int id && G.StarOf(id) == SystemStar && G.Alive(id) ? G.StationAt(id)
        : DefaultStation(SystemStar) is int d ? G.StationAt(d) : null;
    int? _station;

    int? DefaultStation(int star)
    {
        var here = G.StationsIn(star).ToList();
        if (here.Count == 0) return null;
        var bound = Hold.Where(c => c.IsJob).Select(c => c.Mission.Target).FirstOrDefault(here.Contains, -1);
        return bound >= 0 ? bound : here.Where(s => G.KindOf(s) != StationKind.Fortress).DefaultIfEmpty(here[0]).First();
    }

    /// <summary>A ship with one load, as the tools and tests fly it. origin is a station id, target a star.</summary>
    public Voyage(Galaxy g, int origin, int target, Cargo cargo, double[] closestApproach, ulong rngSeed, double epoch = 0, ShipState? ship = null, ShipClass? shipClass = null)
        : this(g, origin, target, new[] { new Consignment { Mission = new Mission("", origin, target, cargo, cargo.Reward) } }, closestApproach, rngSeed, epoch, ship, shipClass) { }

    public Voyage(Galaxy g, int origin, int target, IEnumerable<Consignment> hold, double[] closestApproach, ulong rngSeed, double epoch = 0, ShipState? ship = null, ShipClass? shipClass = null)
    {
        G = g; OriginStation = origin; TargetStar = target; Hold = hold.Select(c => c.Copy()).ToList();
        _station = NextDoor(origin);
        Class = shipClass ?? Ships.Courier;
        Fuel = ship?.Fuel ?? Class.Tank;
        Hull = ship?.Hull ?? 1;
        LifeSupply = ship?.Life ?? Tuning.LifeSupportDays;
        ClosestApproach = closestApproach;
        _rng = new Rng(rngSeed);
        _truth = g.Stars.Select(s => s.Position).ToArray();
        _chart = new Vec3d[g.Stars.Length];
        Epoch = epoch;
        var st = g.StationAt(origin);
        Primary = st.Host ?? g.System(st.Star);
        SubPosition = st.Position(epoch);
        SubVelocity = st.Velocity(epoch);
        SyncTruePosition();
        Observe();
    }

    void SyncTruePosition() => TruePosition = JumpState.Position;

    /// <summary>Leaving a station with a job for another of its system, that one is what the ship deals with next; else the station itself.</summary>
    int NextDoor(int station) => Hold.Where(c => c.IsJob).Select(c => c.Mission.Target)
        .FirstOrDefault(t => t != station && G.IsStation(t) && G.StarOf(t) == G.StarOf(station) && G.Alive(t), station);

    /// <summary>The station relative to the ship's current primary, whichever body the station circles.</summary>
    public (Vec3d Position, Vec3d Velocity) StationNow()
    {
        var st = LocalStation!;
        var t = AbsoluteTime;
        var host = st.Host ?? G.System(SystemStar);
        return (host.StarPosition(t) - Primary.StarPosition(t) + st.Position(t), host.StarVelocity(t) - Primary.StarVelocity(t) + st.Velocity(t));
    }

    /// <summary>A body or station picked as the target. Not saved: it is a setting of the ship's computer.</summary>
    public IOrbiter? ChosenTarget { get; private set; }
    /// <summary>The system where the pilot cleared the target, so nothing is steered for there; -1 if none.</summary>
    int _clearedIn = -1;

    /// <summary>What the plans are for: the chosen target in this system, else the station, unless cleared.</summary>
    public IOrbiter? Goal => _clearedIn == SystemStar ? null : ChosenTarget is { } c && c.Star == SystemStar ? c : LocalStation;

    /// <summary>Sets the target; null clears it, so the plans are for nothing in particular until another is picked.</summary>
    public void SetGoal(IOrbiter? target)
    {
        ChosenTarget = target is Station ? null : target;
        if (target is Station st) _station = st.Id;
        _clearedIn = target == null ? SystemStar : -1;
    }

    /// <summary>What to steer for from here to reach the goal. See <see cref="Sublight.Waypoint"/>.</summary>
    public IOrbiter? Waypoint => Sublight.Waypoint(Primary, Goal);

    /// <summary>Where an orbiter of this system is now, relative to the ship's primary.</summary>
    public (Vec3d Position, Vec3d Velocity) RelativeNow(IOrbiter o)
    {
        var t = AbsoluteTime;
        if (o is Center c) return (c.StarPosition(t) - Primary.StarPosition(t), c.StarVelocity(t) - Primary.StarVelocity(t));
        var host = (o as Station)?.Host ?? G.System(SystemStar);
        return (host.StarPosition(t) - Primary.StarPosition(t) + o.Position(t), host.StarVelocity(t) - Primary.StarVelocity(t) + o.Velocity(t));
    }

    /// <summary>State a jump would start from. Speed carries over, so timing the orbit matters.</summary>
    public (Vec3d Position, Vec3d Velocity) JumpState =>
        (System.Position + (Primary.StarPosition(AbsoluteTime) + SubPosition) * Sublight.Scale, SubVelocity + Primary.StarVelocity(AbsoluteTime));

    public void EnterFtl()
    {
        if (Phase != Phase.Sublight) return;
        (TruePosition, Velocity) = JumpState;
        Nodes.Clear();
        Docked = false;
        Phase = Phase.Flight;
    }

    /// <summary>Drops out of hyperspace inside a well; the same state becomes a normal-space orbit.</summary>
    public bool Descend()
    {
        if (Phase != Phase.Flight) return false;
        NearestWell(out var star);
        if (star == null) return false;
        Primary = G.System(star.Id);
        SubPosition = (TruePosition - _truth[star.Id]) / Sublight.Scale;
        SubVelocity = Velocity;
        Phase = Phase.Sublight;
        return true;
    }

    public void Undock() => Docked = false;

    /// <summary>Past the star's sphere of influence, with no star pulling on the ship.</summary>
    public bool Adrift => Phase == Phase.Sublight && Primary.Parent == null && SubPosition.Length >= Primary.Edge;

    /// <summary>Open space is one flat frame, so the ship can coast from one star's reach into another's.</summary>
    void HandOver(ref Vec3d p, ref Vec3d v, double t)
    {
        if (Primary.Parent is { } up && p.Length >= Primary.Edge)
        {
            p += Primary.Position(t);
            v += Primary.Velocity(t);
            Primary = up;
        }
        var before = Primary;
        while (true)
        {
            var (at, _) = (p, v);
            var child = Primary.Children.FirstOrDefault(c => (at - c.Position(t)).Length < c.Edge);
            if (child == null) break;
            p -= child.Position(t);
            v -= child.Velocity(t);
            Primary = child;
        }
        if (Primary != before) return;
        if (Primary.Parent != null || p.Length < Primary.Edge) return;
        var ly = System.Position + p * Sublight.Scale;
        var next = Physics.Primary(G, _truth, ly);
        if (next < 0 || next == SystemStar) return;
        Primary = G.System(next);
        p = (ly - System.Position) / Sublight.Scale;
    }

    /// <summary>Close and slow enough to tie up, whether or not the ship has been away yet.</summary>
    public bool CanDock => Phase == Phase.Sublight && !Docked && LocalStation != null
        && (StationNow().Position - SubPosition).Length < Tuning.DockRange
        && (StationNow().Velocity - SubVelocity).Length < Tuning.DockRelativeSpeed;

    /// <summary>
    /// The pilot's assist toward this system's station: with approach, close on it, slowing as it nears;
    /// without, only match its velocity. Zero once there is nothing left worth a burn, null with no station.
    /// It stops well inside the docking speed, so letting go there leaves the ship able to dock.
    /// </summary>
    public Vec3d? AssistTowardStation(bool approach)
    {
        if (LocalStation == null) return null;
        var (sp, sv) = StationNow();
        var toward = sp - SubPosition;
        var want = sv - SubVelocity;
        if (approach) want += toward.Normalized() * Math.Min(0.02, toward.Length);
        return want.Length < Tuning.DockRelativeSpeed * 0.25 ? Vec3d.Zero : want.Normalized();
    }

    public bool RequestDock()
    {
        if (!CanDock) return false;
        Phase = Phase.Arrived;
        return true;
    }

    /// <summary>Takes on what was bought and loaded at the port while docked.</summary>
    public void Restock(ShipState ship, IEnumerable<Consignment> hold)
    {
        if (!Docked) return;
        Hold = hold.Select(c => c.Copy()).ToList();
        if (_station == OriginStation) _station = NextDoor(OriginStation);   // a job taken at the port for next door
        Fuel = ship.Fuel;
        Hull = ship.Hull;
        LifeSupply = ship.Life + (Time - _dockedDays);
    }

    public double SublightDeltaV => DeltaVRemaining / Tuning.SublightFuelFactor;

    /// <summary>Normal-space flight. thrust is a unit vector, or zero to coast.</summary>
    public void AdvanceSublight(double days, Vec3d thrust)
    {
        if (Phase != Phase.Sublight) return;
        var (p, v) = (SubPosition, SubVelocity);
        double left = days;
        while (left > 0 && Phase == Phase.Sublight)
        {
            var mu = p.Length < Primary.Edge ? Primary.Mu : 0;
            var dt = Math.Min(left, 0.02);
            var due = IdealNodesForTest ? Nodes.OrderBy(n => n.Time).FirstOrDefault() : null;
            if (due != null && due.Time <= AbsoluteTime + dt)
            {
                var wait = Math.Max(0, due.Time - AbsoluteTime);
                Sublight.Propagate(mu, ref p, ref v, wait);
                PassSublightTime(wait); left -= wait;
                var impulse = Sublight.Impulse(p, v, due);
                var done = SpendDeltaV(impulse.Length * Tuning.SublightFuelFactor) / Tuning.SublightFuelFactor;
                if (impulse.Length > 0) v += impulse.Normalized() * done;
                Nodes.Remove(due);
                continue;
            }
            var a = Vec3d.Zero;
            Thrusting = false;
            if (thrust.LengthSq > 0 && Fuel > 0)
            {
                var throttle = thrust.Length;
                var want = SublightThrust * throttle * dt;
                var done = SpendDeltaV(want * Tuning.SublightFuelFactor) / Tuning.SublightFuelFactor;
                a = thrust.Normalized() * (done / dt);
                Thrusting = true;
            }
            Sublight.Propagate(mu, ref p, ref v, dt, a);
            HandOver(ref p, ref v, AbsoluteTime + dt);
            PassSublightTime(dt); left -= dt;
            Heat = Math.Max(0, Heat - Tuning.CoolingPerDay * Heat * dt);
            if (p.Length < Primary.BodyRadius) { Kill(DeathCause.Collision); break; }
            if (LifeRemaining <= 0) { Kill(DeathCause.LifeSupport); break; }
            if (LocalStation != null)
            {
                var (sp, sv) = StationNow();
                var near = (sp - p).Length < Tuning.DockRange;
                if (near && GateBooked != null && G.GateLinks.TryGetValue(LocalStation.Id, out var exit)) PassGate(ref p, ref v, sp, sv, exit);
                // Docking is the pilot's call (RequestDock): passing close and slow is not enough.
            }
        }
        SubPosition = p; SubVelocity = v;
        SyncTruePosition();
        Observe();
    }

    int _bookedAt = -1;
    double _toll;

    /// <summary>The toll of the passage booked through this system's gate, or null if none is.</summary>
    public double? GateBooked => _bookedAt >= 0 && _bookedAt == LocalStation?.Id ? _toll : null;
    /// <summary>Tolls of passages made since the last dock; they are settled there.</summary>
    public double TollsDue { get; private set; }
    /// <summary>How many gates the ship has gone through, so the game can tell a passage happened.</summary>
    public int Passages { get; private set; }
    public bool AtGate => LocalStation is { } st && G.GateLinks.ContainsKey(st.Id);

    /// <summary>Books the passage: from now, coming within docking range of the gate takes the ship through.</summary>
    public void BookGate(double toll)
    {
        if (!AtGate || Docked || !Class.Gates) return;
        (_bookedAt, _toll) = (LocalStation!.Id, toll);
    }

    public void CancelGate() => _bookedAt = -1;

    /// <summary>
    /// Through the gate: position and velocity relative to it, taken in its own orbit's frame, come out
    /// the same relative to the exit gate in that one's frame. A fast entry is a fast exit.
    /// </summary>
    void PassGate(ref Vec3d p, ref Vec3d v, Vec3d gateAt, Vec3d gateSpeed, int exit)
    {
        var t = AbsoluteTime;
        var from = LocalStation!;
        var (t0, r0, n0) = Sublight.Frame(from.Position(t), from.Velocity(t));
        var to = G.StationAt(exit);
        var (t1, r1, n1) = Sublight.Frame(to.Position(t), to.Velocity(t));
        Vec3d Turn(Vec3d x) => t1 * x.Dot(t0) + r1 * x.Dot(r0) + n1 * x.Dot(n0);
        var (dp, dv) = (Turn(p - gateAt), Turn(v - gateSpeed));
        Primary = to.Host ?? G.System(to.Star);
        _station = exit;
        p = to.Position(t) + dp;
        v = to.Velocity(t) + dv;
        TollsDue += _toll;
        _bookedAt = -1;
        Passages++;
        Nodes.Clear();
    }

    void PassSublightTime(double days)
    {
        Time += days;
        if (Docked) _dockedDays += days;
    }

    /// <summary>
    /// Re-measures a node against the orbit it was planned to leave on: what is left of the burn is
    /// the difference between that orbit and the one the ship would coast on if it stopped now,
    /// compared at the node's time, or now once that has passed. Whatever the burn so far got
    /// wrong, aim or the orbit turning under a long burn, shows up here to be flown off.
    /// </summary>
    /// <param name="mu">Of the body both states are relative to; zero in open space.</param>
    public void Remeasure(Maneuver node, BurnTarget target, double mu)
    {
        Vec3d miss;
        if (AbsoluteTime < target.Time)
        {
            var (p, v) = (SubPosition, SubVelocity);
            Sublight.Propagate(mu, ref p, ref v, target.Time - AbsoluteTime);
            miss = target.After - v;
        }
        else
        {
            var (p, v) = (target.Position, target.After);
            Sublight.Propagate(mu, ref p, ref v, AbsoluteTime - target.Time);
            miss = v - SubVelocity;
        }
        var (t, r, n) = Sublight.Frame(target.Position, target.Before);
        node.Prograde = miss.Dot(t);
        node.Radial = miss.Dot(r);
        node.Normal = miss.Dot(n);
    }

    public static double ChartFactor(double closest) =>
        Math.Clamp((closest - 1.5) / Tuning.ChartRefineRangeLy, 0.03, 1);

    void Observe()
    {
        for (int i = 0; i < _truth.Length; i++)
        {
            ClosestApproach[i] = Math.Min(ClosestApproach[i], (_truth[i] - TruePosition).Length);
            _chart[i] = _truth[i] + G.Stars[i].ChartOffset * ChartFactor(ClosestApproach[i]);
        }
    }

    /// <summary>The star whose well the ship was last found in: a hint, checked before use.</summary>
    int _well = -1;

    /// <summary>The well the ship is in and the distance to its star; MaxValue in open space. Wells never overlap.</summary>
    public double NearestWell(out Star? star)
    {
        if (_well >= 0 && (_truth[_well] - TruePosition).Length is var d && d < G.Stars[_well].Soi)
        {
            star = G.Stars[_well];
            return d;
        }
        star = null; double best = double.MaxValue;
        _well = -1;
        for (int i = 0; i < _truth.Length; i++)
        {
            var s = G.Stars[i];
            var r = (_truth[i] - TruePosition).Length;
            if (r < s.Soi && r < best) { best = r; star = s; _well = i; }
        }
        return best;
    }

    /// <summary>Spends fuel by the rocket equation. Returns the delta-v actually delivered.</summary>
    double SpendDeltaV(double dv)
    {
        dv = Math.Min(dv, DeltaVRemaining);
        if (dv > 0) Docked = false;
        var m1 = Mass / Math.Exp(dv / Tuning.ExhaustVelocity);
        Fuel = Math.Max(0, m1 - Class.DryMass - CargoMass);
        return dv;
    }

    public double FuelFor(double dv) => Mass * (1 - Math.Exp(-dv / Tuning.ExhaustVelocity));

    /// <summary>Instant burn, as used for launch and mid-course corrections. Pointing is imperfect.</summary>
    public void Burn(Vec3d direction, double dv)
    {
        if (Phase is Phase.Dead or Phase.Arrived) return;
        var done = SpendDeltaV(dv);
        var dir = direction.Normalized();
        var perp = dir.AnyPerpendicular();
        var side = dir.Cross(perp);
        dir = (dir + perp * (_rng.Normal() * Tuning.BurnPointingErrorRad) + side * (_rng.Normal() * Tuning.BurnPointingErrorRad)).Normalized();
        Velocity += dir * done;
        if (Velocity.Length >= Tuning.StallSpeed && Phase == Phase.Stalled) Phase = Phase.Flight;
    }

    /// <summary>Star sight: costs time, resets accumulated position error.</summary>
    public void TakeSight()
    {
        if (Phase is not (Phase.Flight or Phase.Stalled)) return;
        Time += Tuning.SightDays;
        NavSigma = Tuning.SightError;
        _navError = _rng.NormalVec() * Tuning.SightError;
    }

    /// <summary>Hyperspace flight with no thrust, or along or against the velocity.</summary>
    public void Advance(double days, int thrust, bool shielded) =>
        Advance(days, Velocity.Normalized() * thrust, shielded);

    /// <param name="thrust">Thrust direction; its length is the throttle, zero coasts.</param>
    /// <param name="shielded">The shield faces the nearest star this tick.</param>
    public void Advance(double days, Vec3d thrust, bool shielded)
    {
        Thrusting = false;
        if (Phase is Phase.Stalled)
        {
            Time += days;
            Tick(days, shielded);
            if (DeltaVRemaining < Tuning.StallSpeed) Kill(DeathCause.Stranded);
            else if (LifeRemaining <= 0) Kill(DeathCause.LifeSupport);
            return;
        }
        if (Phase is not Phase.Flight) return;

        double left = days;
        while (left > 0 && Phase == Phase.Flight)
        {
            var pos = TruePosition; var vel = Velocity;
            double dt;
            if (thrust.LengthSq > 0 && Fuel > 0)
            {
                // Under power the path bends anywhere, so every star is checked as before.
                dt = Math.Min(left, Physics.SafeStep(G, _truth, pos, vel));
                var done = SpendDeltaV(FlightThrust * thrust.Length * dt);
                Physics.Rk4(G, _truth, ref pos, ref vel, thrust.Normalized() * (done / dt), dt, Class.Drag);
                Thrusting = true;
                _well = -1;
            }
            else dt = Physics.Coast(G, _truth, ref pos, ref vel, left, ref _well, Class.Drag);   // the same steps the forecast takes
            TruePosition = pos; Velocity = vel;
            Time += dt; left -= dt;
            Tick(dt, shielded);
            CheckStates();
        }
        Observe();
    }

    void Tick(double dt, bool shielded)
    {
        // Navigation error grows as a random walk.
        var q = Tuning.PositionDriftPerSqrtDay * Math.Sqrt(dt);
        _navError += _rng.NormalVec() * q;
        NavSigma = Math.Sqrt(NavSigma * NavSigma + q * q);

        var r = NearestWell(out var star);
        double gain = 0;
        if (star != null)
        {
            var flux = star.Mass / (r * r);
            gain = Tuning.HeatCoefficient * flux * (shielded ? Tuning.ShieldFactor : 1);
            Fuel = Math.Min(Class.Tank, Fuel + Tuning.ScoopCoefficient * flux * dt);
        }
        Heat = Math.Max(0, Heat + (gain - Tuning.CoolingPerDay * Math.Max(Heat, 0.05)) * dt);
        if (Heat > 1)
            Hull -= (Heat - 1) * Tuning.HullDamagePerExcessHeatDay * dt;
        foreach (var c in Hold)
            if (Heat > c.Mission.Cargo.HeatTolerance)
                c.Integrity = Math.Max(0, c.Integrity - (Heat - c.Mission.Cargo.HeatTolerance) * Tuning.CargoDamagePerExcessHeatDay * dt);
    }

    void CheckStates()
    {
        // A star's body lies inside its well, so only the well's star can be hit.
        var r = NearestWell(out var star);
        if (star != null && r < star.Radius) { Kill(DeathCause.Collision); return; }
        if (Hull <= 0) { Kill(DeathCause.HullFailure); return; }
        if (LifeRemaining <= 0) { Kill(DeathCause.LifeSupport); return; }

        var speed = Velocity.Length;
        if (star == null && speed < Tuning.StallSpeed)
        {
            Velocity = Vec3d.Zero;
            Phase = Phase.Stalled;
            if (DeltaVRemaining < Tuning.StallSpeed) Kill(DeathCause.Stranded); // too little left to re-enter FTL
            return;
        }
    }

    /// <summary>Smoke-test hooks; not reachable from play.</summary>
    public void PlaceForTest(Vec3d position, Vec3d velocity) { TruePosition = position; Velocity = velocity; }
    /// <summary>Puts the ship beside this system's station, drifting with it.</summary>
    public void PlaceNearStationForTest(double distance)
    {
        var st = LocalStation!;
        Primary = st.Host!;
        SubPosition = st.Position(AbsoluteTime) + st.PlaneNormal * distance;
        SubVelocity = st.Velocity(AbsoluteTime);
        SyncTruePosition();
    }

    /// <summary>Puts the ship at a state relative to the star, leaving any planet.</summary>
    public void PlaceSublightForTest(Vec3d position, Vec3d velocity) { Primary = G.System(SystemStar); SubPosition = position; SubVelocity = velocity; SyncTruePosition(); }
    public void RefillForTest() => Fuel = Class.Tank;
    public void ForceDeathForTest() => Kill(DeathCause.Collision);

    void Kill(DeathCause c) { Death = c; Phase = Phase.Dead; }
}

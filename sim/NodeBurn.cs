namespace Starbearer.Sim;

/// <summary>
/// Follows the burn on one node. What is left of the node is measured against the orbit the frozen
/// plan was to leave on, so aim errors and the orbit turning under a long burn show up and can be
/// flown off. The node counts as flown once the burn came close to nothing, whenever the pilot stops.
/// </summary>
public sealed class NodeBurn
{
    /// <summary>A node is done if, while burning on it, what was left fell to this share of the plan.</summary>
    public const double DoneFraction = 0.1;

    public enum Ending { None, Short, Done }

    /// <summary>What the node was planned to achieve, the body that is relative to, and whether that is open space.</summary>
    readonly record struct Aim(BurnTarget At, double Mu, int Body, bool Open);

    Aim? _aim;
    double _planned, _least;

    /// <summary>The node being flown, from its first ignition until it is done or another is flown.</summary>
    public Maneuver? Node { get; private set; }
    /// <summary>The engine has burned on the node since it was last stopped.</summary>
    public bool Live { get; private set; }
    /// <summary>The burn has gone past the node: what is left points behind the thrust.</summary>
    public bool Overshot { get; private set; }
    /// <summary>The last burn did not bring what is left down. An automatic burn stops here.</summary>
    public bool Stalled { get; private set; }
    public double Planned => _planned;

    /// <summary>Takes the plan as the reference again once it changes, as on a replan.</summary>
    public void Forget() => _aim = null;

    /// <summary>
    /// Before the ship moves. firedOn is the node the engine is burning on this frame, if any;
    /// frozen is the plan as it stood at ignition, or null while the plan is still open.
    /// </summary>
    public void Prepare(Voyage v, Maneuver? firedOn, OrbitForecast? frozen)
    {
        if (firedOn != null && Node != firedOn)
        {
            Node = firedOn;
            _planned = _least = firedOn.DeltaV;
            _aim = null;
        }
        if (Node != null && _aim == null && frozen != null) _aim = AimFrom(v, frozen, Node);
        // From the first burn on, the node keeps the axes of the orbit it was to leave: the plan's, or
        // without one the ship's at ignition.
        if (firedOn != null && Node == firedOn && Node.Locked == null)
            Node.Locked = _aim is { } aim ? Sublight.Frame(aim.At.Position, aim.At.Before) : Sublight.Frame(v.SubPosition, v.SubVelocity);
    }

    /// <summary>After the ship moved. flown is the velocity change the engine made on the node this frame, if it burned.</summary>
    public void Measure(Voyage v, Maneuver? firedOn, Vec3d? flown)
    {
        if (Node is not { } node || !v.Nodes.Contains(node)) return;
        var before = node.DeltaV;
        var aimed = _aim is { } aim && Holds(v, aim);
        if (aimed) v.Remeasure(node, _aim!.Value.At, _aim.Value.Mu);
        if (firedOn != node || flown is not { } burn) return;
        if (!aimed) CountOff(v, node, burn);
        Live = true;
        Overshot = Left(v, node, aimed).Dot(burn) < 0;
        Stalled = node.DeltaV >= before;
        _least = Math.Min(_least, node.DeltaV);
    }

    /// <summary>
    /// The engine was stopped. Close enough, and the node is taken off the plan; stopped short, it
    /// stays with what is left of it.
    /// </summary>
    public Ending Stop(Voyage v)
    {
        if (!Live) return Ending.None;
        Live = false;
        Overshot = false;
        Stalled = false;
        if (Node is not { } node || !v.Nodes.Contains(node)) { Node = null; return Ending.None; }
        if (_least > _planned * DoneFraction) return Ending.Short;
        v.Nodes.Remove(node);
        Node = null;
        return Ending.Done;
    }

    static Aim? AimFrom(Voyage v, OrbitForecast plan, Maneuver node)
    {
        if (plan.LegOf(node) is not { } leg || plan.StageOf(node) is not { } st) return null;
        var at = new BurnTarget(leg.Track[st.TrackIndex].Time, st.Position, st.Velocity,
            st.Velocity + Sublight.Impulse(st.Position, st.Velocity, node));
        var aim = new Aim(at, leg.IsDrift ? 0 : leg.Center?.Mu ?? 0, leg.IsDrift ? leg.Star : leg.Center?.Key ?? -1, leg.IsDrift);
        return Holds(v, aim) ? aim : null;
    }

    /// <summary>The ship is still in the frame the plan was made in, so the two can be compared.</summary>
    static bool Holds(Voyage v, Aim aim) =>
        aim.Open ? v.Adrift && v.Primary.Star == aim.Body : !v.Adrift && v.Primary.Key == aim.Body;

    /// <summary>What is left of the node, as a velocity change in space.</summary>
    Vec3d Left(Voyage v, Maneuver node, bool aimed) => aimed && _aim is { } aim
        ? Sublight.Impulse(aim.At.Position, aim.At.Before, node)
        : Sublight.Impulse(v.SubPosition, v.SubVelocity, node);

    /// <summary>Without a plan to compare against, as across a change of primary, just count off what was flown.</summary>
    static void CountOff(Voyage v, Maneuver node, Vec3d flown)
    {
        var (t, r, n) = Sublight.FrameOf(node, v.SubPosition, v.SubVelocity);
        node.Prograde -= flown.Dot(t);
        node.Radial -= flown.Dot(r);
        node.Normal -= flown.Dot(n);
    }
}

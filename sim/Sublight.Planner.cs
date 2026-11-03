namespace Starbearer.Sim;

public static partial class Sublight
{
    /// <summary>A burn at the next crossing of the station's plane that rotates the orbit into it. Null if already aligned.</summary>
    public static Maneuver? SuggestPlaneMatch(Center star, IOrbiter target, Vec3d p, Vec3d v, double t0)
    {
        if (RelativeInclination(target, p, v) < 1) return null;
        var mu = star.Mu;
        var n = target.PlaneNormal;
        var el = OrbitOf(mu, p, v);
        var span = el.Bound ? el.Period : 2 * Math.PI / target.Omega;
        var dt = span / 400;
        var (pos, vel, t) = (p, v, t0);
        var lead = Math.Min(0.1, span / 100);
        Propagate(mu, ref pos, ref vel, lead); t += lead;
        for (int i = 0; i < 400; i++)
        {
            var (p0, v0, t1) = (pos, vel, t);
            Propagate(mu, ref pos, ref vel, dt); t += dt;
            if (p0.Dot(n) * pos.Dot(n) > 0) continue;
            var inPlane = vel - n * vel.Dot(n);
            var dv = inPlane.Normalized() * vel.Length - vel;
            return Decompose(t, pos, vel, dv);
        }
        return null;
    }

    /// <summary>
    /// Burns that stop a ship falling through a body: trim the pass to the target's height if it is still
    /// coming in, then circle at the low point. Null if the orbit is already safely held.
    /// </summary>
    public static List<Maneuver>? SuggestCapture(Center c, IOrbiter target, Vec3d p, Vec3d v, double t0)
    {
        var el = OrbitOf(c.Mu, p, v);
        if (el.Bound && el.Apoapsis < c.Edge * 0.9 && el.Periapsis > c.BodyRadius * 1.2) return null;
        var plan = new List<Maneuver>();
        var lead = 0.05 * Math.Sqrt(p.LengthSq * p.Length / c.Mu);
        var t = t0 + lead;
        Propagate(c.Mu, ref p, ref v, lead);
        if (p.Dot(v) < 0)
        {
            var want = Math.Max(target.OrbitRadius, c.BodyRadius * 2.5);
            var (_, radial, _) = Frame(p, v);
            // Trims in steps of the ship's own speed, so the search fits a moon as well as a star.
            var unit = v.Length * 0.02;
            var trim = Enumerable.Range(-60, 121).Select(i => i * unit)
                .OrderBy(dv => Math.Abs(OrbitOf(c.Mu, p, v + radial * dv).Periapsis - want) / want + Math.Abs(dv) / v.Length * 0.1).First();
            if (Math.Abs(trim) > unit * 0.1)
            {
                plan.Add(Decompose(t, p, v, radial * trim));
                v += radial * trim;
            }
            // Coast to the low point.
            for (int i = 0; i < 4000 && p.Dot(v) < 0; i++)
            {
                var step = 0.01 * Math.Sqrt(p.LengthSq * p.Length / c.Mu);
                Propagate(c.Mu, ref p, ref v, step);
                t += step;
            }
        }
        var (tangent, _, _) = Frame(p, v);
        var along = (tangent - p.Normalized() * tangent.Dot(p.Normalized())).Normalized();
        plan.Add(Decompose(t, p, v, along * Math.Sqrt(c.Mu / p.Length) - v));
        return plan;
    }

    /// <summary>A burn along the way of travel that carries the ship out of a body's sphere of influence.</summary>
    public static Maneuver SuggestEscape(Center c, Vec3d p, Vec3d v, double t0)
    {
        var needed = Math.Sqrt(2 * c.Mu / p.Length) * 1.1 - v.Length;
        var (_, radial, _) = Frame(p, v);
        // A ship falling at the body has to miss it first.
        var unit = v.Length * 0.02;
        var clear = p.Dot(v) < 0 && OrbitOf(c.Mu, p, v).Periapsis < c.BodyRadius * 1.5
            ? Enumerable.Range(-60, 121).Select(i => i * unit).Where(dv => OrbitOf(c.Mu, p, v + radial * dv).Periapsis > c.BodyRadius * 2).OrderBy(Math.Abs).FirstOrDefault()
            : 0;
        var soon = Math.Min(0.02, 0.01 * Math.Sqrt(p.LengthSq * p.Length / c.Mu));   // a moment, beside the orbit
        return new Maneuver { Time = t0 + soon, Prograde = Math.Max(needed, 0), Radial = clear };
    }

    /// <summary>A step of delta-v that means the same around a star as around a moon.</summary>
    static double DvUnit(IOrbiter target) => target.OrbitRadius * target.Omega * 0.09;

    /// <summary>How long a transfer search looks ahead: scaled to the orbits at hand, not to days.</summary>
    static double Horizon(Elements el, IOrbiter target) =>
        Math.Max(2 * Math.PI / target.Omega, el.Bound ? el.Period * 1.5 : 2 * Math.PI / target.Omega);

    /// <summary>
    /// How badly a path misses, in units of the target's orbital speed (as the delta-v it is weighed
    /// against), or null if it can't be flown. Reaching a body's sphere is a hit. The distance counts
    /// as a share of the target's orbit, so a miss means the same around a star as around a moon.
    /// </summary>
    static double? Miss(OrbitForecast f, IOrbiter target)
    {
        if (f.Impact) return null;
        var speed = target.OrbitRadius * target.Omega;
        // Into the body's sphere: the faster it comes in, the more it costs to stay. The pass is best a
        // fifth of the way in, clear of the body: near the edge a small error misses the sphere, near the
        // body it falls in or comes by too fast to hold.
        if (f.Enter is { } e && e.Body == target)
        {
            var body = e.Body;
            var low = OrbitOf(body.Mu, e.Position, e.Velocity).Periapsis;
            if (low < body.BodyRadius * 1.5) return null;
            var want = Math.Max(body.Edge * 0.2, body.BodyRadius * 3);
            return e.Velocity.Length / speed + 2 * Math.Abs(low - want) / body.Edge;
        }
        // Another body in the way cuts the path short: a bad pass, but one a better burn can clear.
        return f.Closest is { } c ? c.Distance / target.OrbitRadius * 9 + c.RelativeSpeed / speed + (f.Enter != null || f.Exit != null ? 5 : 0) : null;
    }

    /// <summary>
    /// A plan's price in the same units as Miss: its delta-v, and the days until it gets there, a whole
    /// orbit of the target's costing as much as its orbital speed. Life support runs while waiting for
    /// a cheap window, so a little more delta-v that saves months is worth it.
    /// </summary>
    static double Cost(Maneuver m, OrbitForecast f, IOrbiter target, double t0)
    {
        var arrive = f.Enter is { } e && e.Body == target ? e.Time : f.Closest?.Time ?? m.Time;
        return m.DeltaV / (target.OrbitRadius * target.Omega) + (arrive - t0) / DaysPerSpeed;
    }

    /// <summary>The days that cost as much as the target's orbital speed of delta-v.</summary>
    public const double DaysPerSpeed = 300;

    /// <summary>A rough transfer to refine by hand: scans departure time and prograde, after any burns already planned.</summary>
    public static Maneuver SuggestTransfer(Center star, IOrbiter target, Vec3d p, Vec3d v, double t0, IReadOnlyList<Maneuver>? planned = null)
    {
        var baseNodes = planned ?? Array.Empty<Maneuver>();
        var el = OrbitOf(star.Mu, p, v);
        var span = el.Bound ? Math.Max(el.Period, 2 * Math.PI / target.Omega) * 1.5 : 4 * Math.PI / target.Omega;
        var horizon = Horizon(el, target);
        var unit = DvUnit(target);
        var start = Math.Max(t0, baseNodes.Count > 0 ? baseNodes.Max(n => n.Time) : t0) + Math.Min(0.2, span / 100);
        Maneuver best = new() { Time = start };
        double bestScore = double.MaxValue;
        for (int i = 0; i <= 48; i++)
            for (int j = -16; j <= 16; j++)
            {
                var m = new Maneuver { Time = start + span * i / 48, Prograde = j * unit };
                var f = Predict(star, target, p, v, t0, baseNodes.Append(m), horizon, 120, coarse: true);
                var miss = Miss(f, target);
                if (miss == null) continue;
                var score = miss.Value + Cost(m, f, target, t0);
                if (score < bestScore) { bestScore = score; best = m; }
            }
        return best;
    }

    /// <summary>A burn at the closest approach that matches the station's velocity.</summary>
    public static Maneuver? MatchAtClosest(Center star, IOrbiter target, Vec3d p, Vec3d v, double t0, IReadOnlyList<Maneuver> nodes, double horizon)
    {
        var f = Predict(star, target, p, v, t0, nodes, horizon, 800);
        if (f.Closest is not { } c) return null;
        var mu = star.Mu;
        var (ps, vs, t) = (p, v, t0);
        foreach (var n in nodes.OrderBy(n => n.Time))
        {
            Propagate(mu, ref ps, ref vs, Math.Max(n.Time - t, 0));
            t = Math.Max(n.Time, t);
            vs += Impulse(ps, vs, n);
        }
        Propagate(mu, ref ps, ref vs, c.Time - t);
        return Decompose(c.Time, ps, vs, target.Velocity(c.Time) - vs);
    }

    /// <summary>
    /// The transfer computer: plane match if needed, then a pattern search for a departure burn that
    /// meets the station, then a burn matching its velocity. Null when nothing comes close.
    /// </summary>
    public static List<Maneuver>? PlanRendezvous(Center star, IOrbiter target, Vec3d p, Vec3d v, double t0)
    {
        if (SuggestCapture(star, target, p, v, t0) is { } capture) return capture;
        var plan = new List<Maneuver>();
        if (SuggestPlaneMatch(star, target, p, v, t0) is { } plane) plan.Add(plane);
        var first = SuggestTransfer(star, target, p, v, t0, plan);
        var horizon = Horizon(OrbitOf(star.Mu, p, v), target);
        double Score(Maneuver m)
        {
            var f = Predict(star, target, p, v, t0, plan.Append(m), horizon, 160, coarse: true);
            return Miss(f, target) is { } miss ? miss + Cost(m, f, target, t0) : double.MaxValue;
        }
        var scale = 2 * Math.PI / target.Omega / 30;
        var earliest = plan.Count > 0 ? plan[0].Time + Math.Min(0.1, scale / 2) : t0 + Math.Min(0.05, scale / 4);
        var bestS = Score(first);
        double stepT = Math.Min(0.5, scale), stepD = DvUnit(target);
        // A way into a body is refined too: Miss weighs how deep the pass goes, so shaving delta-v stops short of grazing.
        for (int round = 0; round < 30; round++)
        {
            var improved = false;
            foreach (var (dt, dp, dr, dn) in new[] { (stepT, 0.0, 0.0, 0.0), (-stepT, 0.0, 0.0, 0.0), (0.0, stepD, 0.0, 0.0), (0.0, -stepD, 0.0, 0.0), (0.0, 0.0, stepD, 0.0), (0.0, 0.0, -stepD, 0.0), (0.0, 0.0, 0.0, stepD), (0.0, 0.0, 0.0, -stepD) })
            {
                var m = new Maneuver { Time = Math.Max(earliest, first.Time + dt), Prograde = first.Prograde + dp, Radial = first.Radial + dr, Normal = first.Normal + dn };
                var s = Score(m);
                if (s < bestS) { bestS = s; first = m; improved = true; }
            }
            if (!improved) { stepT /= 2; stepD /= 2; }
        }
        plan.Add(first);
        var result = Predict(star, target, p, v, t0, plan, horizon, 600);
        var reached = result.Enter?.Body == target;
        if (!reached && (result.Closest is not { } meet || meet.Distance > 0.5 * target.OrbitRadius)) return null;
        // A body is only a way in: its sphere of influence takes over before there is anything to match.
        if (target is Station && MatchAtClosest(star, target, p, v, t0, plan, horizon) is { } match) plan.Add(match);
        return plan;
    }
}

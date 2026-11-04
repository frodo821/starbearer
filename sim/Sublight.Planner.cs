namespace Starbearer.Sim;

public static partial class Sublight
{
    /// <summary>
    /// A burn at a crossing of the target's plane that rotates the orbit into it: of the crossings in the
    /// next orbit, the cheapest, which is the one farther out where the ship is slower. Null if already aligned.
    /// </summary>
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
        Maneuver? best = null;
        for (int i = 0; i < 400; i++)
        {
            var p0 = pos;
            Propagate(mu, ref pos, ref vel, dt); t += dt;
            if (p0.Dot(n) * pos.Dot(n) > 0) continue;
            var inPlane = vel - n * vel.Dot(n);
            var m = Decompose(t, pos, vel, inPlane.Normalized() * vel.Length - vel);
            if (best == null || m.DeltaV < best.DeltaV) best = m;
        }
        return best;
    }

    /// <summary>
    /// Burns that stop a ship falling through a body. Coming in, the pass is trimmed only if it would hit
    /// the body or keeps above the target's height. At the low point, if the target circles higher, the
    /// ship is caught into a long orbit reaching up to it and circled there, at the top, where it is slow:
    /// far cheaper than circling deep in a giant's well. Otherwise it circles at the low point.
    /// Null if the orbit is already safely held.
    /// </summary>
    public static List<Maneuver>? SuggestCapture(Center c, IOrbiter target, Vec3d p, Vec3d v, double t0)
    {
        var el = OrbitOf(c.Mu, p, v);
        if (el.Bound && el.Apoapsis < c.Edge * 0.9 && el.Periapsis > c.BodyRadius * 1.2) return null;
        var plan = new List<Maneuver>();
        // A moment ahead to plan from: a twentieth of the time to fall around here, or less on a fast pass,
        // which would be through the low point and gone before a slow orbit's moment was out.
        var lead = Math.Min(0.05 * Math.Sqrt(p.LengthSq * p.Length / c.Mu), 0.02 * p.Length / Math.Max(v.Length, 1e-12));
        var t = t0 + lead;
        Propagate(c.Mu, ref p, ref v, lead);
        var want = Math.Max(target.OrbitRadius, c.BodyRadius * 2.5);
        if (p.Dot(v) < 0)
        {
            var (_, radial, _) = Frame(p, v);
            var low = OrbitOf(c.Mu, p, v).Periapsis;
            // Trims in steps of the ship's own speed, so the search fits a moon as well as a star.
            var unit = v.Length * 0.02;
            var trim = Enumerable.Range(-60, 121).Select(i => i * unit)
                .OrderBy(dv => Math.Abs(OrbitOf(c.Mu, p, v + radial * dv).Periapsis - want) / want + Math.Abs(dv) / v.Length * 0.1).First();
            if (low > c.BodyRadius * 1.5 && low <= want) trim = 0;   // safe and below the target: caught from where it is
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
        var r = p.Length;
        if (want > r * 1.2 && want < c.Edge * 0.9)
        {
            // Caught into an orbit from here up to the target's height, then circled at its top half an orbit later.
            var a = (r + want) / 2;
            var vLow = along * Math.Sqrt(c.Mu * (2 / r - 1 / a));
            plan.Add(Decompose(t, p, v, vLow - v));
            var (pt, vt) = (p, vLow);
            var half = Math.PI * Math.Sqrt(a * a * a / c.Mu);
            Propagate(c.Mu, ref pt, ref vt, half);
            var (tt, _, _) = Frame(pt, vt);
            var top = (tt - pt.Normalized() * tt.Dot(pt.Normalized())).Normalized();
            plan.Add(Decompose(t + half, pt, vt, top * Math.Sqrt(c.Mu / pt.Length) - vt));
            return plan;
        }
        plan.Add(Decompose(t, p, v, along * Math.Sqrt(c.Mu / r) - v));
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
        // Another body in the way cuts the path short: a bad pass, but one a better burn can clear. A body
        // not reached at all is worse than any way into it, however slow: waiting for a window costs days,
        // giving up costs the job.
        return f.Closest is { } c ? c.Distance / target.OrbitRadius * 9 + c.RelativeSpeed / speed + (f.Enter != null || f.Exit != null ? 5 : 0) + (target is Center ? NotReached : 0) : null;
    }

    /// <summary>
    /// A plan's price in the same units as Miss: its delta-v, and the days until it gets there, a whole
    /// orbit of the target's costing as much as its orbital speed. Life support runs while waiting for
    /// a cheap window, so a little more delta-v that saves months is worth it.
    /// </summary>
    static double Cost(Maneuver m, OrbitForecast f, IOrbiter target, double t0) => Cost(m.DeltaV, m.Time, f, target, t0);

    static double Cost(double deltaV, double lastBurn, OrbitForecast f, IOrbiter target, double t0)
    {
        var arrive = f.Enter is { } e && e.Body == target ? e.Time : f.Closest?.Time ?? lastBurn;
        return deltaV / (target.OrbitRadius * target.Omega) + (arrive - t0) / DaysPerSpeed;
    }

    /// <summary>What missing a body costs, beside how far: more than the slowest way in (see Cost).</summary>
    const double NotReached = 10;

    /// <summary>The days that cost as much as the target's orbital speed of delta-v.</summary>
    public const double DaysPerSpeed = 600;

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
    /// The transfer computer. Tries several ways and keeps the best by how near it brings the ship and
    /// what the whole of it costs: a plane match first, a transfer that leans out of the plane itself,
    /// and a small correction to the way the ship is already going (often all a near miss needs). Each
    /// is refined by a pattern search. With a budget, nothing dearer than it is offered; if no way fits,
    /// the cheapest is. Then, for a station, a burn matching its velocity. Null when nothing comes close.
    /// </summary>
    public static List<Maneuver>? PlanRendezvous(Center star, IOrbiter target, Vec3d p, Vec3d v, double t0, double budget = double.MaxValue)
    {
        if (SuggestCapture(star, target, p, v, t0) is { } capture) return capture;
        // On the way to a body, at least half is kept for what follows inside it: catching the orbit and
        // the station. A plan that spent it all to arrive sooner would arrive with nothing to stop.
        if (target is Center) budget *= 0.5;
        var horizon = Horizon(OrbitOf(star.Mu, p, v), target);
        var scale = 2 * Math.PI / target.Omega / 30;

        // A plan's score, and whether it keeps to the budget.
        (double Score, bool Fits) Rate(List<Maneuver> before, Maneuver m)
        {
            var nodes = before.Append(m).ToList();
            var f = Predict(star, target, p, v, t0, nodes, horizon, 160, coarse: true);
            if (Miss(f, target) is not { } miss) return (double.MaxValue, false);
            var dv = nodes.Sum(n => n.DeltaV);
            return (miss + Cost(dv, nodes.Max(n => n.Time), f, target, t0), dv <= budget);
        }
        (Maneuver Burn, double Score, bool Fits) Refine(List<Maneuver> before, Maneuver first)
        {
            var earliest = before.Count > 0 ? before[0].Time + Math.Min(0.1, scale / 2) : t0 + Math.Min(0.05, scale / 4);
            first = new Maneuver { Time = Math.Max(earliest, first.Time), Prograde = first.Prograde, Radial = first.Radial, Normal = first.Normal };
            // Over the budget counts as worse than any plan within it, so the search walks back inside.
            double Key((double Score, bool Fits) r) => r.Fits ? r.Score : 1e6 + r.Score;
            var best = Rate(before, first);
            double stepT = Math.Min(0.5, scale), stepD = DvUnit(target);
            // A way into a body is refined too: Miss weighs how deep the pass goes, so shaving delta-v stops short of grazing.
            for (int round = 0; round < 30; round++)
            {
                var improved = false;
                foreach (var (dt, dp, dr, dn) in new[] { (stepT, 0.0, 0.0, 0.0), (-stepT, 0.0, 0.0, 0.0), (0.0, stepD, 0.0, 0.0), (0.0, -stepD, 0.0, 0.0), (0.0, 0.0, stepD, 0.0), (0.0, 0.0, -stepD, 0.0), (0.0, 0.0, 0.0, stepD), (0.0, 0.0, 0.0, -stepD) })
                {
                    var m = new Maneuver { Time = Math.Max(earliest, first.Time + dt), Prograde = first.Prograde + dp, Radial = first.Radial + dr, Normal = first.Normal + dn };
                    var r = Rate(before, m);
                    if (Key(r) < Key(best)) { best = r; first = m; improved = true; }
                }
                if (!improved) { stepT /= 2; stepD /= 2; }
            }
            return (first, best.Score, best.Fits);
        }

        var ways = new List<(List<Maneuver> Before, Maneuver Burn, double Score, bool Fits)>();
        void Try(List<Maneuver> before, Maneuver seed) { var (m, sc, fits) = Refine(before, seed); ways.Add((before, m, sc, fits)); }
        var none = new List<Maneuver>();
        if (SuggestPlaneMatch(star, target, p, v, t0) is { } plane)
        {
            var withPlane = new List<Maneuver> { plane };
            Try(withPlane, SuggestTransfer(star, target, p, v, t0, withPlane));
        }
        Try(none, SuggestTransfer(star, target, p, v, t0));
        Try(none, new Maneuver { Time = t0 });   // a correction to the present course
        // Slowing or speeding up a little to come round again ahead or behind: what a ship sharing the
        // target's orbit, but away from it, needs, and the transfer's coarse scan steps over.
        foreach (var k in new[] { -0.6, -0.3, 0.3, 0.6 }) Try(none, new Maneuver { Time = t0, Prograde = k * DvUnit(target) });
        // The best that holds up when looked at closely: the search's forecast is coarse, and a body grazed
        // there can be missed in fact. A body has to be reached; a station, come near.
        foreach (var w in ways.Where(w => w.Score < double.MaxValue).OrderBy(w => w.Fits ? 0 : 1).ThenBy(w => w.Fits ? w.Score : w.Before.Append(w.Burn).Sum(n => n.DeltaV)))
        {
            var plan = w.Before.Append(w.Burn).ToList();
            var result = Predict(star, target, p, v, t0, plan, horizon, 600);
            var reached = result.Enter?.Body == target;
            if (target is Center ? !reached : !reached && (result.Closest is not { } meet || meet.Distance > 0.5 * target.OrbitRadius)) continue;
            // A body is only a way in: its sphere of influence takes over before there is anything to match.
            if (target is Station && MatchAtClosest(star, target, p, v, t0, plan, horizon) is { } match) plan.Add(match);
            return plan;
        }
        return null;
    }
}

namespace Starbearer.Sim;

public static partial class Sublight
{
    /// <summary>A burn at the next crossing of the station's plane that rotates the orbit into it. Null if already aligned.</summary>
    public static Maneuver? SuggestPlaneMatch(Star star, Station target, Vec3d p, Vec3d v, double t0)
    {
        if (RelativeInclination(target, p, v) < 1) return null;
        var mu = Mu(star);
        var n = target.PlaneNormal;
        var el = OrbitOf(mu, p, v);
        var span = el.Bound ? el.Period : 30;
        var dt = span / 400;
        var (pos, vel, t) = (p, v, t0);
        Propagate(mu, ref pos, ref vel, 0.1); t += 0.1;
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

    /// <summary>A rough transfer to refine by hand: scans departure time and prograde, after any burns already planned.</summary>
    public static Maneuver SuggestTransfer(Star star, Station target, Vec3d p, Vec3d v, double t0, IReadOnlyList<Maneuver>? planned = null)
    {
        var baseNodes = planned ?? Array.Empty<Maneuver>();
        var el = OrbitOf(Mu(star), p, v);
        var span = el.Bound ? Math.Max(el.Period, 2 * Math.PI / target.Omega) * 1.5 : 30;
        var horizon = Math.Max(2 * Math.PI / target.Omega, 20);
        var start = Math.Max(t0, baseNodes.Count > 0 ? baseNodes.Max(n => n.Time) : t0) + 0.2;
        Maneuver best = new() { Time = start };
        double bestScore = double.MaxValue;
        for (int i = 0; i <= 48; i++)
            for (int j = -16; j <= 16; j++)
            {
                var m = new Maneuver { Time = start + span * i / 48, Prograde = j * 0.02 };
                var f = Predict(star, target, p, v, t0, baseNodes.Append(m), horizon, 120, coarse: true);
                if (f.Impact || f.Closest is not { } c) continue;
                var score = c.Distance * 6 + m.DeltaV + c.RelativeSpeed;
                if (score < bestScore) { bestScore = score; best = m; }
            }
        return best;
    }

    /// <summary>A burn at the closest approach that matches the station's velocity.</summary>
    public static Maneuver? MatchAtClosest(Star star, Station target, Vec3d p, Vec3d v, double t0, IReadOnlyList<Maneuver> nodes, double horizon)
    {
        var f = Predict(star, target, p, v, t0, nodes, horizon, 800);
        if (f.Closest is not { } c) return null;
        var mu = Mu(star);
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
    public static List<Maneuver>? PlanRendezvous(Star star, Station target, Vec3d p, Vec3d v, double t0)
    {
        var plan = new List<Maneuver>();
        if (SuggestPlaneMatch(star, target, p, v, t0) is { } plane) plan.Add(plane);
        var first = SuggestTransfer(star, target, p, v, t0, plan);
        var horizon = Math.Max(2 * Math.PI / target.Omega, 20);
        double Score(Maneuver m)
        {
            var f = Predict(star, target, p, v, t0, plan.Append(m), horizon, 160, coarse: true);
            return f.Impact || f.Closest is not { } c ? double.MaxValue : c.Distance * 6 + m.DeltaV + c.RelativeSpeed;
        }
        var earliest = plan.Count > 0 ? plan[0].Time + 0.1 : t0 + 0.05;
        var bestS = Score(first);
        double stepT = 0.5, stepD = 0.02;
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
        if (result.Closest is not { } meet || meet.Distance > 0.15) return null;
        if (MatchAtClosest(star, target, p, v, t0, plan, horizon) is { } match) plan.Add(match);
        return plan;
    }
}

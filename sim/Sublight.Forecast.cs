namespace Starbearer.Sim;

public static partial class Sublight
{
    /// <summary>
    /// Continues a forecast past its Disengage: the run through open space, the star that takes over,
    /// and its pass, then the same from there. Burns in nodes that the forecast did not reach are flown
    /// where they fall. Arriving from outside, an unpowered path always leaves again or hits the star,
    /// so each leg ends unless a burn captures it.
    /// </summary>
    public static void FollowOn(Galaxy g, int star, OrbitForecast f, IEnumerable<Maneuver>? nodes = null, int legs = 3)
    {
        if (f.Exit is not { } e) return;
        var rest = (nodes ?? Array.Empty<Maneuver>()).Where(n => f.Stages.All(s => s.Node != n)).OrderBy(n => n.Time).ToList();
        var (drift, velocity) = FlyDrift(g, star, e, rest, out var next);
        f.Drift = drift;
        f.Next = next;
        if (legs <= 0 || next is not { } n) return;
        var to = g.Stars[n.Star];
        var arrive = (n.Point - to.Position) / Scale * (1 - 1e-9);   // on the boundary would count as already out
        var el = OrbitOf(Mu(to), arrive, velocity);
        var span = el.Bound ? el.Period : 2 * SystemRadius(to) / velocity.Length;
        f.Beyond = Predict(to, g.StationOrNull(to.Id), arrive, velocity, n.Time, rest, span);
        FollowOn(g, n.Star, f.Beyond, rest, legs - 1);
    }

    /// <summary>The straight run from a Disengage to the next star, flying and removing the burns in rest
    /// that fall on it. Stages stay in the frame of the star left behind, as the ship's state does.</summary>
    static (OrbitForecast Drift, Vec3d Velocity) FlyDrift(Galaxy g, int star, Disengage e, List<Maneuver> rest, out Handover? next)
    {
        var from = g.Stars[star];
        var drift = new OrbitForecast { Star = star, IsDrift = true };
        var (p, v, t) = (e.Position, e.Velocity, e.Time);
        (drift.AfterPosition, drift.AfterVelocity) = (p, v);
        drift.Track.Add((t, p));
        void RunTo(double when)
        {
            const int Samples = 32;
            var start = p;
            for (int i = 1; i <= Samples; i++)
                drift.Track.Add((t + (when - t) * i / Samples, start + v * ((when - t) * i / Samples)));
            p = start + v * (when - t);
            t = when;
        }
        while (true)
        {
            next = NextPrimary(g, star, from.Position + p * Scale, v, t);
            var end = next?.Time ?? Math.Max(t + OpenRunDays, rest.Count > 0 ? rest[^1].Time + OpenRunDays : 0);
            if (rest.Count == 0 || rest[0].Time >= end) { RunTo(end); break; }
            RunTo(Math.Max(rest[0].Time, t));
            var before = (p, v);
            v += Impulse(p, v, rest[0]);
            drift.Stages.Add(new Stage(rest[0], before.p, before.v, OrbitOf(Mu(from), p, v), drift.Track.Count - 1));
            (drift.AfterPosition, drift.AfterVelocity) = (p, v);
            rest.RemoveAt(0);
        }
        drift.DrawnCount = drift.Track.Count;
        return (drift, v);
    }

    /// <summary>The whole path from a state: this star's reach, and everything past it.</summary>
    public static OrbitForecast PredictPath(Galaxy g, Star star, Vec3d p, Vec3d v, double t0, IReadOnlyCollection<Maneuver> nodes, double horizon)
    {
        var f = Predict(star, g.StationOrNull(star.Id), p, v, t0, nodes, horizon);
        FollowOn(g, star.Id, f, nodes);
        return f;
    }

    /// <summary>One coasted forecast step, for finding what happened partway through it.</summary>
    readonly record struct Hop(double Mu, double T0, Vec3d P0, Vec3d V0, double T1)
    {
        public (Vec3d P, Vec3d V, double T) At(double fraction)
        {
            var (p, v) = (P0, V0);
            var when = T0 + (T1 - T0) * fraction;
            Propagate(Mu, ref p, ref v, when - T0);
            return (p, v, when);
        }
    }

    /// <summary>Watches a path step by step for what matters about one station.</summary>
    sealed class StationWatch
    {
        readonly double _mu;
        readonly Station _station;
        readonly OrbitForecast _result;
        Approach? _best;
        (double T, Vec3d P, Vec3d V, double Span) _bracket;
        Crossing? _crossing;

        public StationWatch(double mu, Station station, OrbitForecast result) => (_mu, _station, _result) = (mu, station, result);

        public void PlaneCrossing(Hop hop, Vec3d p)
        {
            var (before, after) = (hop.P0.Dot(_station.PlaneNormal), p.Dot(_station.PlaneNormal));
            if (before * after < 0 && _result.PlaneCrossings.Count < 4)
                _result.PlaneCrossings.Add((hop.At(before / (before - after)).P, before < 0));
        }

        public void Observe(Hop hop, Vec3d p, Vec3d v)
        {
            var rBefore = hop.P0.Length;
            if (_crossing == null && (rBefore - _station.Radius) * (p.Length - _station.Radius) <= 0)
            {
                var (cp, cv, ct) = hop.At(p.Length == rBefore ? 1 : (_station.Radius - rBefore) / (p.Length - rBefore));
                var sc = _station.Position(ct);
                var (_, _, up) = Frame(cp, cv);
                var lead = Math.Atan2(cp.Cross(sc).Dot(up), cp.Dot(sc)) * 180 / Math.PI;
                _crossing = new Crossing(ct, cp, sc, lead);
            }
            var sp = _station.Position(hop.T1);
            var d = (sp - p).Length;
            if (_best == null || d < _best.Value.Distance)
            {
                _best = new Approach(hop.T1, d, (_station.Velocity(hop.T1) - v).Length, p, sp);
                _bracket = (hop.T0, hop.P0, hop.V0, (hop.T1 - hop.T0) * 2.5);
            }
        }

        public void Finish()
        {
            _result.Closest = _best == null ? null : RefineApproach(_mu, _station, _bracket);
            _result.Crossing = _crossing;
        }
    }

    /// <summary>The point in a step where the path crosses the edge of the sphere of influence.</summary>
    static Disengage FindExit(Hop hop, double edge, bool coarse)
    {
        double lo = 0, hi = 1;
        for (int i = 0; i < 30 && !coarse; i++)
        {
            var mid = (lo + hi) / 2;
            if (hop.At(mid).P.Length < edge) lo = mid; else hi = mid;
        }
        var (p, v, t) = hop.At(hi);
        return new Disengage(t, p, v);
    }

    /// <summary>
    /// Samples the path. Unless coarse, steps are also limited by the local orbital time, so the line
    /// stays smooth through periapsis instead of being cut across by long chords that shift on every
    /// refresh. Coarse is for scans that run thousands of forecasts and never draw them.
    /// The search runs to horizon, but always far enough to close a lap or reach the edge of the
    /// sphere of influence, where it stops: burns past that point are not flown.
    /// </summary>
    public static OrbitForecast Predict(Star star, Station? target, Vec3d p, Vec3d v, double t0, IEnumerable<Maneuver> nodes, double horizon, int samples = 500, bool coarse = false)
    {
        var mu = Mu(star);
        var res = new OrbitForecast { Star = star.Id };
        var pending = nodes.OrderBy(n => n.Time).ToList();
        var lastNode = pending.Count > 0 ? Math.Max(pending[^1].Time, t0) : t0;
        var end = lastNode + horizon;
        var dt = (end - t0) / samples;
        var t = t0;
        int next = 0;
        var edge = SystemRadius(star);
        var lapEnd = double.PositiveInfinity;
        void OpenLap(Vec3d from, Vec3d speed, double when)
        {
            var el = OrbitOf(mu, from, speed);
            var closed = el.Bound && el.Apoapsis < edge;
            lapEnd = closed ? when + el.Period : double.PositiveInfinity;
            if (!coarse) end = Math.Max(end, closed ? lapEnd : when + 3000);
        }
        if (pending.Count == 0) OpenLap(p, v, t);
        var watch = target != null ? new StationWatch(mu, target, res) : null;
        var (afterP, afterV) = (p, v);
        res.Track.Add((t, p));
        if (p.Length >= edge)
        {
            res.Exit = new Disengage(t, p, v);
            res.DrawnCount = 1;
            res.After = OrbitOf(mu, p, v);
            (res.AfterPosition, res.AfterVelocity) = (p, v);
            return res;
        }
        while (t < end - 1e-9)
        {
            var r0 = p.Length;
            var step = coarse ? dt : Math.Min(dt, TrackStep * Math.Sqrt(r0 * r0 * r0 / mu));
            var stepEnd = Math.Min(t + step, end);
            if (t < lapEnd - 1e-9) stepEnd = Math.Min(stepEnd, lapEnd);   // the drawn lap closes on a sample
            while (next < pending.Count && pending[next].Time < stepEnd)
            {
                var when = Math.Max(pending[next].Time, t);
                Propagate(mu, ref p, ref v, when - t);
                t = when;
                var before = (p, v);
                v += Impulse(p, v, pending[next]);
                res.Stages.Add(new Stage(pending[next], before.p, before.v, OrbitOf(mu, p, v), res.Track.Count));
                res.Track.Add((t, p));
                next++;
                (afterP, afterV) = (p, v);
                if (next == pending.Count) OpenLap(p, v, t);
            }
            var hop = new Hop(mu, t, p, v, stepEnd);
            Propagate(mu, ref p, ref v, stepEnd - t);
            t = stepEnd;
            res.Track.Add((t, p));
            if (p.Length < StarRadius(star)) { res.Impact = true; break; }
            if (p.Length >= edge)
            {
                var exit = FindExit(hop, edge, coarse);
                res.Exit = exit;
                res.Track[^1] = (exit.Time, exit.Position);
                break;
            }
            if (t > lapEnd + 1e-9 && res.DrawnCount == 0) res.DrawnCount = res.Track.Count - 1;
            if (t <= lapEnd + 1e-9) watch?.PlaneCrossing(hop, p);
            if (next == pending.Count) watch?.Observe(hop, p, v);
        }
        if (res.DrawnCount == 0) res.DrawnCount = res.Track.Count;
        watch?.Finish();
        res.After = OrbitOf(mu, afterP, afterV);
        (res.AfterPosition, res.AfterVelocity) = (afterP, afterV);
        return res;
    }

    /// <summary>Golden-section search for the true closest approach inside a sample bracket.</summary>
    static Approach RefineApproach(double mu, Station target, (double T, Vec3d P, Vec3d V, double Span) from)
    {
        (double D, Vec3d P, Vec3d V) At(double offset)
        {
            var (p, v) = (from.P, from.V);
            Propagate(mu, ref p, ref v, offset);
            return ((target.Position(from.T + offset) - p).Length, p, v);
        }
        const double Ratio = 0.6180339887498949;
        double lo = 0, hi = from.Span;
        var x1 = hi - (hi - lo) * Ratio;
        var x2 = lo + (hi - lo) * Ratio;
        double d1 = At(x1).D, d2 = At(x2).D;
        for (int i = 0; i < 24; i++)
        {
            if (d1 < d2) { hi = x2; x2 = x1; d2 = d1; x1 = hi - (hi - lo) * Ratio; d1 = At(x1).D; }
            else { lo = x1; x1 = x2; d1 = d2; x2 = lo + (hi - lo) * Ratio; d2 = At(x2).D; }
        }
        var t = (lo + hi) / 2;
        var (d, pos, vel) = At(t);
        var when = from.T + t;
        return new Approach(when, d, (target.Velocity(when) - vel).Length, pos, target.Position(when));
    }
}

namespace Starbearer.Sim;

/// <summary>
/// A deliberately unclever pilot. It proves a contract is flyable and measures margins for balance;
/// it is never used in play. Burns at periapsis, corrects heading on leaving a well, captures on arrival.
/// </summary>
public static class Autopilot
{
    public const double DesiredPeriapsis = 0.16;
    public const double MinPeriapsis = 0.09;

    public static double SpeedForLeg(Galaxy g, Vec3d a, Vec3d b, double margin, double drag = 1)
    {
        var d = (b - a).Length;
        double integral = 0;
        const int n = 24;
        for (int i = 0; i < n; i++)
            integral += g.Density(a + (b - a) * ((i + 0.5) / n)) * d / n;
        return Tuning.StallSpeed * margin * Math.Exp(Tuning.BaseDrag * drag * integral);
    }

    /// <summary>Cheapest star chain, costing each leg by the launch speed it needs.</summary>
    public static List<int> Route(Galaxy g, int from, int to, double drag = 1)
    {
        int n = g.Stars.Length;
        var dist = Enumerable.Repeat(double.MaxValue, n).ToArray();
        var prev = Enumerable.Repeat(-1, n).ToArray();
        var done = new bool[n];
        dist[from] = 0;
        for (int it = 0; it < n; it++)
        {
            int u = -1;
            for (int i = 0; i < n; i++) if (!done[i] && (u < 0 || dist[i] < dist[u])) u = i;
            if (u < 0 || dist[u] == double.MaxValue) break;
            done[u] = true;
            for (int v = 0; v < n; v++)
            {
                if (v == u) continue;
                var c = dist[u] + SpeedForLeg(g, g.Stars[u].Position, g.Stars[v].Position, 1.3, drag);
                if (c < dist[v]) { dist[v] = c; prev[v] = u; }
            }
        }
        var route = new List<int>();
        for (int v = to; v != -1; v = prev[v]) route.Insert(0, v);
        return route;
    }

    /// <summary>
    /// Velocity from the current position. With a next waypoint it picks the pass that lets the star's
    /// gravity do the turn; without one (final approach) it just targets the desired periapsis.
    /// </summary>
    static Vec3d AimAt(Galaxy g, Voyage v, int starId, double speed, int nextStar = -1)
    {
        var pos = v.TruePosition;
        var truth = v.TruePositions;
        var dir = (truth[starId] - pos).Normalized();
        var star = g.Stars[starId];
        Vec3d exitWanted = Vec3d.Zero;
        Vec3d axis;
        if (nextStar >= 0)
        {
            exitWanted = (truth[nextStar] - truth[starId]).Normalized();
            var perp = exitWanted - dir * exitWanted.Dot(dir);
            axis = perp.Length > 1e-3 ? -perp.Normalized() : dir.AnyPerpendicular(); // pass with the star on the turning side
        }
        else axis = dir.AnyPerpendicular();

        double lo = 0.01, hi = star.Soi * 0.95;
        var best = dir * speed;
        double bestScore = double.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            var b = (lo + hi) / 2;
            var vel = (truth[starId] + axis * b - pos).Normalized() * speed;
            var p = Predictor.Run(g, truth, pos, vel, starId, v.Class.Drag);
            var hit = p.Encounters.FirstOrDefault(e => e.Star == starId, new Encounter(-1, double.MaxValue, 0, 0, 0, pos));
            bool tooClose = p.End == PredictionEnd.Collision || hit.Periapsis < MinPeriapsis;
            if (tooClose) { lo = b; continue; }
            if (hit.Star < 0) { hi = b; continue; }
            if (nextStar < 0)
            {
                best = vel;
                if (hit.Periapsis < DesiredPeriapsis) lo = b; else hi = b;
                continue;
            }
            var exit = p.FinalVelocity.Normalized();
            var score = 1 - exit.Dot(exitWanted);
            if (score < bestScore) { bestScore = score; best = vel; }
            // Turned past the wanted heading means we passed too close.
            var turned = Math.Acos(Math.Clamp(exit.Dot(dir), -1, 1));
            var wanted = Math.Acos(Math.Clamp(exitWanted.Dot(dir), -1, 1));
            if (turned > wanted) lo = b; else hi = b;
        }
        return best;
    }

    /// <summary>
    /// Launching from deep in a well, gravity bends the first stretch, so a straight aim misses.
    /// Sweeps the heading in the plane of the bend, then narrows in on the wanted periapsis.
    /// </summary>
    static Vec3d AimOutOfWell(Galaxy g, Voyage v, Star home, int starId, double speed)
    {
        var pos = v.TruePosition;
        var truth = v.TruePositions;
        var dir = (truth[starId] - pos).Normalized();
        var axis = dir.Cross(pos - home.Position);
        axis = axis.Length > 1e-6 ? axis.Normalized() : dir.AnyPerpendicular();
        var side = axis.Cross(dir);
        var best = dir * speed;
        double center = 0, span = 1.2, bestScore = double.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            double pick = center;
            for (int i = -12; i <= 12; i++)
            {
                var a = center + span * i / 12;
                var vel = (dir * Math.Cos(a) + side * Math.Sin(a)) * speed;
                var p = Predictor.Run(g, truth, pos, vel, starId, v.Class.Drag);
                if (p.End == PredictionEnd.Collision) continue;
                var hit = p.Encounters.FirstOrDefault(e => e.Star == starId, new Encounter(-1, double.MaxValue, 0, 0, 0, pos));
                if (hit.Star < 0) continue;
                var score = Math.Abs(hit.Periapsis - DesiredPeriapsis);
                if (score < bestScore) { bestScore = score; best = vel; pick = a; }
            }
            center = pick; span /= 6;
        }
        return best;
    }

    public sealed record Result(bool Delivered, DeathCause Death, double FuelLeft, double Days, double CargoIntegrity, int Legs, string Note, Voyage Final);

    /// <param name="origin">The station set out from.</param>
    /// <param name="target">The star flown to.</param>
    public static Result Fly(Galaxy g, int origin, int target, Cargo cargo, ulong rngSeed = 1, double epoch = 0, ShipClass? ship = null)
    {
        var v = new Voyage(g, origin, target, cargo, Enumerable.Repeat(double.MaxValue, g.Stars.Length).ToArray(), rngSeed, epoch, shipClass: ship);
        var route = Route(g, g.StarOf(origin), target, v.Class.Drag);
        const double margin = 1.5;

        // Jump from the station's orbit toward the first waypoint.
        var next = 1;
        v.EnterFtl();
        var home = g.StarAt(origin);
        var legSpeed = SpeedForLeg(g, v.TruePosition, g.Stars[route[next]].Position, margin, v.Class.Drag);
        var startR = (v.TruePosition - home.Position).Length;
        var startSpeed = Math.Sqrt(legSpeed * legSpeed + 2 * home.Mu * (1 / startR - 1 / home.Soi)); // what is left after climbing out
        var launch = AimOutOfWell(g, v, home, route[next], startSpeed) - v.Velocity;
        v.Burn(launch, launch.Length);

        int guard = 0;
        while (v.Phase == Phase.Flight && guard++ < 400000)
        {
            var star = g.Stars[route[next]];
            var pos = v.TruePosition;
            var r = (star.Position - pos).Length;
            var towards = (star.Position - pos).Dot(v.Velocity) > 0;

            var nextR = r;
            if (r < star.Soi)
            {
                // Coast to periapsis at fine steps.
                v.Advance(Physics.SafeStep(g, v.TruePositions, pos, v.Velocity), 0, true);
                if (v.Phase != Phase.Flight) break;
                nextR = (star.Position - v.TruePosition).Length;
                if (nextR > r) // just passed periapsis
                {
                    var speedAtP = v.Velocity.Length;
                    if (next == route.Count - 1)
                    {
                        // Brake to a bound speed and drop out; the rest is the rendezvous.
                        var bound = Physics.CaptureSpeed(star, nextR) * 0.9;
                        if (speedAtP > bound) v.Burn(-v.Velocity, speedAtP - bound);
                        v.Descend();
                        var el = Sublight.OrbitOf(Sublight.Mu(star), v.SubPosition, v.SubVelocity);
                        var ok = v.Phase == Phase.Sublight && el.Bound && v.Fuel >= Tuning.RendezvousFuelReserve;
                        return new Result(ok, v.Death, v.Fuel, v.Time, v.CargoIntegrity, route.Count - 1, ok ? "descended" : "missed capture", v);
                    }
                    var after = route[next + 1];
                    var need = SpeedForLeg(g, v.TruePosition, v.TruePositions[after], margin, v.Class.Drag);
                    var well = 2 * star.Mu * (1 / nextR - 1 / star.Soi);
                    var want = Math.Sqrt(need * need + well);
                    if (want > speedAtP) v.Burn(v.Velocity, want - speedAtP);
                    // Coast out, then correct heading once in open space.
                    int gg = 0;
                    while (v.Phase == Phase.Flight && (star.Position - v.TruePosition).Length < star.Soi && gg++ < 20000)
                        v.Advance(Physics.SafeStep(g, v.TruePositions, v.TruePosition, v.Velocity), 0, true);
                    if (v.Phase != Phase.Flight) break;
                    next++;
                    var correct = AimAt(g, v, route[next], Math.Max(v.Velocity.Length, need * 0.9), next + 1 < route.Count ? route[next + 1] : -1);
                    var delta = correct - v.Velocity;
                    v.Burn(delta, delta.Length);
                }
                continue;
            }
            if (!towards) return Report(v, route, $"overshot {star.Name}");
            v.Advance(Math.Min(0.5, Math.Max(0.02, (r - star.Soi) * 0.3 / Math.Max(v.Velocity.Length, 0.05))), 0, true);
        }
        return Report(v, route, v.Phase.ToString());
    }

    static Result Report(Voyage v, List<int> route, string note) =>
        new(false, v.Death, v.Fuel, v.Time, v.CargoIntegrity, route.Count - 1, note, v);
}

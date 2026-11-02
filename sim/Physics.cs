namespace Starbearer.Sim;

/// <summary>
/// Hyperspace flight, patched conics: only the star whose sphere of influence the ship is in pulls on
/// it, so paths are two-body arcs in a well and straight between stars. Outside a well, quadratic
/// drag bleeds speed: v(x) = v0 exp(-k x).
/// Planner and flight share this code; only the star positions they are given differ.
/// </summary>
public static class Physics
{
    /// <summary>The one star whose sphere of influence holds this point, or -1 in open space.</summary>
    public static int Primary(Galaxy g, Vec3d[] starPos, Vec3d pos)
    {
        var (best, depth) = (-1, 1.0);
        for (int i = 0; i < starPos.Length; i++)
        {
            var share = (starPos[i] - pos).LengthSq / (g.Stars[i].Soi * g.Stars[i].Soi);
            if (share < depth) (best, depth) = (i, share);
        }
        return best;
    }

    public static Vec3d Acceleration(Galaxy g, Vec3d[] starPos, Vec3d pos, Vec3d vel, Vec3d thrust)
    {
        var a = thrust;
        var primary = Primary(g, starPos, pos);
        if (primary >= 0)
        {
            var d = starPos[primary] - pos;
            var r2 = d.LengthSq;
            a += d * (g.Stars[primary].Mu / (r2 * Math.Sqrt(r2)));
        }
        var k = Tuning.BaseDrag * g.Density(pos) * (primary >= 0 ? 0.1 : 1);   // stellar wind clears the medium near a star
        return a - vel * (k * vel.Length);
    }

    public static void Rk4(Galaxy g, Vec3d[] starPos, ref Vec3d p, ref Vec3d v, Vec3d thrust, double dt)
    {
        var a1 = Acceleration(g, starPos, p, v, thrust);
        var v2 = v + a1 * (dt / 2);
        var a2 = Acceleration(g, starPos, p + v * (dt / 2), v2, thrust);
        var v3 = v + a2 * (dt / 2);
        var a3 = Acceleration(g, starPos, p + v2 * (dt / 2), v3, thrust);
        var v4 = v + a3 * dt;
        var a4 = Acceleration(g, starPos, p + v3 * dt, v4, thrust);
        var np = p + (v + 2 * v2 + 2 * v3 + v4) * (dt / 6);
        var nv = v + (a1 + 2 * a2 + 2 * a3 + a4) * (dt / 6);
        p = np; v = nv;
    }

    /// <summary>Step size that resolves the closest well; large in open space.</summary>
    public static double SafeStep(Galaxy g, Vec3d[] starPos, Vec3d pos, Vec3d vel)
    {
        var speed = Math.Max(vel.Length, 1e-3);
        var dt = 0.5 / speed * 0.5;
        for (int i = 0; i < starPos.Length; i++)
        {
            var r = (starPos[i] - pos).Length;
            var s = g.Stars[i];
            if (r < s.Soi * 1.2) dt = Math.Min(dt, 0.04 * Math.Max(r, 0.01) / Math.Sqrt(speed * speed + 2 * s.Mu / Math.Max(r, 0.01)));
            else dt = Math.Min(dt, 0.4 * (r - s.Soi) / speed + 0.01);
        }
        return Math.Clamp(dt, 1e-4, 1.0);
    }

    /// <summary>Longest straight run in one coasting step, so drag follows the nebulae and a forecast samples the line.</summary>
    const double OpenRunLy = 0.2;

    /// <summary>
    /// The first well a straight line from pos along dir meets, and how far along it the edge is: zero
    /// if pos is already inside one. -1 if the line meets none.
    /// </summary>
    public static int NextWell(Galaxy g, Vec3d[] starPos, Vec3d pos, Vec3d dir, out double distance)
    {
        var (best, at) = (-1, double.PositiveInfinity);
        for (int i = 0; i < starPos.Length; i++)
        {
            var soi = g.Stars[i].Soi;
            var oc = pos - starPos[i];
            var c = oc.LengthSq - soi * soi;
            if (c <= 0) { distance = 0; return i; }   // spheres never overlap, so this is the one
            var b = oc.Dot(dir);
            if (b >= 0) continue;                      // moving away
            var disc = b * b - c;
            if (disc < 0) continue;
            var s = -b - Math.Sqrt(disc);
            if (s < at) (best, at) = (i, s);
        }
        distance = at;
        return best;
    }

    /// <summary>
    /// Coasts for up to maxDt days and returns the days taken. In a well only that star counts, so
    /// the step is a two-body arc; in open space the path is straight and drag only slows it, so the
    /// run goes along the line to the next well's edge, the stall point, or the end of the time.
    /// well is the star whose well the ship is in, or -1; it is only a hint and is checked.
    /// </summary>
    public static double Coast(Galaxy g, Vec3d[] starPos, ref Vec3d pos, ref Vec3d vel, double maxDt, ref int well)
    {
        if (well >= 0 && (starPos[well] - pos).LengthSq >= g.Stars[well].Soi * g.Stars[well].Soi) well = -1;
        var speed = vel.Length;
        double ahead = double.PositiveInfinity;
        if (well < 0 && speed > 1e-12)
        {
            var next = NextWell(g, starPos, pos, vel / speed, out ahead);
            if (ahead == 0) well = next;
            else if (next >= 0 && ahead <= OpenRunLy) return RunTo(g, ref pos, ref vel, ahead + 1e-6, maxDt, ref well, next);
        }
        if (well >= 0)
        {
            var s = g.Stars[well];
            var d = starPos[well] - pos;
            var r = Math.Max(d.Length, 0.01);
            var dt = Math.Min(maxDt, Math.Clamp(0.04 * r / Math.Sqrt(speed * speed + 2 * s.Mu / r), 1e-4, 1.0));
            Rk4(g, starPos[well], s.Mu, ref pos, ref vel, dt);
            if ((starPos[well] - pos).LengthSq >= s.Soi * s.Soi) well = -1;
            return dt;
        }
        if (speed <= 1e-12) return maxDt;
        return RunTo(g, ref pos, ref vel, OpenRunLy, maxDt, ref well, -1);
    }

    /// <summary>
    /// A straight coast of up to run ly in open space, shortened to the time left or the point where the
    /// speed falls to the stall line. With quadratic drag v(s) = v0 exp(-k s) and t(s) = (exp(k s) - 1) / (k v0).
    /// </summary>
    static double RunTo(Galaxy g, ref Vec3d pos, ref Vec3d vel, double run, double maxDt, ref int well, int entering)
    {
        var speed = vel.Length;
        var dir = vel / speed;
        var k = Tuning.BaseDrag * g.Density(pos + dir * (run / 2));
        double Days(double s) => k * s < 1e-9 ? s / speed : (Math.Exp(k * s) - 1) / (k * speed);
        var reached = true;
        if (speed > Tuning.StallSpeed && speed * Math.Exp(-k * run) < Tuning.StallSpeed)
        {
            run = Math.Log(speed / Tuning.StallSpeed) / k + 1e-9;   // stop just past the stall line
            reached = false;
        }
        var dt = Days(run);
        if (dt > maxDt)
        {
            run = k * speed * maxDt < 1e-9 ? speed * maxDt : Math.Log(1 + k * speed * maxDt) / k;
            dt = maxDt;
            reached = false;
        }
        pos += dir * run;
        vel = dir * (speed * Math.Exp(-k * run));
        if (reached && entering >= 0) well = entering;
        return dt;
    }

    /// <summary>One step in a well: the one star's pull and the thinned drag near it.</summary>
    static void Rk4(Galaxy g, Vec3d star, double mu, ref Vec3d p, ref Vec3d v, double dt)
    {
        Vec3d A(Vec3d q, Vec3d w)
        {
            var d = star - q;
            var r2 = d.LengthSq;
            return d * (mu / (r2 * Math.Sqrt(r2))) - w * (Tuning.BaseDrag * g.Density(q) * 0.1 * w.Length);
        }
        var a1 = A(p, v);
        var v2 = v + a1 * (dt / 2);
        var a2 = A(p + v * (dt / 2), v2);
        var v3 = v + a2 * (dt / 2);
        var a3 = A(p + v2 * (dt / 2), v3);
        var v4 = v + a3 * dt;
        var a4 = A(p + v3 * dt, v4);
        p += (v + 2 * v2 + 2 * v3 + v4) * (dt / 6);
        v += (a1 + 2 * a2 + 2 * a3 + a4) * (dt / 6);
    }

    /// <summary>Fastest speed at distance r that a descent turns into a bound orbit.</summary>
    public static double CaptureSpeed(Star s, double r) =>
        Math.Sqrt(2 * s.Mu * Math.Max(0, 1 / r - 1 / s.Soi));
}

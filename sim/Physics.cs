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

    /// <summary>Fastest speed at distance r that a descent turns into a bound orbit.</summary>
    public static double CaptureSpeed(Star s, double r) =>
        Math.Sqrt(2 * s.Mu * Math.Max(0, 1 / r - 1 / s.Soi));
}

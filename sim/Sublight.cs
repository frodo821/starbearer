namespace Starbearer.Sim;

/// <summary>
/// Normal space: patched conics with no drag. Inside a star's sphere of influence the ship is on a
/// two-body orbit, solved exactly while coasting; outside it flies straight. Mapped so FTL wells and orbits
/// agree: r(ly) = r(AU) * Scale and speeds carry over unchanged, which keeps orbital energy continuous
/// across a descent or a jump.
/// </summary>
public static partial class Sublight
{
    public const double Scale = 0.4; // ly per AU
    const double TrackStep = 0.03;   // forecast sample limit, as a fraction of the local orbital time
    const double OpenRunDays = 30;   // how far a path that meets nothing is followed into open space

    public static double Mu(Star s) => s.Mu / Scale;
    public static double SystemRadius(Star s) => s.Soi / Scale;
    public static double StarRadius(Star s) => s.Radius / Scale;

    /// <summary>Orbit frame at a state: tangent, outward radial and orbit normal, all unit length.</summary>
    public static (Vec3d T, Vec3d R, Vec3d N) Frame(Vec3d p, Vec3d v)
    {
        var t = v.Normalized();
        var h = p.Cross(v);
        var n = h.Length > 1e-9 ? h.Normalized() : t.AnyPerpendicular();
        return (t, t.Cross(n), n);
    }

    public static Maneuver Decompose(double time, Vec3d p, Vec3d v, Vec3d dv)
    {
        var (t, r, n) = Frame(p, v);
        return new Maneuver { Time = time, Prograde = dv.Dot(t), Radial = dv.Dot(r), Normal = dv.Dot(n) };
    }

    /// <summary>Angle in degrees between an orbit and the station's plane.</summary>
    public static double RelativeInclination(IOrbiter s, Vec3d p, Vec3d v)
    {
        var (_, _, n) = Frame(p, v);
        return Math.Acos(Math.Clamp(n.Dot(s.PlaneNormal), -1, 1)) * 180 / Math.PI;
    }

    static Vec3d Accel(double mu, Vec3d p, Vec3d thrust)
    {
        var r2 = p.LengthSq;
        return thrust - p * (mu / (r2 * Math.Sqrt(r2)));
    }

    /// <param name="mu">Zero in open space.</param>
    public static void Propagate(double mu, ref Vec3d p, ref Vec3d v, double days, Vec3d thrust = default)
    {
        if (days <= 1e-12) return;
        if (mu <= 0)
        {
            p += v * days + thrust * (days * days / 2);
            v += thrust * days;
            return;
        }
        if (thrust.LengthSq == 0) { Kepler(mu, ref p, ref v, days); return; }
        double left = days;
        while (left > 1e-12)
        {
            var r = p.Length;
            var h = Math.Min(left, Math.Max(1e-4, 0.02 * Math.Sqrt(r * r * r / mu)));
            var a1 = Accel(mu, p, thrust);
            var v2 = v + a1 * (h / 2);
            var a2 = Accel(mu, p + v * (h / 2), thrust);
            var v3 = v + a2 * (h / 2);
            var a3 = Accel(mu, p + v2 * (h / 2), thrust);
            var v4 = v + a3 * h;
            var a4 = Accel(mu, p + v3 * h, thrust);
            p += (v + 2 * v2 + 2 * v3 + v4) * (h / 6);
            v += (a1 + 2 * a2 + 2 * a3 + a4) * (h / 6);
            left -= h;
        }
    }

    static double StumpffC(double z) => z > 1e-8 ? (1 - Math.Cos(Math.Sqrt(z))) / z
        : z < -1e-8 ? (Math.Cosh(Math.Sqrt(-z)) - 1) / -z : 0.5 - z / 24;

    static double StumpffS(double z)
    {
        if (z > 1e-8) { var q = Math.Sqrt(z); return (q - Math.Sin(q)) / (q * q * q); }
        if (z < -1e-8) { var q = Math.Sqrt(-z); return (Math.Sinh(q) - q) / (q * q * q); }
        return 1.0 / 6 - z / 120;
    }

    /// <summary>The exact two-body coast, for any kind of orbit, by the universal anomaly.</summary>
    public static void Kepler(double mu, ref Vec3d p, ref Vec3d v, double days)
    {
        var r0 = p.Length;
        var vr = p.Dot(v) / r0;
        var alpha = 2 / r0 - v.LengthSq / mu;
        var root = Math.Sqrt(mu);
        if (alpha > 1e-9) days %= 2 * Math.PI / (alpha * Math.Sqrt(alpha)) / root;
        (double F, double Slope) Kepler(double x)
        {
            var z = alpha * x * x;
            var (c, s) = (StumpffC(z), StumpffS(z));
            return (r0 * vr / root * x * x * c + (1 - alpha * r0) * x * x * x * s + r0 * x - root * days,
                r0 * vr / root * x * (1 - z * s) + (1 - alpha * r0) * x * x * c + r0);
        }
        // F only ever rises with x, so Newton can be held inside a bracket.
        double lo = 0, hi = Math.Max(root * days / r0, 1e-9);
        while (Kepler(hi).F < 0) hi *= 2;
        var chi = hi / 2;
        for (int i = 0; i < 80; i++)
        {
            var (f, slope) = Kepler(chi);
            if (f > 0) hi = chi; else lo = chi;
            var next = chi - f / slope;
            if (!(next > lo && next < hi)) next = (lo + hi) / 2;
            var moved = Math.Abs(next - chi);
            chi = next;
            if (moved < 1e-13 * (1 + chi)) break;
        }
        var zEnd = alpha * chi * chi;
        var (cEnd, sEnd) = (StumpffC(zEnd), StumpffS(zEnd));
        var np = p * (1 - chi * chi / r0 * cEnd) + v * (days - chi * chi * chi / root * sEnd);
        var r = np.Length;
        var nv = p * (root / (r * r0) * (alpha * chi * chi * chi * sEnd - chi)) + v * (1 - chi * chi / r * cEnd);
        (p, v) = (np, nv);
    }

    /// <summary>The first sphere of influence a straight path runs into, leaving the one it came from.</summary>
    public static Handover? NextPrimary(Galaxy g, int from, Vec3d ly, Vec3d velocity, double time)
    {
        var speed = velocity.Length * Scale;
        if (speed < 1e-9) return null;
        var dir = velocity.Normalized();
        Handover? best = null;
        foreach (var s in g.Stars)
        {
            if (s.Id == from) continue;
            var to = s.Position - ly;
            var along = to.Dot(dir);
            var miss = to.LengthSq - along * along;
            if (miss >= s.Soi * s.Soi) continue;
            var half = Math.Sqrt(s.Soi * s.Soi - miss);
            if (along + half < 0) continue;              // behind the ship
            var hit = Math.Max(along - half, 0);         // zero when spheres overlap: already inside
            if (best == null || time + hit / speed < best.Value.Time)
                best = new Handover(s.Id, time + hit / speed, ly + dir * hit);
        }
        return best;
    }

    /// <summary>What to steer for from a body to reach a goal, a station or a body: the goal itself when it
    /// circles this body, else the body one level down toward it. Null when the ship has to climb out first,
    /// or is already circling the goal.</summary>
    public static IOrbiter? Waypoint(Center from, IOrbiter? goal)
    {
        Center? host;
        switch (goal)
        {
            case Station st:
                if (st.Host == null || st.Host.Key == from.Key) return st;
                host = st.Host;
                break;
            case Center body: host = body; break;
            default: return null;
        }
        for (var c = host; c != null; c = c.Parent)
            if (c.Parent?.Key == from.Key) return c;
        return null;
    }

    /// <summary>The next periapsis and apoapsis after t0 on the coast from a state. Neither on a circle, where
    /// they are nowhere in particular; no apoapsis on an open orbit, nor a periapsis already passed.</summary>
    public static (Apsis? Pe, Apsis? Ap) NextApsides(double mu, Vec3d p, Vec3d v, double t0)
    {
        var r = p.Length;
        var h = p.Cross(v);
        var ev = v.Cross(h) / mu - p / r;
        var e = ev.Length;
        if (e < 1e-6 || h.Length < 1e-12) return (null, null);
        var a = -mu / (2 * (v.LengthSq / 2 - mu / r));
        var nu = Math.Acos(Math.Clamp(ev.Dot(p) / (e * r), -1, 1));
        if (p.Dot(v) < 0) nu = -nu;   // falling in: before periapsis
        Apsis At(double dt, bool apo)
        {
            var (q, w) = (p, v);
            Kepler(mu, ref q, ref w, dt);
            return new Apsis(t0 + dt, q, apo ? a * (1 + e) : a * (1 - e), apo);
        }
        if (e < 1)
        {
            var n = Math.Sqrt(mu / (a * a * a));
            var big = 2 * Math.Atan2(Math.Sqrt(1 - e) * Math.Sin(nu / 2), Math.Sqrt(1 + e) * Math.Cos(nu / 2));
            var mean = big - e * Math.Sin(big);
            double Ahead(double target)
            {
                var d = (target - mean) % (2 * Math.PI);
                return (d <= 1e-12 ? d + 2 * Math.PI : d) / n;
            }
            return (At(Ahead(0), false), At(Ahead(Math.PI), true));
        }
        var hn = Math.Sqrt(mu / (-a * -a * -a));
        var f = 2 * Math.Atanh(Math.Sqrt((e - 1) / (e + 1)) * Math.Tan(nu / 2));
        var m = e * Math.Sinh(f) - f;
        return (m < 0 ? At(-m / hn, false) : null, null);
    }

    public static Elements OrbitOf(double mu, Vec3d p, Vec3d v)
    {
        var r = p.Length;
        var energy = v.LengthSq / 2 - mu / r;
        var h = p.Cross(v).Length;
        if (energy >= 0)
        {
            var e0 = Math.Sqrt(1 + 2 * energy * h * h / (mu * mu));
            return new Elements(h * h / mu / (1 + e0), double.PositiveInfinity, double.PositiveInfinity, false);
        }
        var a = -mu / (2 * energy);
        var e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h * h / (mu * mu)));
        return new Elements(a * (1 - e), a * (1 + e), 2 * Math.PI * Math.Sqrt(a * a * a / mu), true);
    }

    /// <summary>The orbit a ship would drop into from a periapsis pass in hyperspace.</summary>
    public static Elements DescentOrbit(Star star, double periapsisLy, double speed) =>
        OrbitOf(Mu(star), new Vec3d(periapsisLy / Scale, 0, 0), new Vec3d(0, 0, speed));

    public static Vec3d Impulse(Vec3d p, Vec3d v, Maneuver m)
    {
        var (t, r, n) = Frame(p, v);
        return t * m.Prograde + r * m.Radial + n * m.Normal;
    }
}

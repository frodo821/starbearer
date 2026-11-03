namespace Starbearer.Sim;

public enum BodyKind { Star, Rocky, Giant, Moon }

/// <summary>Something on a circular orbit: a station, or a planet or moon a course can be aimed at.</summary>
public interface IOrbiter
{
    /// <summary>The system it belongs to.</summary>
    int Star { get; }
    Vec3d Position(double t);
    Vec3d Velocity(double t);
    Vec3d PlaneNormal { get; }
    double OrbitRadius { get; }
    double Omega { get; }
}

/// <summary>
/// A body with a sphere of influence, in AU: a star at the root of its system, or a planet or moon on
/// rails around its parent. Ship states are held relative to the one Center pulling on them.
/// </summary>
public sealed class Center : IOrbiter
{
    public required int Star { get; init; }
    /// <summary>Zero for the star. Unique within a system.</summary>
    public int Index { get; init; }
    public required string Name { get; init; }
    public BodyKind Kind { get; init; }
    public required double Mu { get; init; }
    public required double BodyRadius { get; init; }
    public required double Edge { get; init; }
    public Center? Parent { get; init; }
    public double OrbitRadius { get; init; }
    public double Phase { get; init; }
    public double Omega { get; init; }
    public Vec3d U { get; init; }
    public Vec3d W { get; init; }
    public List<Center> Children { get; } = new();

    public int Key => Star * 1000 + Index;
    public Vec3d PlaneNormal => U.Cross(W);

    /// <summary>Relative to the parent.</summary>
    public Vec3d Position(double t)
    {
        var a = Phase + Omega * t;
        return (U * Math.Cos(a) + W * Math.Sin(a)) * OrbitRadius;
    }

    public Vec3d Velocity(double t)
    {
        var a = Phase + Omega * t;
        return (W * Math.Cos(a) - U * Math.Sin(a)) * (OrbitRadius * Omega);
    }

    /// <summary>Relative to the star.</summary>
    public Vec3d StarPosition(double t) => Parent == null ? Vec3d.Zero : Parent.StarPosition(t) + Position(t);
    public Vec3d StarVelocity(double t) => Parent == null ? Vec3d.Zero : Parent.StarVelocity(t) + Velocity(t);

    /// <summary>This body and everything under it.</summary>
    public IEnumerable<Center> Tree => Children.SelectMany(c => c.Tree).Prepend(this);

    public bool Holds(Center other) => other.Key == Key || (other.Parent != null && Holds(other.Parent));

    /// <summary>A star alone, with no bodies around it.</summary>
    public static implicit operator Center(Star s) =>
        new() { Star = s.Id, Name = s.Name, Kind = BodyKind.Star, Mu = Sublight.Mu(s), BodyRadius = Sublight.StarRadius(s), Edge = Sublight.SystemRadius(s) };
}

public static class Bodies
{
    static double Sphere(double radius, double mu, double parentMu) => radius * Math.Pow(mu / parentMu, 0.4);

    static (Vec3d U, Vec3d W) Plane(Rng r, double maxTiltDegrees)
    {
        var node = r.Range(0, 2 * Math.PI);
        var tilt = r.Range(-maxTiltDegrees, maxTiltDegrees) * Math.PI / 180;
        var u = new Vec3d(Math.Cos(node), 0, Math.Sin(node));
        return (u, new Vec3d(-Math.Sin(node), 0, Math.Cos(node)) * Math.Cos(tilt) + new Vec3d(0, Math.Sin(tilt), 0));
    }

    /// <summary>Share of a planet's sphere kept clear of moons, for stations to circle in.</summary>
    public const double FreeInnerShare = 0.42;

    /// <summary>The orbit whose sphere of influence starts at the given radius, so neighbours never overlap.</summary>
    static double Slot(double inner, double mu, double parentMu) => inner / (1 - Math.Pow(mu / parentMu, 0.4));

    /// <summary>The middle of the widest band of a system clear of every planet's sphere.</summary>
    public static double WidestGap(Center system)
    {
        var (best, from, wide) = (system.Edge * 0.5, system.Edge * 0.1, 0.0);
        void Band(double to)
        {
            if (to - from > wide) (wide, best) = (to - from, (from + to) / 2);
        }
        foreach (var p in system.Children.OrderBy(p => p.OrbitRadius))
        {
            Band(p.OrbitRadius - p.Edge);
            from = p.OrbitRadius + p.Edge;
        }
        Band(system.Edge * 0.95);
        return best;
    }

    /// <summary>Earth's share of the Sun's mass, and the radii (AU) of Earth and of Jupiter.</summary>
    const double EarthShare = 3.003e-6, EarthRadius = 4.2635e-5, JupiterShare = 9.546e-4, JupiterRadius = 4.7789e-4;

    /// <summary>A body's radius from its mass: rock grows slowly with mass; a giant is about Jupiter's size whatever it weighs.</summary>
    static double RadiusFor(double share, bool giant) =>
        giant ? JupiterRadius * Math.Pow(share / JupiterShare, 0.05) : EarthRadius * Math.Pow(share / EarthShare, 0.28);

    /// <summary>
    /// A star's planets and moons, from the seed, so they never need saving. To scale: rocky worlds of a
    /// Mars to a few Earths, giants of a Saturn to a couple of Jupiters, more often further out; each
    /// planet's orbit half again to twice the last one's, from a tenth of the way to the edge; moons a ten-thousandth to a hundredth of their planet.
    /// </summary>
    public static Center Build(ulong seed, Star s)
    {
        var r = new Rng(seed * 2147483647 + (ulong)s.Id);
        Center root = s;
        var system = new Center { Star = s.Id, Name = s.Name, Kind = BodyKind.Star, Mu = root.Mu, BodyRadius = root.BodyRadius, Edge = root.Edge };
        var index = 1;
        var planets = 2 + r.Index(3);
        var a = system.Edge * r.Range(0.15, 0.25);   // out to where a descent from the well comes down
        var inner = 0.0;
        for (int i = 0; i < planets; i++)
        {
            var giant = r.Unit() < (a > system.Edge * 0.2 ? 0.6 : 0.15);
            var share = giant ? r.Range(3e-4, 2e-3) : EarthShare * Math.Pow(10, r.Range(-1, 0.7));
            var mu = system.Mu * share;
            a = Math.Max(a, Slot(inner, mu, system.Mu) * 1.05);
            var edge = Sphere(a, mu, system.Mu);
            if (a + edge > system.Edge * 0.92) break;
            inner = a + edge;
            var (u, w) = Plane(r, 4);
            var planet = new Center
            {
                Star = s.Id, Index = index++, Name = $"{s.Name} {"bcdefg"[i]}", Kind = giant ? BodyKind.Giant : BodyKind.Rocky, Parent = system,
                Mu = mu, BodyRadius = RadiusFor(share * s.Mass, giant), Edge = edge,
                OrbitRadius = a, Phase = r.Range(0, 2 * Math.PI), Omega = Math.Sqrt(system.Mu / (a * a * a)), U = u, W = w,
            };
            system.Children.Add(planet);
            var moonInner = edge * FreeInnerShare;
            for (int m = 0, moons = giant ? 1 + r.Index(2) : r.Index(2); m < moons; m++)
            {
                var mshare = Math.Pow(10, r.Range(-4, -2));
                var mmu = mu * mshare;
                var ma = Slot(moonInner + edge * r.Range(0.01, 0.05), mmu, mu);
                var me = Sphere(ma, mmu, mu);
                if (ma + me > edge * 0.9) break;
                moonInner = ma + me;
                var (mu0, mw) = Plane(r, 12);
                planet.Children.Add(new Center
                {
                    Star = s.Id, Index = index++, Name = $"{planet.Name}{"ⅠⅡⅢ"[m]}", Kind = BodyKind.Moon, Parent = planet,
                    Mu = mmu, BodyRadius = RadiusFor(share * mshare * s.Mass, false), Edge = me,
                    OrbitRadius = ma, Phase = r.Range(0, 2 * Math.PI), Omega = Math.Sqrt(mu / (ma * ma * ma)), U = mu0, W = mw,
                });
            }
            a *= r.Range(1.5, 2.1);
        }
        return system;
    }
}

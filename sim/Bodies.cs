namespace Starbearer.Sim;

public enum BodyKind { Star, Rocky, Giant, Moon }

/// <summary>Something on a circular orbit: a station, or a planet or moon a course can be aimed at.</summary>
public interface IOrbiter
{
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

    /// <summary>A star's planets and moons, from the seed, so they never need saving.</summary>
    public static Center Build(ulong seed, Star s)
    {
        var r = new Rng(seed * 2147483647 + (ulong)s.Id);
        Center root = s;
        var system = new Center { Star = s.Id, Name = s.Name, Kind = BodyKind.Star, Mu = root.Mu, BodyRadius = root.BodyRadius, Edge = root.Edge };
        var index = 1;
        var count = 2 + r.Index(3);
        for (int i = 0; i < count; i++)
        {
            var a = system.Edge * (0.22 + 0.62 * (i + r.Range(0.1, 0.7)) / count);
            var giant = r.Unit() < 0.45;
            var mu = system.Mu * r.Range(giant ? 0.02 : 0.004, giant ? 0.05 : 0.012);
            var (u, w) = Plane(r, 4);
            var edge = Sphere(a, mu, system.Mu);
            var planet = new Center
            {
                Star = s.Id, Index = index++, Name = $"{s.Name} {"bcdefg"[i]}", Kind = giant ? BodyKind.Giant : BodyKind.Rocky, Parent = system,
                Mu = mu, BodyRadius = edge * 0.05, Edge = edge,
                OrbitRadius = a, Phase = r.Range(0, 2 * Math.PI), Omega = Math.Sqrt(system.Mu / (a * a * a)), U = u, W = w,
            };
            system.Children.Add(planet);
            for (int m = 0, moons = giant ? 1 + r.Index(2) : r.Index(2); m < moons; m++)
            {
                var ma = edge * (0.4 + 0.3 * (m + r.Range(0, 0.6)) / moons);
                var mmu = mu * 0.03;
                var (mu0, mw) = Plane(r, 12);
                var me = Sphere(ma, mmu, mu);
                planet.Children.Add(new Center
                {
                    Star = s.Id, Index = index++, Name = $"{planet.Name}{"ⅠⅡⅢ"[m]}", Kind = BodyKind.Moon, Parent = planet,
                    Mu = mmu, BodyRadius = me * 0.07, Edge = me,
                    OrbitRadius = ma, Phase = r.Range(0, 2 * Math.PI), Omega = Math.Sqrt(mu / (ma * ma * ma)), U = mu0, W = mw,
                });
            }
        }
        return system;
    }
}

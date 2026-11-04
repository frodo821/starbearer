using System.Text.Json.Serialization;

namespace Starbearer.Sim;

/// <summary>
/// A ship left in orbit, as saved: the body it circles (by star and index in that system), and where
/// and how fast it was, relative to that body, at an epoch. It coasts on from there, untouched.
/// </summary>
public sealed class ParkedState
{
    public int Star { get; set; }
    public int Body { get; set; }
    public double[] Position { get; set; } = new double[3];
    public double[] Velocity { get; set; } = new double[3];
    public double Epoch { get; set; }

    public static ParkedState Of(Center body, Vec3d p, Vec3d v, double epoch) => new()
    {
        Star = body.Star, Body = body.Index, Epoch = epoch,
        Position = new[] { p.X, p.Y, p.Z }, Velocity = new[] { v.X, v.Y, v.Z },
    };

    public ParkedOrbit Orbit(Galaxy g, int shipId) =>
        new(g.System(Star).Tree.First(c => c.Index == Body), new Vec3d(Position[0], Position[1], Position[2]), new Vec3d(Velocity[0], Velocity[1], Velocity[2]), Epoch, shipId);
}

/// <summary>
/// A parked ship's orbit, as something to steer for: it coasts by the two-body law around its host, so
/// where it is at any time is exact. Its orbit may be an ellipse; OrbitRadius and Omega give its
/// semi-major axis and mean motion, the measures the planners scale by.
/// </summary>
public sealed record ParkedOrbit(Center Host, Vec3d P0, Vec3d V0, double Epoch, int ShipId) : IOrbiter
{
    public int Star => Host.Star;
    public Vec3d PlaneNormal => P0.Cross(V0).Normalized();
    public double OrbitRadius => 1 / (2 / P0.Length - V0.LengthSq / Host.Mu);
    public double Omega => Math.Sqrt(Host.Mu / Math.Pow(OrbitRadius, 3));
    public double Period => 2 * Math.PI / Omega;

    public (Vec3d Position, Vec3d Velocity) StateAt(double t)
    {
        var dt = t - Epoch;
        if (dt < 0) dt += Math.Ceiling(-dt / Period) * Period;   // a closed orbit comes round again
        var (p, v) = (P0, V0);
        Sublight.Propagate(Host.Mu, ref p, ref v, dt);
        return (p, v);
    }

    public Vec3d Position(double t) => StateAt(t).Position;
    public Vec3d Velocity(double t) => StateAt(t).Velocity;
}

public static class Parking
{
    /// <summary>
    /// Why a ship can't be left on this orbit, or null if it can: a ship left behind must stay on a
    /// closed orbit, clear of the body it circles and inside its reach, or it would drift off or fall in.
    /// </summary>
    public static string? Refusal(Center body, Vec3d p, Vec3d v)
    {
        var el = Sublight.OrbitOf(body.Mu, p, v);
        if (!el.Bound) return "閉じた軌道でないと置いていけない（脱出軌道）";
        if (el.Apoapsis > body.Edge * 0.95) return $"遠点が {body.Name} の重力圏を出る。軌道を下げてから";
        if (el.Periapsis < body.BodyRadius * 1.2) return $"近点が {body.Name} に近すぎる。軌道を上げてから";
        return null;
    }
}

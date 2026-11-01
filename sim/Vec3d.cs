namespace Starbearer.Sim;

/// <summary>Double-precision vector. Lengths are light years, time is days.</summary>
public readonly record struct Vec3d(double X, double Y, double Z)
{
    public static readonly Vec3d Zero = new(0, 0, 0);
    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator -(Vec3d a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3d operator *(double s, Vec3d a) => a * s;
    public static Vec3d operator /(Vec3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public double Dot(Vec3d o) => X * o.X + Y * o.Y + Z * o.Z;
    public Vec3d Cross(Vec3d o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    public double LengthSq => Dot(this);
    public double Length => Math.Sqrt(LengthSq);
    public Vec3d Normalized() { var l = Length; return l > 0 ? this / l : Zero; }
    /// <summary>Any unit vector perpendicular to this one.</summary>
    public Vec3d AnyPerpendicular()
    {
        var axis = Math.Abs(X) < 0.9 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0);
        return Cross(axis).Normalized();
    }
}

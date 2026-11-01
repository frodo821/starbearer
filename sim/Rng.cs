namespace Starbearer.Sim;

/// <summary>Seeded SplitMix64, so a galaxy is identical on every platform and runtime.</summary>
public sealed class Rng
{
    ulong _s;
    public Rng(ulong seed) => _s = seed;
    public ulong Next()
    {
        ulong z = _s += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    public double Unit() => (Next() >> 11) * (1.0 / (1UL << 53));
    public double Range(double a, double b) => a + (b - a) * Unit();
    public int Index(int n) => (int)(Next() % (ulong)n);
    public double Normal()
    {
        var u = 1.0 - Unit();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * Unit());
    }
    public Vec3d NormalVec() => new(Normal(), Normal(), Normal());
}

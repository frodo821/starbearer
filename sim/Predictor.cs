namespace Starbearer.Sim;

/// <summary>Captured: caught in a well on an orbit that never reaches its edge again.</summary>
public enum PredictionEnd { Horizon, Stall, Collision, Lost, Captured }

public readonly record struct Encounter(int Star, double Periapsis, double PeriapsisSpeed, double ExcessSpeed, double Time, Vec3d Point);
public readonly record struct TrackPoint(Vec3d Position, double Speed, double Time);

public sealed class Prediction
{
    public List<TrackPoint> Track { get; } = new();
    public List<Encounter> Encounters { get; } = new();
    public PredictionEnd End { get; set; }
    public int? CollisionStar { get; set; }
    public double Duration { get; set; }
    public Vec3d FinalVelocity { get; set; }
}

public static class Predictor
{
    const double MaxDays = 240;
    const double SampleLy = 0.2;

    /// <param name="stopAtStar">Stop after the first encounter with this star (used by the autopilot).</param>
    /// <param name="drag">The hull's drag, as a multiple of a small ship's.</param>
    public static Prediction Run(Galaxy g, Vec3d[] starPos, Vec3d pos, Vec3d vel, int stopAtStar = -1, double drag = 1)
    {
        var res = new Prediction();
        double t = 0, sinceSample = double.MaxValue;
        int well = -1, inWell = -1;
        double minR = double.MaxValue, spdAtMin = 0, entrySpeed = 0, tMin = 0, caughtUntil = double.PositiveInfinity;
        var pMin = pos;
        var start = pos;
        void Pass() => res.Encounters.Add(new Encounter(inWell, minR, spdAtMin, entrySpeed, tMin, pMin));

        while (true)
        {
            if (sinceSample >= SampleLy)
            {
                res.Track.Add(new TrackPoint(pos, vel.Length, t));
                sinceSample = 0;
            }
            var prev = pos;
            t += Physics.Coast(g, starPos, ref pos, ref vel, Math.Max(MaxDays - t, 1e-6), ref well, drag);
            sinceSample += (pos - prev).Length;

            if (well >= 0)
            {
                var s = g.Stars[well];
                var r = (starPos[well] - pos).Length;
                if (r < s.Radius)
                {
                    res.End = PredictionEnd.Collision; res.CollisionStar = well; res.Duration = t;
                    res.Track.Add(new TrackPoint(pos, vel.Length, t));
                    return res;
                }
                if (inWell != well) { inWell = well; minR = double.MaxValue; entrySpeed = vel.Length; caughtUntil = double.PositiveInfinity; }
                if (r < minR) { minR = r; spdAtMin = vel.Length; tMin = t; pMin = pos; }
                // Caught: the orbit never reaches the edge again, so one more lap shows all there is.
                if (double.IsPositiveInfinity(caughtUntil) && Caught(s, pos - starPos[well], vel) is { } lap) caughtUntil = t + lap;
                if (t >= caughtUntil) { Pass(); Finish(res, PredictionEnd.Captured, t, pos, vel); return res; }
            }
            else if (inWell >= 0)
            {
                Pass();
                if (inWell == stopAtStar) { Finish(res, PredictionEnd.Horizon, t, pos, vel); return res; }
                inWell = -1;
            }
            if (well < 0 && vel.Length < Tuning.StallSpeed) { Finish(res, PredictionEnd.Stall, t, pos, vel); return res; }
            if ((pos - start).Length > 120) { Finish(res, PredictionEnd.Lost, t, pos, vel); return res; }
            if (t >= MaxDays)
            {
                if (inWell >= 0) Pass();
                Finish(res, PredictionEnd.Horizon, t, pos, vel);
                return res;
            }
        }
    }

    /// <summary>The period of the orbit if it stays inside the well, ignoring the thin drag there; null if it leaves.</summary>
    static double? Caught(Star s, Vec3d r, Vec3d v)
    {
        var energy = v.LengthSq / 2 - s.Mu / r.Length;
        if (energy >= 0) return null;
        var a = -s.Mu / (2 * energy);
        var h = r.Cross(v).LengthSq;
        var e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h / (s.Mu * s.Mu)));
        return a * (1 + e) < s.Soi ? 2 * Math.PI * Math.Sqrt(a * a * a / s.Mu) : null;
    }

    static void Finish(Prediction r, PredictionEnd e, double t, Vec3d p, Vec3d v)
    {
        r.End = e; r.Duration = t; r.FinalVelocity = v;
        r.Track.Add(new TrackPoint(p, v.Length, t));
    }
}

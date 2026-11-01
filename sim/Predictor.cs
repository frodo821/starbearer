namespace Starbearer.Sim;

public enum PredictionEnd { Horizon, Stall, Collision, Lost }

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
    public static Prediction Run(Galaxy g, Vec3d[] starPos, Vec3d pos, Vec3d vel, int stopAtStar = -1)
    {
        var res = new Prediction();
        double t = 0, sinceSample = double.MaxValue;
        int inWell = -1;
        double minR = double.MaxValue, spdAtMin = 0, entrySpeed = 0, tMin = 0;
        var pMin = pos;
        var start = pos;

        while (true)
        {
            if (sinceSample >= SampleLy)
            {
                res.Track.Add(new TrackPoint(pos, vel.Length, t));
                sinceSample = 0;
            }
            var dt = Physics.SafeStep(g, starPos, pos, vel);
            var prev = pos;
            Physics.Rk4(g, starPos, ref pos, ref vel, Vec3d.Zero, dt);
            t += dt;
            sinceSample += (pos - prev).Length;

            int here = -1;
            for (int i = 0; i < starPos.Length; i++)
            {
                var s = g.Stars[i];
                var r = (starPos[i] - pos).Length;
                if (r < s.Radius)
                {
                    res.End = PredictionEnd.Collision; res.CollisionStar = i; res.Duration = t;
                    res.Track.Add(new TrackPoint(pos, vel.Length, t));
                    return res;
                }
                if (r >= s.Soi) continue;
                here = i;
                if (inWell != i) { inWell = i; minR = double.MaxValue; entrySpeed = vel.Length; }
                if (r < minR) { minR = r; spdAtMin = vel.Length; tMin = t; pMin = pos; }
            }
            if (here < 0 && inWell >= 0)
            {
                res.Encounters.Add(new Encounter(inWell, minR, spdAtMin, entrySpeed, tMin, pMin));
                if (inWell == stopAtStar) { Finish(res, PredictionEnd.Horizon, t, pos, vel); return res; }
                inWell = -1;
            }
            if (here < 0 && vel.Length < Tuning.StallSpeed) { Finish(res, PredictionEnd.Stall, t, pos, vel); return res; }
            if ((pos - start).Length > 120) { Finish(res, PredictionEnd.Lost, t, pos, vel); return res; }
            if (t > MaxDays)
            {
                if (inWell >= 0) res.Encounters.Add(new Encounter(inWell, minR, spdAtMin, entrySpeed, tMin, pMin));
                Finish(res, PredictionEnd.Horizon, t, pos, vel);
                return res;
            }
        }
    }

    static void Finish(Prediction r, PredictionEnd e, double t, Vec3d p, Vec3d v)
    {
        r.End = e; r.Duration = t; r.FinalVelocity = v;
        r.Track.Add(new TrackPoint(p, v.Length, t));
    }
}

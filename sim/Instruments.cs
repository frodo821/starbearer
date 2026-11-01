namespace Starbearer.Sim;

/// <summary>A star as the pilot sees it from here, worked out from the chart.</summary>
public readonly record struct Contact(Star Star, double Distance, double OffPrograde, double ClosingSpeed, double? Eta);

public static class Instruments
{
    public const double SecondsPerDay = 86400;

    /// <summary>Time rates offered in flight, as multiples of real time.</summary>
    public static readonly double[] TimeScales = { 1, 100, 1000, 10000, 100000, 1000000 };

    /// <summary>A rate in days per second, written as a multiple of real time: x1 is one second per second.</summary>
    public static string Rate(double daysPerSecond)
    {
        var x = daysPerSecond * SecondsPerDay;
        return x >= 1e6 ? $"x{x / 1e6:0.#}M" : x >= 1000 ? $"x{x / 1000:0.#}k" : $"x{x:0.#}";
    }

    /// <summary>
    /// All-sphere attitude display: the nose sits at the centre, dead astern on the rim, and the
    /// distance from centre is proportional to the angle off the nose. Nothing can be out of view.
    /// Returns x right and y up in unit-disc coordinates.
    /// </summary>
    public static (double X, double Y) CompassPoint(Vec3d direction, Vec3d right, Vec3d up, Vec3d forward)
    {
        var d = direction.Normalized();
        var angle = Math.Acos(Math.Clamp(d.Dot(forward), -1, 1));
        var x = d.Dot(right); var y = d.Dot(up);
        var side = Math.Sqrt(x * x + y * y);
        if (side < 1e-9) return (0, angle > 1 ? 1 : 0);
        var r = angle / Math.PI;
        return (x / side * r, y / side * r);
    }

    /// <summary>Mission clock, e.g. "D012 03:24:10".</summary>
    public static string Clock(double days)
    {
        var seconds = (long)Math.Floor(Math.Max(days, 0) * 86400);
        return $"D{seconds / 86400:000} {seconds / 3600 % 24:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    /// <summary>Time until an event, e.g. "T-2d 03:24:10", or "T+00:01:05" once it has passed.</summary>
    public static string Countdown(double days)
    {
        var seconds = (long)Math.Ceiling(Math.Abs(days) * 86400);
        var sign = days >= 0 ? "T-" : "T+";
        var d = seconds / 86400;
        return $"{sign}{(d > 0 ? $"{d}d " : "")}{seconds / 3600 % 24:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    /// <summary>The nearest charted stars, closest first.</summary>
    public static List<Contact> Nearby(Galaxy g, Vec3d[] chart, Vec3d position, Vec3d velocity, int count)
    {
        var heading = velocity.Normalized();
        var moving = velocity.Length > 1e-6;
        return g.Stars
            .Select(s =>
            {
                var to = chart[s.Id] - position;
                var dist = to.Length;
                var unit = to.Normalized();
                var off = moving ? Math.Acos(Math.Clamp(unit.Dot(heading), -1, 1)) * 180 / Math.PI : 0;
                var closing = velocity.Dot(unit);
                return new Contact(s, dist, off, closing, closing > 1e-4 ? dist / closing : null);
            })
            .OrderBy(c => c.Distance)
            .Take(count)
            .ToList();
    }
}

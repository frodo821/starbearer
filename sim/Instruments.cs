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

    /// <summary>Kilometres in an AU, and km/s in an AU/day.</summary>
    public const double KmPerAu = Units.MetresPerAu / 1000, KmPerSecondPerAuPerDay = KmPerAu / Units.SecondsPerDay;

    /// <summary>A distance in a system: AU from a tenth of one up, km below, where AU would read as nothing.</summary>
    public static string Distance(double au) =>
        Math.Abs(au) >= 0.1 ? $"{au:F3} AU" : Math.Abs(au) * KmPerAu >= 1e5 ? $"{au * KmPerAu / 1e3:N0}千 km" : $"{au * KmPerAu:N0} km";

    /// <summary>A speed in a system, in km/s.</summary>
    public static string Speed(double auPerDay) =>
        Math.Abs(auPerDay * KmPerSecondPerAuPerDay) >= 100 ? $"{auPerDay * KmPerSecondPerAuPerDay:F1} km/s" : $"{auPerDay * KmPerSecondPerAuPerDay:F2} km/s";

    /// <summary>A delta-v of the drive, given in ly/day of the well, as the km/s it is in the system it is spent in.</summary>
    public static string WellDeltaV(double lyPerDay) => Speed(lyPerDay * Sublight.SpeedFactor);

    /// <summary>A mass in ship units, in tonnes.</summary>
    public static string Tonnes(double mass) => $"{Units.Kg(mass) / 1000:N0} t";

    /// <summary>A force, given in N, in kN.</summary>
    public static string Force(double newtons) => $"{newtons / 1000:F1} kN";

    /// <summary>An acceleration in a system, in m/s².</summary>
    public static string Acceleration(double auPerDay2) => $"{Units.MetresPerSecond2(auPerDay2):F2} m/s²";

    /// <summary>A year of the clock, in days.</summary>
    public const double DaysPerYear = 365;
    /// <summary>The galaxy's day 0, on the calendar.</summary>
    public static readonly DateTime Epoch = new(3456, 1, 1);

    /// <summary>A span of time, e.g. "1y 2d 03:04:05.67"; years and days only where there are any.</summary>
    public static string Span(double days)
    {
        var sign = days < 0 ? "-" : "";
        var cs = (long)Math.Round(Math.Abs(days) * SecondsPerDay * 100);
        var seconds = cs / 100;
        var day = seconds / 86400;
        var (y, d) = (day / (long)DaysPerYear, day % (long)DaysPerYear);
        var lead = y > 0 ? $"{y}y {d}d " : d > 0 ? $"{d}d " : "";
        return $"{sign}{lead}{seconds / 3600 % 24:00}:{seconds / 60 % 60:00}:{seconds % 60:00}.{cs % 100:00}";
    }

    /// <summary>The mission clock: time since leaving port, as a span.</summary>
    public static string Clock(double days) => Span(Math.Max(days, 0));

    /// <summary>Time until an event, e.g. "T-2d 03:24:10.00", or "T+00:01:05.00" once it has passed.</summary>
    public static string Countdown(double days) => (days >= 0 ? "T-" : "T+") + Span(Math.Abs(days));

    static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    /// <summary>A day of the galaxy on the calendar, e.g. "Jan, 1st 3456".</summary>
    public static string Day(double day)
    {
        var at = Epoch.AddDays(Math.Max(day, 0));
        return $"{at.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture)}, {Ordinal(at.Day)} {at.Year}";
    }

    /// <summary>A moment of the galaxy on the calendar, e.g. "Jan, 1st 3456, 07:08:09.12".</summary>
    public static string Date(double day)
    {
        var cs = (long)Math.Round(Math.Max(day, 0) * SecondsPerDay * 100);
        var at = Epoch.AddSeconds(cs / 100);
        var month = at.ToString("MMM", System.Globalization.CultureInfo.InvariantCulture);
        return $"{month}, {Ordinal(at.Day)} {at.Year}, {at:HH}:{at:mm}:{at:ss}.{cs % 100:00}";
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

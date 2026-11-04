namespace Starbearer.Sim;

/// <summary>
/// What the window shows of the sky beyond plain geometry, after general relativity: the light of a
/// star bent by the mass of a nearer one, and in FTL the view through the bubble. Ray tracing of warp
/// bubbles finds the stars crowding toward the way the ship is going, blue and bright ahead, red and dim
/// behind, and at faster than light a region behind where no light catches up with the ship at all; the
/// bubble collapsing lets go a burst ahead of what it swept up.
///
/// Taken literally, a bubble at tens to hundreds of times light speed would leave one bright point
/// ahead and darkness everywhere else, so the bubble's bending is put as an effective β that grows
/// with speed and stops short of 1, and the dark region behind as a cone that widens with speed. Starlight
/// bent by a real star is a matter of arcseconds; LensExaggeration makes it a sight. Presentation
/// only: navigation and the chart work from where things are.
/// </summary>
public static class Optics
{
    /// <summary>The effective β approaches BetaMax, reaching about two thirds of it at BetaSpeed (ly/day).</summary>
    public const double BetaMax = 0.97, BetaSpeed = 0.4;
    /// <summary>The dark cone behind: its half-angle approaches DarkMaxDeg, about two thirds of it at DarkSpeed; its edge fades over DarkEdgeDeg.</summary>
    public const double DarkMaxDeg = 100, DarkSpeed = 0.5, DarkEdgeDeg = 8;
    /// <summary>How much more a star bends light than it really would, so it can be seen.</summary>
    public const double LensExaggeration = 200000;   // for a Sun, a ring of some 5° at 1 AU, clear of its glow
    /// <summary>Light speed in AU/day, for a star's Schwarzschild radius from its μ (AU³/day²).</summary>
    public const double LightAuPerDay = 173.1446;
    /// <summary>Below this temperature a star's light has gone out of sight into the infrared; it fades out from FadeFromK.</summary>
    public const double InvisibleK = 1500, FadeFromK = 3000;
    /// <summary>The most a star brightens, by Doppler or lens: the eye and the screen saturate.</summary>
    public const double MaxGain = 6;

    public static double Beta(double speed) => speed <= 0 ? 0 : BetaMax * Math.Tanh(speed / BetaSpeed);

    /// <summary>The speed an effective β stands for: the inverse of Beta.</summary>
    public static double SpeedFor(double beta) => beta <= 0 ? 0 : BetaSpeed * Math.Atanh(Math.Min(beta / BetaMax, 1 - 1e-12));

    /// <summary>Half-angle of the dark cone behind, in radians; zero out of FTL.</summary>
    public static double DarkAngle(double speed) => speed <= 0 ? 0 : DarkMaxDeg * Math.PI / 180 * Math.Tanh(speed / DarkSpeed);

    /// <summary>
    /// Where light from a direction is seen from a ship moving along a unit direction at β, and the
    /// Doppler factor it comes with (above 1 bluer and brighter): cos θ' = (cos θ + β) / (1 + β cos θ).
    /// </summary>
    public static (Vec3d Dir, double Doppler) Aberrate(Vec3d dir, Vec3d along, double beta)
    {
        if (beta <= 0) return (dir, 1);
        var c = Math.Clamp(dir.Dot(along), -1, 1);
        var perp = dir - along * c;
        var c2 = (c + beta) / (1 + beta * c);
        var s2 = Math.Sqrt(Math.Max(0, 1 - c2 * c2));
        var side = perp.Length > 1e-12 ? perp / perp.Length : along.AnyPerpendicular();
        var gamma = 1 / Math.Sqrt(1 - beta * beta);
        return (along * c2 + side * s2, gamma * (1 + beta * c));
    }

    /// <summary>How much of a star's light stays in sight at the temperature it is seen at: none below InvisibleK.</summary>
    public static double Visible(double seenK) => Math.Clamp((seenK - InvisibleK) / (FadeFromK - InvisibleK), 0, 1);

    /// <summary>How much brighter a point of light is for its Doppler factor: D³, saturating at MaxGain.</summary>
    public static double DopplerGain(double doppler) => Math.Min(MaxGain, doppler * doppler * doppler);

    /// <summary>How much of the light from a seen direction gets through the dark cone behind: 0 inside, 1 clear of it.</summary>
    public static double Horizon(Vec3d seen, Vec3d along, double speed)
    {
        var dark = DarkAngle(speed);
        if (dark <= 0) return 1;
        var fromRear = Math.Acos(Math.Clamp(-seen.Dot(along), -1, 1));
        var edge = DarkEdgeDeg * Math.PI / 180;
        return Math.Clamp((fromRear - (dark - edge)) / edge, 0, 1);
    }

    /// <summary>
    /// The Einstein angle (radians) of a mass of μ AU³/day² at a distance in AU, for light from far behind
    /// it: √(2 r_s / D), exaggerated.
    /// </summary>
    public static double EinsteinAngle(double mu, double distAu)
    {
        var rs = 2 * mu / (LightAuPerDay * LightAuPerDay);
        return Math.Sqrt(2 * rs * LensExaggeration / Math.Max(distAu, 1e-9));
    }

    /// <summary>
    /// A point lens: for light from an angle off the lens, the two images, each as its angle off the lens
    /// on the same side (the inner one is on the far side, so negative) and how much brighter it is.
    /// </summary>
    public static ((double Angle, double Gain) Outer, (double Angle, double Gain) Inner) Lens(double angle, double einstein)
    {
        if (einstein <= 0) return ((angle, 1), (0, 0));
        var root = Math.Sqrt(angle * angle + 4 * einstein * einstein);
        var u = Math.Max(angle / einstein, 1e-6);
        var m = (u * u + 2) / (2 * u * Math.Sqrt(u * u + 4));
        return (((angle + root) / 2, Math.Min(MaxGain, m + 0.5)), ((angle - root) / 2, Math.Min(MaxGain, m - 0.5)));
    }

    /// <summary>A direction turned within the plane it shares with an axis, to a given angle from that axis (negative: to the far side).</summary>
    public static Vec3d AtAngle(Vec3d axis, Vec3d dir, double angle)
    {
        var perp = dir - axis * dir.Dot(axis);
        var side = perp.Length > 1e-12 ? perp / perp.Length : axis.AnyPerpendicular();
        return axis * Math.Cos(angle) + side * Math.Sin(angle);
    }
}

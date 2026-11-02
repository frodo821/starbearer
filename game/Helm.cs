using System;
using Starbearer.Sim;

namespace Starbearer;

public enum ViewHold { Free, Star, Prograde, Retrograde, RadialOut, RadialIn, Normal, AntiNormal, Node, Target }

/// <summary>
/// The ship's controls: where the nose points, what attitude it holds, and the engine. The nose is
/// separate from the camera; engine and shield both follow it. What the holds point at is up to the game.
/// </summary>
public sealed class Helm
{
    const double SlewRadPerSecond = 1.4, TurnRadPerSecond = 0.9;

    public Vec3d Nose = new(0, 0, -1);
    public ViewHold Hold = ViewHold.Free;
    /// <summary>Engine setting, 0..1. The engine switch only starts and stops it at this setting.</summary>
    public double Throttle { get; private set; } = 0.5;
    public bool EngineOn { get; private set; }
    /// <summary>The engine is actually pushing this frame.</summary>
    public bool Firing { get; set; }

    public static string NameOf(ViewHold hold) => hold switch
    {
        ViewHold.Star => "恒星",
        ViewHold.Prograde => "順行",
        ViewHold.Retrograde => "逆行",
        ViewHold.RadialOut => "外",
        ViewHold.RadialIn => "内",
        ViewHold.Normal => "法線",
        ViewHold.AntiNormal => "反法線",
        ViewHold.Node => "ノード",
        ViewHold.Target => "目的地",
        _ => "なし",
    };

    /// <summary>Straight ahead, nothing held, engine off: how a ship is found on boarding.</summary>
    public void Reset(Vec3d nose)
    {
        Nose = nose.Normalized();
        Hold = ViewHold.Free;
        EngineOn = false;
        Throttle = 0.5;
    }

    /// <summary>Turns the nose toward a direction at the ship's slew rate.</summary>
    public void Slew(Vec3d want, double delta)
    {
        var angle = Math.Acos(Math.Clamp(Nose.Dot(want), -1, 1));
        var step = SlewRadPerSecond * delta;
        if (angle <= step) { Nose = want; return; }
        if (angle > 3.1) want = (want + Nose.AnyPerpendicular() * 0.05).Normalized();  // no axis dead astern
        var k = Math.Sin(step) / Math.Sin(Math.Min(angle, 3.1));
        Nose = (Nose * (Math.Cos(step) - Math.Cos(Math.Min(angle, 3.1)) * k) + want * k).Normalized();
    }

    /// <summary>Compass frame for the nose: x right, y up, with no roll.</summary>
    public (Vec3d Right, Vec3d Up) NoseFrame()
    {
        var right = Nose.Cross(new Vec3d(0, 1, 0));
        right = right.Length > 1e-3 ? right.Normalized() : Nose.AnyPerpendicular();
        return (right, right.Cross(Nose).Normalized());
    }

    /// <summary>Turns by hand, which lets go of any hold.</summary>
    public void Turn(int yaw, int pitch, bool fine, double delta)
    {
        if (yaw == 0 && pitch == 0) return;
        Hold = ViewHold.Free;
        var (right, up) = NoseFrame();
        var rate = TurnRadPerSecond * (fine ? 0.15 : 1) * delta;
        Nose = (Nose + right * (yaw * rate) + up * (pitch * rate)).Normalized();
    }

    public void MoveThrottle(int dir, bool fine, double delta)
    {
        if (dir != 0) Throttle = Math.Clamp(Throttle + dir * (fine ? 0.06 : 0.6) * delta, 0, 1);
    }

    public void SetThrottle(double throttle) => Throttle = Math.Clamp(throttle, 0, 1);

    public void ToggleEngine() => EngineOn = !EngineOn;

    public void LightEngine() => EngineOn = true;

    /// <summary>Stops the engine. False if it was not running.</summary>
    public bool CutEngine()
    {
        if (!EngineOn) return false;
        EngineOn = false;
        return true;
    }

    /// <summary>The thrust the engine gives this frame, as a fraction of full along the nose.</summary>
    public Vec3d Thrust()
    {
        Firing = EngineOn && Throttle > 0;
        return Firing ? Nose * Throttle : Vec3d.Zero;
    }
}

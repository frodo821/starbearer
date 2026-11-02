using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

public partial class Main
{

    public Vec3d CameraForward()
    {
        var f = -Camera.GlobalTransform.Basis.Z;
        return new Vec3d(f.X, f.Y, f.Z);
    }

    void LookAt(Vec3d dir)
    {
        _yaw = Mathf.Atan2((float)-dir.X, (float)-dir.Z);
        _pitch = Mathf.Asin((float)dir.Y);
    }

    public void LookAtPublic(Vec3d dir) => LookAt(dir);

    public Vec3d CurrentVelocity => V!.Phase == Phase.Sublight ? V.SubVelocity : V.Velocity;

    /// <summary>Direction of the nearest star, or the system's star in normal space.</summary>
    public Vec3d? NearestStarDirection()
    {
        var v = V!;
        var star = Instruments.Nearby(G!, v.TruePositions, v.TruePosition, Vec3d.Zero, 1)[0].Star;
        return (v.TruePositions[star.Id] - v.TruePosition).Normalized();
    }

    public Vec3d? NodeDirection()
    {
        var v = V!;
        if (v.Phase != Phase.Sublight || NextNode is not { } n) return null;
        if (SubForecast?.StageOf(n) is not { } s) return null;
        var i = Sublight.Impulse(s.Position, s.Velocity, n);
        return i.Length > 1e-6 ? i.Normalized() : null;
    }

    /// <summary>Orbit frame the attitude markers use: around the star in either layer.</summary>
    public (Vec3d T, Vec3d R, Vec3d N)? MotionFrame()
    {
        var v = V!;
        if (CurrentVelocity.Length < 1e-6) return null;
        var pos = v.Phase == Phase.Sublight ? v.SubPosition : v.TruePosition - v.TruePositions[Instruments.Nearby(G!, v.TruePositions, v.TruePosition, Vec3d.Zero, 1)[0].Star.Id];
        return Sublight.Frame(pos, CurrentVelocity);
    }

    public Vec3d? DirectionOf(ViewHold hold)
    {
        var f = MotionFrame();
        return hold switch
        {
            ViewHold.Star => NearestStarDirection(),
            ViewHold.Prograde => f?.T,
            ViewHold.Retrograde => f is { } a ? -a.T : null,
            ViewHold.RadialOut => f?.R,
            ViewHold.RadialIn => f is { } b ? -b.R : null,
            ViewHold.Normal => f?.N,
            ViewHold.AntiNormal => f is { } c ? -c.N : null,
            ViewHold.Node => NodeDirection(),
            ViewHold.Target => (V!.ChartPositions[V.TargetStar] - V.EstimatedPosition).Normalized(),
            _ => null,
        };
    }

    void SlewNose(Vec3d want, double delta)
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

    void TurnManually(double delta)
    {
        var yaw = (IsDown(Key.Right) ? 1 : 0) - (IsDown(Key.Left) ? 1 : 0);
        var pitch = (IsDown(Key.Up) ? 1 : 0) - (IsDown(Key.Down) ? 1 : 0);
        if (yaw == 0 && pitch == 0) return;
        Hold = ViewHold.Free;
        var (right, up) = NoseFrame();
        var rate = TurnRadPerSecond * (Input.IsKeyPressed(Key.Shift) ? 0.15 : 1) * delta;
        Nose = (Nose + right * (yaw * rate) + up * (pitch * rate)).Normalized();
    }

    /// <summary>The ship doesn't move while time is stopped. Commands given now take effect on resuming.</summary>
    bool ClockStopped => Screen is Screen.Orbit or Screen.Flight && Paused && !EngineOn && !IsDown(Key.X) && !IsDown(Key.Z);

    void UpdateAttitude(double delta)
    {
        if (V == null || V.Docked) return;
        UpdateThrottle(delta);
        if (ClockStopped) return;
        TurnManually(delta);
        if (DirectionOf(Hold) is { } want) SlewNose(want, delta);
        if (CameraFollow) LookAt(Nose);
    }

    public void SetHold(ViewHold hold)
    {
        if (V?.Docked == true) { Message = DockedNotice; return; }
        Hold = hold;
        Message = "姿勢保持: " + HoldName;
    }

    void ToggleCameraFollow()
    {
        CameraFollow = !CameraFollow;
        Message = CameraFollow ? "カメラを機首に固定" : "カメラ自由";
    }

    void UpdateThrottle(double delta)
    {
        var dir = (IsDown(Key.W) ? 1 : 0) - (IsDown(Key.S) ? 1 : 0);
        if (dir != 0) Throttle = Math.Clamp(Throttle + dir * (Input.IsKeyPressed(Key.Shift) ? 0.06 : 0.6) * delta, 0, 1);
    }

    void ToggleEngine()
    {
        EngineOn = !EngineOn;
        Message = EngineOn ? (Throttle > 0 ? "エンジン点火" : "スロットルが0%") : "エンジン停止";
    }

    void CutEngine(string message)
    {
        if (!EngineOn) return;
        EngineOn = false;
        Message = message;
    }

    void ThrottleKeys(Key key)
    {
        if (key == Key.E) ToggleEngine();
        if (key == Key.O) Throttle = 1;
        if (key == Key.L) Throttle = 0;
    }

    Vec3d EngineCommand()
    {
        if (EngineOn && V!.Fuel <= 0) CutEngine("燃料切れ");
        Firing = EngineOn && Throttle > 0;
        return Firing ? Nose * Throttle : Vec3d.Zero;
    }

    /// <summary>Engine acceleration at the current throttle, in the units of the current layer.</summary>
    public double EngineAcceleration =>
        Throttle * (V!.Phase == Phase.Sublight ? Tuning.SublightThrust : Tuning.MaxThrust);

    /// <summary>Time rate a burn runs at, so the real-time estimate matches what will happen.</summary>
    /// <summary>Days per second under power. A tight orbit gets a minute a lap, or the nose can't keep up with it.</summary>
    public double BurnTimeRate
    {
        get
        {
            var v = V!;
            if (v.Phase != Phase.Sublight) return Math.Min(BurnRate, Rates[RateIndex]);
            var r = v.SubPosition.Length;
            var lap = 2 * Math.PI * Math.Sqrt(r * r * r / v.Primary.Mu);
            return Math.Min(Math.Min(BurnRate, Rates[SubRateIndex]), lap / 60);
        }
    }

    public string HoldName => Hold switch
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
}

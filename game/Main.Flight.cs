using System;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>Faster than light: the jump plan, flight, and the descent back to normal space.</summary>
public partial class Main
{
    public Vec3d PlanDir;
    public double PlanDv = 0.45;
    public bool Shielded { get; private set; }

    public void BeginJump()
    {
        if (Screen != Screen.Orbit) return;
        CutEngine("エンジン停止");
        Screen = Screen.Plan;
        PlanDir = (V!.ChartPositions[V.TargetStar] - V.EstimatedPosition).Normalized();
        LookAt(PlanDir);
        Message = "空をタップして方位を選び、W/S でキック量を決める";
        _forecastDirty = true;
    }

    public void BeginCorrection()
    {
        if (Screen != Screen.Flight || V!.Phase == Phase.Dead) return;
        CutEngine("エンジン停止");
        Screen = Screen.Plan;
        PlanDir = CameraForward();
        PlanDv = 0.02;
        Message = "修正噴射: 空をタップして方向を選び、W/S で量を決める";
        _forecastDirty = true;
    }

    public void SetAimScreen(Vector2 screen)
    {
        if (Camera.ProjectRayNormal(screen) is var d)
        {
            PlanDir = new Vec3d(d.X, d.Y, d.Z).Normalized();
            _forecastDirty = true;
        }
    }

    public void SetPlanDir(Vec3d dir) { PlanDir = dir.Normalized(); _forecastDirty = true; }
    public void SetPlanDv(double dv) { PlanDv = Math.Clamp(dv, 0, V!.DeltaVRemaining); _forecastDirty = true; }

    void Nudge(Key key, bool fine)
    {
        var deg = Mathf.DegToRad(fine ? 0.02 : 0.2);
        var up = new Vec3d(0, 1, 0);
        var right = PlanDir.Cross(up).Normalized();
        var pitch = right.Cross(PlanDir).Normalized();
        Vec3d? d = key switch
        {
            Key.Left => PlanDir - right * deg,
            Key.Right => PlanDir + right * deg,
            Key.Up => PlanDir + pitch * deg,
            Key.Down => PlanDir - pitch * deg,
            _ => null,
        };
        if (d is { } dir) SetPlanDir(dir);
    }

    void UpdatePlan(double delta)
    {
        var dir = (IsDown(Key.W) ? 1 : 0) - (IsDown(Key.S) ? 1 : 0);
        if (dir != 0) SetPlanDv(PlanDv + dir * (Input.IsKeyPressed(Key.Shift) ? 0.01 : 0.12) * delta);
    }

    public void ExecutePlan()
    {
        var v = V!;
        if (v.Docked) { Message = "停泊中は発進できない。Esc で戻り、U で出港する"; return; }
        var jumping = v.Phase == Phase.Sublight;
        v.EnterFtl();
        v.Burn(PlanDir, PlanDv);
        // No banner for the jump: the sky and the corner frame tell it.
        Helm.Nose = PlanDir.Normalized();
        Screen = Screen.Flight;
        Paused = false;
        MapOpen = false;
        Message = "";
        _forecastDirty = true;
    }

    public void CancelPlan()
    {
        Screen = V!.Phase == Phase.Sublight ? Screen.Orbit : Screen.Flight;
        _forecastDirty = true;
    }

    /// <summary>Faster than light: the clock slows in a well and under power, and the shield works when the nose faces the star.</summary>
    void UpdateFlight(double delta)
    {
        var v = V!;
        if (v.Phase == Phase.Arrived) { Dock(); return; }
        if (v.Phase == Phase.Dead) { Screen = Screen.Dead; return; }
        var r = v.NearestWell(out var star);
        var rate = Rates[RateIndex];
        if (star != null)
            rate = Math.Clamp(0.5 * r / Math.Max(v.Velocity.Length, 0.01), 0.04, rate);
        if (Helm.EngineOn) rate = Math.Min(rate, BurnRate);
        else if (Paused) rate = 0;   // a lit engine runs the clock, as in orbit
        CurrentRate = rate;

        var toStar = star != null ? (v.TruePositions[star.Id] - v.TruePosition).Normalized() : Vec3d.Zero;
        Shielded = star != null && Helm.Nose.Dot(toStar) > 0.8;
        var engine = EngineCommand();
        if (engine.LengthSq > 0) _forecastDirty = true;   // the coasting forecast is "if I stop now"
        v.Advance(rate * delta, engine, Shielded);

        if (v.Phase == Phase.Arrived) Dock();
        else if (v.Phase == Phase.Dead) Screen = Screen.Dead;
        else if (v.Phase == Phase.Stalled) Message = "失速。FTLから脱落した。Cで再キック";
    }

    public void TryDescend()
    {
        if (Screen != Screen.Flight) return;
        if (!V!.Descend()) { Message = "恒星の井戸の中でしか降下できない"; return; }
        Helm.CutEngine();
        Screen = Screen.Orbit;
        MapOpen = false; Selected = null; Paused = true; PlanFrozen = false;
        OrbitSky = false;
        Focus = null;
        Chart.Glide(PrimaryOnChart, ChartView.ZoomFor(V.Primary));
        // No banner for the descent either: the system comes up around the ship.
        Message = V.LocalStation == null ? "この星系に港はない。J で再発進できる"
            : V.Hold.Any(c => c.IsJob && G!.StarOf(c.Mission.Target) == V.SystemStar) ? "亜光速へ降下。軌道をタップしてマニューバを置き、港へ軌道を合わせる"
            : "この星系の港に入港すると補給できる。J で再発進もできる";
        _forecastDirty = true;
    }

    void ToggleMap()
    {
        MapOpen = !MapOpen;
        if (!MapOpen) return;
        var close = V!.Phase == Phase.Flight && V.NearestWell(out _) < double.MaxValue;
        Chart.Glide(V.EstimatedPosition, close ? ChartView.SystemZoom / 3 : ChartView.GalaxyZoom);
    }

    public double SigmaAt(double t) =>
        Math.Sqrt(V!.NavSigma * V.NavSigma + Tuning.PositionDriftPerSqrtDay * Tuning.PositionDriftPerSqrtDay * t);

    /// <summary>Delta-v needed at periapsis to dock at the target, per the chart.</summary>
    public double CaptureDv(Encounter e) =>
        Math.Max(0, e.PeriapsisSpeed - Physics.CaptureSpeed(G!.Stars[e.Star], e.Periapsis));
}

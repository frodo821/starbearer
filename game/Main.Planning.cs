using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

public partial class Main
{

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

    public void ExecutePlan()
    {
        var v = V!;
        if (v.Docked) { Message = "停泊中は発進できない。Esc で戻り、U で出港する"; return; }
        var jumping = v.Phase == Phase.Sublight;
        v.EnterFtl();
        v.Burn(PlanDir, PlanDv);
        if (jumping) ShowCue(new StateCue("FTL 発進", $"速度 {v.Velocity.Length:F2}   失速線 {Tuning.StallSpeed:F2}", new Color(0.45f, 0.75f, 1f), Major: true, Outward: true));
        Nose = PlanDir.Normalized();
        Screen = Screen.Flight;
        MapOpen = false;
        Message = "";
        _forecastDirty = true;
    }

    public void CancelPlan()
    {
        Screen = V!.Phase == Phase.Sublight ? Screen.Orbit : Screen.Flight;
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

    public double SigmaAt(double t) =>
        Math.Sqrt(V!.NavSigma * V.NavSigma + Tuning.PositionDriftPerSqrtDay * Tuning.PositionDriftPerSqrtDay * t);

    /// <param name="full">Something changed the path itself, not just the time.</param>
    void RefreshForecast(bool full)
    {
        var v = V!;
        if (Screen == Screen.Orbit)
        {
            // A coast is exact, so the path only needs working out again when something changes it.
            if (full || SubCurrent == null || v.AbsoluteTime >= _pathGoodUntil) RefreshPath();
            // The orbit view reaches the galaxy, so it shows where a jump after the plan would go.
            var plan = SubForecast?.Legs.LastOrDefault(l => l.Stages.Count > 0);
            var (from, velocity) = plan != null
                ? (G!.Stars[plan.Star].Position + plan.AfterPosition * Sublight.Scale, plan.AfterVelocity)
                : v.JumpState;
            Forecast = velocity.Length < 1e-3 ? null : Predictor.Run(G!, v.ChartPositions, from, velocity);
            DescentForecast = null;
            return;
        }
        var (pos, baseVel) = v.Phase == Phase.Sublight ? v.JumpState : (v.EstimatedPosition, v.Velocity);
        var vel = Screen == Screen.Plan ? baseVel + PlanDir * PlanDv : baseVel;
        DescentForecast = null;
        if (v.Phase == Phase.Flight && v.NearestWell(out var star) < double.MaxValue && star != null)
        {
            DescentStar = star;
            DescentForecast = Sublight.Predict(star, G!.StationOrNull(star.Id),
                (v.TruePosition - v.TruePositions[star.Id]) / Sublight.Scale, v.Velocity, v.AbsoluteTime, Array.Empty<Maneuver>(), 60, 300);
        }
        if (vel.Length < 1e-3) { Forecast = null; return; }
        Forecast = Predictor.Run(G!, v.ChartPositions, pos, vel);
    }

    /// <summary>The path for the coast and the plan, good until the first thing on it has gone by.</summary>
    void RefreshPath()
    {
        var v = V!;
        OrbitForecast Path(IReadOnlyCollection<Maneuver> nodes) =>
            Sublight.PredictPath(G!, v.System, v.SubPosition, v.SubVelocity, v.AbsoluteTime, nodes, ForecastSpan());
        SubCurrent = Path(Array.Empty<Maneuver>());
        if (v.Nodes.Count == 0) PlanFrozen = false;
        if (!PlanFrozen) SubForecast = v.Nodes.Count == 0 ? SubCurrent : Path(v.Nodes);
        double? PassesAt(OrbitForecast? f) => new[] { f?.Closest?.Time, f?.Crossing?.Time, f?.Exit?.Time, f?.Next?.Time }
            .Where(t => t > v.AbsoluteTime).Min();
        _pathGoodUntil = new[] { PassesAt(SubCurrent), PassesAt(SubForecast) }.Min() ?? double.PositiveInfinity;
    }

    /// <summary>Delta-v needed at periapsis to dock at the target, per the chart.</summary>
    public double CaptureDv(Encounter e) =>
        Math.Max(0, e.PeriapsisSpeed - Physics.CaptureSpeed(G!.Stars[e.Star], e.Periapsis));
}

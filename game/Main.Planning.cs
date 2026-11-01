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

    /// <summary>What a forecast worked out away from the main thread, for the voyage and screen it was asked for.</summary>
    sealed record ForecastResult(Voyage For, Screen On, bool Path, OrbitForecast? Current, OrbitForecast? Plan, double GoodUntil,
        Prediction? Jump, OrbitForecast? Descent, Star? DescentAt);

    Task<ForecastResult>? _forecastJob;

    /// <summary>Takes in a finished forecast. A late one is harmless: the old lines stay up a few frames longer.</summary>
    void CollectForecast()
    {
        if (_forecastJob is not { IsCompleted: true } job) return;
        _forecastJob = null;
        if (!job.IsCompletedSuccessfully) { GD.PushError(job.Exception?.ToString() ?? "forecast failed"); return; }
        var r = job.Result;
        if (r.For != V || r.On != Screen) { _forecastDirty = true; return; }
        Forecast = r.Jump;
        DescentForecast = r.Descent;
        if (r.DescentAt != null) DescentStar = r.DescentAt;
        if (!r.Path) return;
        SubCurrent = r.Current;
        if (V!.Nodes.Count == 0) PlanFrozen = false;
        if (!PlanFrozen) SubForecast = r.Plan;
        _pathGoodUntil = r.GoodUntil;
    }

    /// <param name="full">Something changed the path itself, not just the time.</param>
    void RefreshForecast(bool full)
    {
        var v = V!;
        var g = G!;
        var chart = (Vec3d[])v.ChartPositions.Clone();
        var screen = Screen;
        var t = v.AbsoluteTime;
        if (screen == Screen.Orbit)
        {
            // A coast is exact, so the path only needs working out again when something changes it.
            var path = full || SubCurrent == null || t >= _pathGoodUntil;
            var (primary, p, vel, span, nodes) = (v.Primary, v.SubPosition, v.SubVelocity, ForecastSpan(), v.Nodes.ToList());
            var kept = SubForecast;
            _forecastJob = Task.Run(() =>
            {
                OrbitForecast Path(IReadOnlyCollection<Maneuver> n) => Sublight.PredictPath(g, primary, p, vel, t, n, span);
                var current = path ? Path(Array.Empty<Maneuver>()) : null;
                var plan = !path ? kept : nodes.Count == 0 ? current : Path(nodes);
                double? PassesAt(OrbitForecast? f) => new[] { f?.Closest?.Time, f?.Crossing?.Time, f?.Exit?.Time, f?.Next?.Time, f?.Enter?.Time }
                    .Where(x => x > t).Min();
                var until = new[] { PassesAt(current), PassesAt(plan) }.Min() ?? double.PositiveInfinity;
                return new ForecastResult(v, screen, path, current, plan, until, null, null, null);
            });
            return;
        }
        var (pos, baseVel) = v.Phase == Phase.Sublight ? v.JumpState : (v.EstimatedPosition, v.Velocity);
        var aim = screen == Screen.Plan ? baseVel + PlanDir * PlanDv : baseVel;
        Star? well = null;
        if (v.Phase == Phase.Flight && v.NearestWell(out var star) < double.MaxValue) well = star;
        var (wellAt, wellSpeed) = well != null ? ((v.TruePosition - v.TruePositions[well.Id]) / Sublight.Scale, v.Velocity) : default;
        _forecastJob = Task.Run(() =>
        {
            var descent = well == null ? null : Sublight.Predict(g.System(well.Id), Sublight.Waypoint(g.System(well.Id), g.StationOrNull(well.Id)),
                wellAt, wellSpeed, t, Array.Empty<Maneuver>(), 60, 300);
            return new ForecastResult(v, screen, false, null, null, 0, aim.Length < 1e-3 ? null : Predictor.Run(g, chart, pos, aim), descent, well);
        });
    }

    /// <summary>Delta-v needed at periapsis to dock at the target, per the chart.</summary>
    public double CaptureDv(Encounter e) =>
        Math.Max(0, e.PeriapsisSpeed - Physics.CaptureSpeed(G!.Stars[e.Star], e.Periapsis));
}

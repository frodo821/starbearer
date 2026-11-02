using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>Forecasts, worked out off the main thread: the plan, the path if the engine stops now, the jump and the descent.</summary>
public partial class Main
{
    public Prediction? Forecast { get; private set; }

    /// <summary>The plan: the path with its burns. Frozen once a burn starts, so it stays the reference.</summary>
    public OrbitForecast? SubForecast { get; private set; }

    /// <summary>Where the ship goes if the engine stops now and nothing more is flown.</summary>
    public OrbitForecast? SubCurrent { get; private set; }

    public bool PlanFrozen { get; private set; }

    /// <summary>What a descent right now would leave the ship on, in AU around the star. Null outside a well.</summary>
    public OrbitForecast? DescentForecast { get; private set; }

    public Star? DescentStar { get; private set; }
    bool _forecastDirty = true;
    double _forecastTimer;
    double _pathGoodUntil;

    /// <summary>What a forecast worked out away from the main thread, for the voyage and screen it was asked for.</summary>
    sealed record ForecastResult(Voyage For, Screen On, bool Path, OrbitForecast? Current, OrbitForecast? Plan, double GoodUntil,
        Prediction? Jump, OrbitForecast? Descent, Star? DescentAt);

    Task<ForecastResult>? _forecastJob;

    /// <summary>Starts a forecast when something changed, or now and then for the drift of time.</summary>
    void TickForecast(double delta)
    {
        _forecastTimer -= delta;
        CollectForecast();
        if (_forecastJob != null || Screen is not (Screen.Plan or Screen.Flight or Screen.Orbit) || (!_forecastDirty && _forecastTimer > 0)) return;
        RefreshForecast(_forecastDirty);
        _forecastDirty = false;
        _forecastTimer = 0.4;
    }

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
            var (primary, p, vel, span, nodes, goal) = (v.Primary, v.SubPosition, v.SubVelocity, ForecastSpan(), v.Nodes.ToList(), v.Goal);
            var kept = SubForecast;
            _forecastJob = Task.Run(() =>
            {
                OrbitForecast Path(IReadOnlyCollection<Maneuver> n) => Sublight.PredictPath(g, primary, p, vel, t, n, span, goal);
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
            return new ForecastResult(v, screen, false, null, null, 0, aim.Length < 1e-3 ? null : Predictor.Run(g, chart, pos, aim, drag: v.Class.Drag), descent, well);
        });
    }

    /// <summary>Rebuilds the plan from where the ship actually is.</summary>
    void Replan()
    {
        PlanFrozen = false;
        Burn.Forget();   // measured afresh against the new plan
        _forecastDirty = true;
    }

    /// <summary>Distance between the ship and where the frozen plan says it should be now.</summary>
    public double? PlanDeviation()
    {
        if (!PlanFrozen || SubForecast == null || SubForecast.Track.Count == 0) return null;
        var now = V!.AbsoluteTime;
        var planned = SubForecast.Track.OrderBy(t => Math.Abs(t.Time - now)).First();
        return (planned.Position - V.SubPosition).Length;
    }

    double ForecastSpan()
    {
        var v = V!;
        var el = Sublight.OrbitOf(v.Primary.Mu, v.SubPosition, v.SubVelocity);
        var target = v.Waypoint != null ? 3 * Math.PI / v.Waypoint.Omega : 0;
        return Math.Max(el.Bound ? Math.Clamp(el.Period * 1.5, 10, 150) : 60, target);
    }
}

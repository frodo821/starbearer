using System;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>Normal space: the clock, flying the plan's nodes, and the docking assists.</summary>
public partial class Main
{
    /// <summary>Time is running fast up to the next ignition.</summary>
    public bool ToNode { get; private set; }

    /// <summary>Holds real time after N arrives at a node, so the nose can be brought round before ignition.</summary>
    public bool Aligning { get; private set; }

    /// <summary>Days until the burn for the next node should start at the current throttle.</summary>
    public double? IgnitionIn => NextNode is { } n
        ? n.Time - (EngineAcceleration > 0 ? n.DeltaV / EngineAcceleration / 2 : 0) - V!.AbsoluteTime
        : null;

    void TogglePause()
    {
        Paused = !Paused;
        ToNode = false;
        Aligning = false;
        Message = Paused ? "時間停止" : "時間再開";
    }

    void WarpToNode()
    {
        if (NextNode == null) return;
        ToNode = !ToNode;
        Aligning = false;
        if (ToNode) Paused = false;
    }

    /// <summary>The autopilot module: points the nose at the node and lights the engine on time.</summary>
    void FlyNodeAutomatically()
    {
        var v = V!;
        if (!v.AutoBurn || IgnitionIn is not { } wait) return;
        if (wait < 1) Helm.Hold = ViewHold.Node;
        if (Helm.EngineOn || wait > 0 || NodeDirection() is not { } dir || Helm.Nose.Dot(dir) < 0.98) return;
        if (Helm.Throttle <= 0) Helm.SetThrottle(1);
        Helm.LightEngine();
        Paused = false;
        Message = "自動噴射 点火";
    }

    void ToggleAutoBurn()
    {
        if (!Owns(Modules.AutoBurn)) { Message = "自動噴射装置が未搭載"; return; }
        V!.AutoBurn = !V.AutoBurn;
        Message = V.AutoBurn ? "自動噴射 ON" : "自動噴射 OFF";
    }

    void UpdateOrbit(double delta)
    {
        var v = V!;
        FlyNodeAutomatically();
        var acting = !v.Docked && (Helm.EngineOn || IsDown(Key.X) || IsDown(Key.Z));
        if (acting) Aligning = false;
        if (Paused && !acting) { CurrentRate = 0; ToNode = false; Aligning = false; return; }
        var rate = ToNode ? Rates[^1] : Rates[SubRateIndex];
        if (acting) rate = Math.Min(rate, BurnTimeRate);
        if (NextNode is { } n && n.Time > v.AbsoluteTime)
            rate = Math.Min(rate, Math.Max(BurnTimeRate, (n.Time - v.AbsoluteTime) * 1.5));   // don't sail past ignition
        if (ToNode)
        {
            // Spend the wait in a few real seconds and slow down into it, stopping just short of ignition.
            var left = (IgnitionIn ?? 0) - 0.05;
            if (left <= 0 || acting)
            {
                ToNode = false;
                Paused = v.Docked;
                Aligning = !v.Docked && !v.AutoBurn && !acting;
                if (Aligning) Message = "点火の時機。実時間に落とした。機首を合わせて点火";
                if (Paused) { CurrentRate = 0; Message = "点火の時機。U で出港する"; return; }
            }
            else rate = Math.Min(rate, Math.Max(left * 2, 0.02));
        }
        if (Aligning) rate = Rates[0];
        CurrentRate = rate;
        var engine = SublightThrust(delta);
        if (engine.LengthSq > 0)
        {
            if (v.Nodes.Count > 0) PlanFrozen = true;   // from here on the plan is the reference
            _forecastDirty = true;                      // keep "if I stop now" live
            // The auto burn throttles its last moment down to exactly what is left.
            if (v.AutoBurn && NextNode is { } aim && Helm.EngineOn && !IsDown(Key.X) && !IsDown(Key.Z))
            {
                var step = engine.Length * v.SublightThrust * rate * delta;
                if (step > aim.DeltaV) engine *= aim.DeltaV / step;
            }
        }
        var fuelBefore = v.Fuel;
        var firedOn = engine.LengthSq > 0 ? NextNode : null;
        Burn.Prepare(v, firedOn, PlanFrozen ? SubForecast : null);
        var passages = v.Passages;
        v.AdvanceSublight(rate * delta, engine);
        if (v.Passages != passages) PassedGate();
        var burned = v.Fuel < fuelBefore ? engine * (v.SublightThrust * rate * delta) : (Vec3d?)null;
        Burn.Measure(v, firedOn, burned);
        if (firedOn != null && burned != null && firedOn == Burn.Node)
        {
            _forecastDirty = true;
            if (v.AutoBurn && (Burn.Overshot || Burn.Stalled || firedOn.DeltaV < Burn.Planned * 1e-4)) CutEngine("自動噴射 完了");
        }
        if (Burn.Live && !Helm.EngineOn) EndBurn();
        if (v.Phase == Phase.Arrived) Dock();
        else if (v.Phase == Phase.Dead) Screen = Screen.Dead;
    }

    /// <summary>X and Z point the nose for the pilot and fire once it is on target.</summary>
    Vec3d SublightThrust(double delta)
    {
        var v = V!;
        if (v.Docked) { Helm.Firing = false; return Vec3d.Zero; }
        var here = v.LocalStation != null;
        Vec3d? assist = null;
        if (here && (IsDown(Key.X) || IsDown(Key.Z)))
        {
            var want = v.AssistTowardStation(approach: IsDown(Key.X))!.Value;
            if (want.LengthSq == 0) { Helm.Firing = false; return Vec3d.Zero; }
            assist = want;
        }
        if (assist is not { } dir) return EngineCommand();
        Helm.Hold = ViewHold.Free;
        Helm.Slew(dir, delta);
        Helm.Firing = Helm.Nose.Dot(dir) > 0.99;
        return Helm.Firing ? Helm.Nose : Vec3d.Zero;
    }

    /// <summary>Out of a gate into another system: the plan is gone and the chart follows.</summary>
    void PassedGate()
    {
        var v = V!;
        var region = G!.Regions[G.Stars[v.SystemStar].Region].Name;
        ShowCue(new StateCue("ゲート通過", $"{v.System.Name} 星系 / {region} 宙域", new Color(0.8f, 0.6f, 1f), Major: true, Outward: true));
        Message = $"{v.System.Name} のゲートへ出た。通行税 {v.TollsDue:F0} は次の入港で精算";
        Selected = null;
        Chart.Show(PrimaryOnChart, ChartView.ZoomFor(v.Primary));
        Replan();
    }

    /// <summary>The burn on the node being flown.</summary>
    public NodeBurn Burn { get; } = new();

    /// <summary>The burn has gone past the node: now is the time to stop.</summary>
    public bool NodeOvershot => Burn.Overshot;

    void EndBurn()
    {
        var node = Burn.Node!;
        switch (Burn.Stop(V!))
        {
            case NodeBurn.Ending.Short:
                Message = $"ノード残 Δv {node.DeltaV:F4}。続けるなら再点火";
                break;
            case NodeBurn.Ending.Done:
                if (Selected == node) Selected = NextNode;
                Message = $"ノード完了 (誤差 Δv {node.DeltaV:F4})" + (V!.Nodes.Count > 0 ? "。残りの段を現在の軌道で計画し直した" : "");
                Replan();   // what this stage actually achieved is now the baseline for the rest
                break;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

public partial class Main
{

    public void AddNode(double time)
    {
        Selected = new Maneuver { Time = Math.Max(time, V!.AbsoluteTime) };
        V.Nodes.Add(Selected);
        Paused = true;
        HandlesOpen = true;
        Replan();
    }

    /// <summary>Tapping a node shows its handles; tapping it again puts them away.</summary>
    public void SelectNode(Maneuver m)
    {
        HandlesOpen = Selected != m || !HandlesOpen;
        Selected = m;
        Paused = true;
    }

    public void SelectNodeQuietly(Maneuver m) => Selected = m;

    void CycleNode(int step)
    {
        var nodes = V!.Nodes.OrderBy(n => n.Time).ToList();
        if (nodes.Count == 0) return;
        var i = Selected != null ? nodes.IndexOf(Selected) : -1;
        Selected = nodes[(i + step + nodes.Count) % nodes.Count];
        HandlesOpen = true;
        Paused = true;
    }
    public void NodeEdited() => Replan();

    /// <summary>Rebuilds the plan from where the ship actually is.</summary>
    void Replan()
    {
        PlanFrozen = false;
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

    public static string NameOf(IOrbiter? o) => o switch { Center c => c.Name, Station => "港", _ => "" };

    /// <summary>Why there is nothing to steer for from here.</summary>
    string NoWaypoint => V!.Goal switch
    {
        null => "この星系に港はない。C で目標を選ぶ",
        Center c when c.Key == V.Primary.Key => $"{c.Name} を周回中。目標に着いている",
        var goal => $"{NameOf(goal)}はこの天体の外にある。まずここを離れる",
    };

    /// <summary>Picks the target the plans and the chart's markers are for. Null goes back to the station.</summary>
    public void SetTarget(IOrbiter? target)
    {
        var v = V!;
        v.ChosenTarget = target is Station ? null : target;
        Message = $"目標: {NameOf(v.Goal)}" + (v.Goal == null ? "なし" : "");
        Replan();
    }

    /// <summary>The station, then every planet and moon of the system, round again.</summary>
    public List<IOrbiter> TargetChoices()
    {
        var v = V!;
        var list = new List<IOrbiter>();
        if (v.LocalStation is { } st) list.Add(st);
        list.AddRange(G!.System(v.SystemStar).Tree.Skip(1).Where(c => c.Key != v.Primary.Key));
        return list;
    }

    void CycleTarget()
    {
        var choices = TargetChoices();
        if (choices.Count == 0) return;
        var at = V!.Goal is { } g ? choices.IndexOf(g) : -1;
        SetTarget(choices[(at + 1) % choices.Count]);
    }

    /// <summary>The way out of a body that doesn't lead to the goal.</summary>
    void PlanEscape()
    {
        if (V!.Goal == null || V.Primary.Parent == null || V.Goal is Center c && c.Key == V.Primary.Key) { Message = NoWaypoint; return; }
        Selected = Sublight.SuggestEscape(V.Primary, V.SubPosition, V.SubVelocity, V.AbsoluteTime);
        V.Nodes.Add(Selected);
        Paused = true; HandlesOpen = false;
        Message = $"{V.Primary.Name} を離れる噴射を計画した";
        Replan();
    }

    void SuggestTransfer()
    {
        if (V!.Waypoint is not { } st) { PlanEscape(); return; }
        if (Sublight.SuggestCapture(V.Primary, st, V.SubPosition, V.SubVelocity, V.AbsoluteTime) is { } capture)
        {
            V.Nodes.Clear();
            V.Nodes.AddRange(capture);
            Selected = capture[0];
            Paused = true; HandlesOpen = false;
            Message = $"{V.Primary.Name} に捕まるための噴射を {capture.Count} 段計画した";
            Replan();
            return;
        }
        var added = 0;
        if (V.Nodes.Count == 0 && Sublight.SuggestPlaneMatch(V.Primary, st, V.SubPosition, V.SubVelocity, V.AbsoluteTime) is { } plane)
        {
            V.Nodes.Add(plane);
            added++;
        }
        Selected = Sublight.SuggestTransfer(V.Primary, st, V.SubPosition, V.SubVelocity, V.AbsoluteTime, V.Nodes.OrderBy(n => n.Time).ToList());
        V.Nodes.Add(Selected);
        added++;
        Paused = true; HandlesOpen = false;
        Message = added == 2 ? "面合わせと遷移の2段を置いた。各段を調整し、M で合流噴射を足す" : "遷移の叩き台を置いた。ノードをタップして調整し、M で合流噴射を足す";
        Replan();
    }

    void AddMatchBurn()
    {
        if (V!.Waypoint is not { } st) { Message = NoWaypoint; return; }
        var m = Sublight.MatchAtClosest(V.Primary, st, V.SubPosition, V.SubVelocity, V.AbsoluteTime, V.Nodes.OrderBy(n => n.Time).ToList(), ForecastSpan());
        if (m == null) { Message = "合流点が見つからない"; return; }
        V.Nodes.Add(m);
        Selected = m;
        Paused = true; HandlesOpen = false;
        Message = $"合流噴射を置いた (Δv {m.DeltaV:F3})。N で点火点まで進む";
        Replan();
    }

    /// <summary>Deletes the selected node, or the next one if the selection has gone stale.</summary>
    public void DeleteNode()
    {
        var v = V!;
        var target = Selected != null && v.Nodes.Contains(Selected) ? Selected : NextNode;
        if (target == null) { Selected = null; return; }
        v.Nodes.Remove(target);
        Selected = NextNode;
        if (Selected == null) HandlesOpen = false;
        Message = v.Nodes.Count > 0 ? $"ノードを削除 (残り {v.Nodes.Count})" : "ノードをすべて削除";
        Replan();
    }

    void ClearNodes()
    {
        V!.Nodes.Clear();
        Selected = null;
        HandlesOpen = false;
        Message = "ノードをすべて削除";
        Replan();
    }

    public Maneuver? NextNode => V?.Nodes.OrderBy(n => n.Time).FirstOrDefault();

    void RunTransferComputer()
    {
        if (!Owns(Modules.Transfer)) { Message = "転送計算機が未搭載"; return; }
        if (V!.Waypoint is not { } st) { Message = NoWaypoint; return; }
        var plan = Sublight.PlanRendezvous(V.Primary, st, V.SubPosition, V.SubVelocity, V.AbsoluteTime);
        if (plan == null) { Message = "転送計算機: 解が見つからない"; return; }
        V.Nodes.Clear();
        V.Nodes.AddRange(plan);
        Selected = plan[0];
        HandlesOpen = false;
        Message = $"転送計算機: {plan.Count}段の噴射を計画 (Δv {plan.Sum(n => n.DeltaV):F2})";
        Replan();
    }

    public double? NoseOffNode => NodeDirection() is { } d ? Math.Acos(Math.Clamp(Nose.Dot(d), -1, 1)) * 180 / Math.PI : null;

    /// <summary>The autopilot module: points the nose at the node and lights the engine on time.</summary>
    void FlyNodeAutomatically()
    {
        var v = V!;
        if (!v.AutoBurn || IgnitionIn is not { } wait) return;
        if (wait < 1) Hold = ViewHold.Node;
        if (EngineOn || wait > 0 || NodeDirection() is not { } dir || Nose.Dot(dir) < 0.98) return;
        if (Throttle <= 0) Throttle = 1;
        EngineOn = true;
        Paused = false;
        Message = "自動噴射 点火";
    }

    void ToggleAutoBurn()
    {
        if (!Owns(Modules.AutoBurn)) { Message = "自動噴射装置が未搭載"; return; }
        V!.AutoBurn = !V.AutoBurn;
        Message = V.AutoBurn ? "自動噴射 ON" : "自動噴射 OFF";
    }

    void WarpToNode()
    {
        if (NextNode == null) return;
        ToNode = !ToNode;
        Aligning = false;
        if (ToNode) Paused = false;
    }

    void UpdateOrbit(double delta)
    {
        var v = V!;
        if (Selected != null)
        {
            var step = (Input.IsKeyPressed(Key.Shift) ? 0.006 : 0.06) * delta;
            var pro = (IsDown(Key.W) ? 1 : 0) - (IsDown(Key.S) ? 1 : 0);
            var rad = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
            var nor = (Input.IsKeyPressed(Key.E) ? 1 : 0) - (Input.IsKeyPressed(Key.Q) ? 1 : 0);
            if (pro != 0 || rad != 0 || nor != 0)
            {
                Selected.Prograde += pro * step;
                Selected.Radial += rad * step;
                Selected.Normal += nor * step;
                Replan();
            }
        }
        FlyNodeAutomatically();
        var acting = !v.Docked && (EngineOn || IsDown(Key.X) || IsDown(Key.Z));
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
        }
        var fuelBefore = v.Fuel;
        v.AdvanceSublight(rate * delta, engine);
        if (engine.LengthSq > 0 && v.Fuel < fuelBefore && NextNode is { } burning)
        {
            var flown = engine * (Tuning.SublightThrust * rate * delta);
            if (SubForecast?.StageOf(burning) is { } stage)
                v.ConsumeNode(burning, flown, stage.Position, stage.Velocity);
            else
                v.ConsumeNode(burning, flown);
            _forecastDirty = true;
            if (!v.Nodes.Contains(burning))
            {
                if (Selected == burning) Selected = NextNode;
                CutEngine(v.Nodes.Count > 0 ? "ノード完了。残りの段を現在の軌道で計画し直した" : "ノード完了。エンジン停止");
                Replan();   // what this stage actually achieved is now the baseline for the rest
            }
        }
        if (v.Phase == Phase.Arrived) Dock();
        else if (v.Phase == Phase.Dead) Screen = Screen.Dead;
    }
}

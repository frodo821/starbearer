using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>The plan in normal space: nodes, the target, and the suggestions that place burns.</summary>
public partial class Main
{
    public Maneuver? Selected;
    public bool HandlesOpen;
    public Maneuver? NextNode => V?.Nodes.OrderBy(n => n.Time).FirstOrDefault();
    public double? NoseOffNode => NodeDirection() is { } d ? Math.Acos(Math.Clamp(Helm.Nose.Dot(d), -1, 1)) * 180 / Math.PI : null;

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

    /// <summary>Picks a node and opens its handles and panel on the chart.</summary>
    public void OpenNode(Maneuver m)
    {
        Selected = m;
        HandlesOpen = true;
        Paused = true;
    }

    /// <summary>The node before or after the one picked, in time order.</summary>
    public void StepNode(int by)
    {
        var ordered = V!.Nodes.OrderBy(n => n.Time).ToList();
        if (ordered.Count == 0) return;
        var i = Selected is { } m ? ordered.IndexOf(m) : -1;
        OpenNode(ordered[((i < 0 ? 0 : i + by) % ordered.Count + ordered.Count) % ordered.Count]);
    }

    /// <summary>Δv added to the picked node along one of its axes: 0 prograde, 1 radial, 2 normal.</summary>
    public void NudgeNode(int axis, double dv)
    {
        if (Selected is not { } n || !V!.Nodes.Contains(n)) return;
        if (axis == 0) n.Prograde += dv; else if (axis == 1) n.Radial += dv; else n.Normal += dv;
        NodeEdited();
    }
    public void NodeEdited() => Replan();

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

    /// <summary>A target as the pilot reads it: a body by name, a station by what it is.</summary>
    /// <summary>The company's ships left in orbit in this system, as orbits to steer for.</summary>
    public System.Collections.Generic.IEnumerable<ParkedOrbit> ParkedHere() =>
        Save!.Fleet.Where(x => x.Parked is { } p && p.Star == V!.SystemStar).Select(x => x.Parked!.Orbit(G!, x.Id));

    public string NameOf(IOrbiter? o) => o switch { Center c => c.Name, ParkedOrbit po => $"自船 {Save!.Fleet.FirstOrDefault(x => x.Id == po.ShipId)?.Type.Name}", Station st when G!.IsStation(st.Id) => Missions.KindLabel(G.KindOf(st.Id)), Station => "港", _ => "" };

    /// <summary>Why there is nothing to steer for from here.</summary>
    string NoWaypoint => V!.Goal switch
    {
        null => (V.LocalStation == null ? "この星系に港はない。" : "目標なし。") + "「目標」で目標を選ぶ",
        Center c when c.Key == V.Primary.Key => $"{c.Name} を周回中。目標に着いている",
        var goal => $"{NameOf(goal)}はこの天体の外にある。まずここを離れる",
    };

    /// <summary>Picks the target the plans and the chart's markers are for. Null goes back to the station.</summary>
    public void SetTarget(IOrbiter? target)
    {
        var v = V!;
        v.SetGoal(target);
        Message = "目標: " + (v.Goal == null ? "なし" : NameOf(v.Goal));
        Replan();
    }

    /// <summary>The stations (the one the ship deals with first), then every planet and moon of the system, round again.</summary>
    public List<IOrbiter> TargetChoices()
    {
        var v = V!;
        var list = new List<IOrbiter>();
        if (v.LocalStation is { } st) list.Add(st);
        list.AddRange(G!.StationsIn(v.SystemStar).Where(id => id != v.LocalStation?.Id).Select(G.StationAt));
        list.AddRange(G!.System(v.SystemStar).Tree.Skip(1).Where(c => c.Key != v.Primary.Key));
        list.AddRange(ParkedHere());
        return list;
    }

    /// <summary>Plans for a target picked on the chart: targets it first, then runs the planner of that console key.</summary>
    public void PlanFor(IOrbiter target, Key planner)
    {
        if (V!.Goal != target) SetTarget(target);
        if (planner == Key.H) SuggestTransfer();
        else if (planner == Key.M) AddMatchBurn();
        else if (planner == Key.T) RunTransferComputer();
    }

    /// <summary>Through the choices and then none, round again.</summary>
    void CycleTarget()
    {
        var choices = TargetChoices().Cast<IOrbiter?>().Append(null).ToList();
        var at = choices.IndexOf(V!.Goal);
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
        Message = added == 2 ? "面合わせと遷移の2段を置いた。各段を調整し、「合流案」で合流噴射を足す" : "遷移の叩き台を置いた。ノードをタップして調整し、「合流案」で合流噴射を足す";
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
        Message = $"合流噴射を置いた (Δv {Instruments.Speed(m.DeltaV)})。N で点火点まで進む";
        Replan();
    }

    void RunTransferComputer()
    {
        if (!Owns(Modules.Transfer)) { Message = "転送計算機が未搭載"; return; }
        if (V!.Waypoint is not { } st) { Message = NoWaypoint; return; }
        var plan = Sublight.PlanRendezvous(V.Primary, st, V.SubPosition, V.SubVelocity, V.AbsoluteTime, V.SublightDeltaV);
        if (plan == null) { Message = "転送計算機: 解が見つからない。待てば窓が来ることもある"; return; }
        V.Nodes.Clear();
        V.Nodes.AddRange(plan);
        Selected = plan[0];
        HandlesOpen = false;
        var total = plan.Sum(n => n.DeltaV);
        Message = $"転送計算機: {plan.Count}段の噴射を計画 (Δv {Instruments.Speed(total)})" + (total > V.SublightDeltaV ? "。推進剤が足りない: 最も安い案" : "");
        Replan();
    }
}

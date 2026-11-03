using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>The orbit readout on the left: elements, the goal, the forecast's events and the modules.</summary>
public partial class Hud
{
    void DrawOrbitPanel(float x, float y)
    {
        var v = Game.V!;
        var el = Sublight.OrbitOf(v.Primary.Mu, v.SubPosition, v.SubVelocity);
        var (pe, ap) = Sublight.NextApsides(v.Primary.Mu, v.SubPosition, v.SubVelocity, v.AbsoluteTime);
        void Line(string s, Color c) { Text(new Vector2(x, y), s, c, 15); y += 20; }
        string When(Apsis? a) => a is { } x ? $"  {a.Value.Time - v.AbsoluteTime:F1}日後" : "";
        Line($"Pe {el.Periapsis:F3} AU" + When(pe), Ink);
        Line(el.Bound ? $"Ap {el.Apoapsis:F3} AU" + When(ap) : "Ap なし  脱出軌道", el.Bound ? Ink : Warn);
        Line($"{(el.Bound ? $"周期 {el.Period:F1} 日   " : "")}{(Game.CurrentRate == 0 ? "一時停止 (Space)" : $"時間 {Instruments.Rate(Game.CurrentRate)}")}", Dim);
        Line($"亜光速 Δv 残 {v.SublightDeltaV:F2}", Ink);
        var st = v.LocalStation;
        var goal = v.Goal;
        var watched = Game.SubForecast?.Watched;
        Line($"主天体 {v.Primary.Name}", Dim);
        if (goal != null)
        {
            var (goalAt, goalSpeed) = v.RelativeNow(goal);
            var d = (goalAt - v.SubPosition).Length;
            var rel = (goalSpeed - v.SubVelocity).Length;
            var refused = goal is Station && v.CanDock && !Game.DockAdmitted;
            var dockable = goal is Station && v.CanDock && !refused;
            Line($"目標 {Main.NameOf(goal)}  {d:F4} AU   相対 {rel:F4}" + (dockable ? "   入港可 U" : refused ? "   入港拒否" : ""), dockable ? Good : refused ? Warn : goal is Station && d < Tuning.DockRange * 10 ? Warn : Ink);
        }
        if (watched != null)
        {
            var name = Main.NameOf(watched);
            if (watched != goal) Line($"基準 {name} ({Main.NameOf(goal)} への経由)", Dim);
            var incl = Sublight.RelativeInclination(watched, v.SubPosition, v.SubVelocity);
            var last = Game.SubForecast?.Stages.LastOrDefault();   // the plan within this star's reach
            var planned = last is { Node: not null } ls ? Sublight.RelativeInclination(watched, ls.Position, ls.Velocity + Sublight.Impulse(ls.Position, ls.Velocity, ls.Node)) : incl;
            Line($"相対傾斜 {incl:F1}°" + (last is { Node: not null } ? $" → {planned:F1}°" : ""), planned < 1.5 ? Good : Warn);
            if (Game.SubForecast?.Closest is { } a)
                Line($"最接近 {a.Distance:F3} AU  {a.Time - v.AbsoluteTime:F1}日後  相対 {a.RelativeSpeed:F2}", a.Distance < 0.05 ? Good : Warn);
            if (Game.SubForecast?.Crossing is { } x2)
                Line($"交差時の{name} {Math.Abs(x2.LeadDegrees):F0}° {(x2.LeadDegrees >= 0 ? "先" : "後ろ")}  {x2.Time - v.AbsoluteTime:F1}日後", Math.Abs(x2.LeadDegrees) < 8 ? Good : Warn);
            else Line($"{name}の軌道半径に届いていない", Warn);
        }
        else if (goal != null && v.Waypoint == null) Line(goal is Center c && c.Key == v.Primary.Key ? "目標を周回中" : "目標はこの天体の外", Dim);
        if (st != null && goal is Station)
            Line($"港 半径 {st.Radius:F2} AU   入港 {Tuning.DockRange:F3} AU / 相対 {Tuning.DockRelativeSpeed:F3}", Dim);
        if (Game.SubForecast?.Impact == true) Line("天体に衝突する軌道", Bad);
        y += 4;
        var fc = Game.SubForecast;
        var live = fc?.AllStages.Where(s => v.Nodes.Contains(s.Node)).ToList() ?? new List<Stage>();
        if (Game.SubCurrent is { } now && (live.Count > 0 || Game.Helm.EngineOn))
        {
            var e3 = now.After;
            var stop = e3.Bound ? $"近 {e3.Periapsis:F2} 遠 {e3.Apoapsis:F2}" : "脱出軌道";
            Line($"今止めると {stop}" + (now.Closest is { } ca ? $"  {Main.NameOf(now.Watched)}まで {ca.Distance:F3}" : ""), Color.Color8(255, 255, 255));
        }
        if (Game.PlanDeviation() is { } dev)
            Line($"計画固定中  ずれ {dev:F3} AU   「再計画」で作り直す", dev > 0.05 ? Warn : Good);
        if (live.Count > 0)
        {
            var total = live.Sum(s => s.Node.DeltaV);
            Line($"計画 {live.Count} 段  Δv {total:F3} / 残 {v.SublightDeltaV:F2}", total > v.SublightDeltaV ? Bad : Ink);
            for (int i = 0; i < Math.Min(live.Count, 5); i++)
            {
                var s = live[i];
                var left = s.Node.Time - v.AbsoluteTime;
                var when = left > 0 ? $"{left:F1}日後" : "点火!";
                var orbit = s.After.Bound ? $"{s.After.Periapsis:F2}-{s.After.Apoapsis:F2}" : "脱出";
                Line($"{i + 1}. {when}  Δv {s.Node.DeltaV:F3}  → {orbit}", s.Node == Game.Selected ? Warn : left <= 0.3 ? Warn : Ink);
            }
        }
        if (Game.NextNode is { } burn)
        {
            Line($"ノード残 Δv {burn.DeltaV:F4}   スロットル {Game.Helm.Throttle * 100:F0}%" + (Game.NodeOvershot ? "  過ぎた" : ""), Game.NodeOvershot ? Warn : Ink);
            if (Game.EngineAcceleration > 0)
            {
                var days = burn.DeltaV / Game.EngineAcceleration;
                Line($"噴射 {days:F2} 日 (実時間 {days / Game.BurnTimeRate:F1} 秒)", days > 3 ? Warn : Good);
                var start = burn.Time - days / 2 - v.AbsoluteTime;
                Line(start > 0 ? $"点火推奨 {start:F2} 日後" : "点火推奨 今", start < 0.3 ? Warn : Ink);
            }
            else Line("スロットル0%: 噴射できない (W で上げる)", Warn);
            if (Game.NoseOffNode is { } off)
                Line($"機首と噴射方向のずれ {off:F1}°" + (off > 5 ? "  「噴」をタップ" : ""), off > 5 ? Warn : Good);
        }
        if (Game.Selected is { } n && v.Nodes.Contains(n)) Line($"順 {n.Prograde:+0.000;-0.000} 径 {n.Radial:+0.000;-0.000} 法 {n.Normal:+0.000;-0.000}", Warn);
        y += 4;
        Line($"計算機 {(Game.Owns(Modules.Transfer) ? "搭載" : "なし")}   自動噴射 {(Game.Owns(Modules.AutoBurn) ? (v.AutoBurn ? "ON" : "OFF") : "なし")}", Dim);
    }
}

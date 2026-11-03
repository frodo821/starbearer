using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>Navigation in orbit, in three parts: the orbit flown, the target and how it is coming, and the plan of burns.</summary>
public partial class Hud
{
    void DrawOrbitNav(float x, ref float y, float width)
    {
        var v = Game.V!;
        var yy = y;
        void Line(string s, Color c, int size = 15) { Text(new Vector2(x, yy + 14), s, c, size); yy += 20; }

        // The orbit.
        Section(x, ref yy, width, "軌道");
        var el = Sublight.OrbitOf(v.Primary.Mu, v.SubPosition, v.SubVelocity);
        var (pe, ap) = Sublight.NextApsides(v.Primary.Mu, v.SubPosition, v.SubVelocity, v.AbsoluteTime);
        string When(Apsis? a) => a is { } x ? "  " + Instruments.Countdown(x.Time - v.AbsoluteTime) : "";
        Line($"Pe {Instruments.Distance(el.Periapsis)}" + When(pe), Ink);
        Line(el.Bound ? $"Ap {Instruments.Distance(el.Apoapsis)}" + When(ap) : "Ap なし  脱出軌道", el.Bound ? Ink : Warn);
        Line($"主天体 {v.Primary.Name}" + (el.Bound ? $"   周期 {Instruments.Span(el.Period)}" : ""), Dim);
        Line($"速度 {Instruments.Speed(v.SubVelocity.Length)}   位置誤差 ±{v.NavSigma:F3} ly", v.NavSigma > 0.1 ? Warn : Dim);
        if (Game.SubForecast?.Impact == true) Line("天体に衝突する軌道", Bad);

        // The target: what it is, how the ship is coming to it, and the plans that bring it there.
        Section(x, ref yy, width, "目標");
        Button(new Vector2(x, yy), Game.TargetCommand, width);
        yy += ButtonHeight + 6;
        var st = v.LocalStation;
        var goal = v.Goal;
        var watched = Game.SubForecast?.Watched;
        if (goal != null)
        {
            var (goalAt, goalSpeed) = v.RelativeNow(goal);
            var d = (goalAt - v.SubPosition).Length;
            var rel = (goalSpeed - v.SubVelocity).Length;
            var refused = goal is Station && v.CanDock && !Game.DockAdmitted;
            var dockable = goal is Station && v.CanDock && !refused;
            Line($"距離 {Instruments.Distance(d)}   相対 {Instruments.Speed(rel)}" + (dockable ? "   入港可 U" : refused ? "   入港拒否" : ""), dockable ? Good : refused ? Warn : goal is Station && d < Tuning.DockRange * 10 ? Warn : Ink);
        }
        if (watched != null)
        {
            var name = Game.NameOf(watched);
            if (watched != goal) Line($"基準 {name} ({Game.NameOf(goal)} への経由)", Dim);
            var incl = Sublight.RelativeInclination(watched, v.SubPosition, v.SubVelocity);
            var last = Game.SubForecast?.Stages.LastOrDefault();   // the plan within this star's reach
            var planned = last is { Node: not null } ls ? Sublight.RelativeInclination(watched, ls.Position, ls.Velocity + Sublight.Impulse(ls.Position, ls.Velocity, ls.Node)) : incl;
            Line($"相対傾斜 {incl:F1}°" + (last is { Node: not null } ? $" → {planned:F1}°" : ""), planned < 1.5 ? Good : Warn);
            if (Game.SubForecast?.Closest is { } a)
                Line($"最接近 {Instruments.Distance(a.Distance)}  {Instruments.Countdown(a.Time - v.AbsoluteTime)}  相対 {Instruments.Speed(a.RelativeSpeed)}", a.Distance < Tuning.DockRange * 50 ? Good : Warn);
            if (Game.SubForecast?.Crossing is { } x2)
                Line($"交差時の{name} {Math.Abs(x2.LeadDegrees):F0}° {(x2.LeadDegrees >= 0 ? "先" : "後ろ")}  {Instruments.Countdown(x2.Time - v.AbsoluteTime)}", Math.Abs(x2.LeadDegrees) < 8 ? Good : Warn);
            else Line($"{name}の軌道半径に届いていない", Warn);
        }
        else if (goal != null && v.Waypoint == null) Line(goal is Center c && c.Key == v.Primary.Key ? "目標を周回中" : "目標はこの天体の外", Dim);
        if (st != null && goal is Station)
            Line($"入港の条件  {Instruments.Distance(Tuning.DockRange)} 以内 / 相対 {Instruments.Speed(Tuning.DockRelativeSpeed)} 以下", Dim, 13);
        yy += 2;
        ButtonRow(x, yy, Game.TargetCommands());
        yy += ButtonHeight + 4;

        // The plan: each burn a row to pick (its handles and panel open on the chart), and the whole of it.
        Section(x, ref yy, width, "計画");
        var fc = Game.SubForecast;
        var live = fc?.AllStages.Where(s => v.Nodes.Contains(s.Node)).ToList() ?? new List<Stage>();
        if (live.Count == 0) Line("ノードなし  軌道をタップで置く", Dim, 14);
        else
        {
            var total = live.Sum(s => s.Node.DeltaV);
            Line($"{live.Count} 段  Δv {Instruments.Speed(total)} / 残 {Instruments.Speed(v.SublightDeltaV)}", total > v.SublightDeltaV ? Bad : Ink);
            for (int i = 0; i < Math.Min(live.Count, 5); i++)
            {
                var s = live[i];
                var left = s.Node.Time - v.AbsoluteTime;
                var when = left > 0 ? Instruments.Countdown(left) : "点火!";
                var orbit = s.After.Bound ? $"{Instruments.Distance(s.After.Periapsis)}-{Instruments.Distance(s.After.Apoapsis)}" : "脱出";
                var row = new Rect2(x - 4, yy, width + 8, 20);
                var node = s.Node;
                _clicks.Add((row, () => Game.OpenNode(node)));
                if (node == Game.Selected) DrawRect(row, new Color(Warn, 0.12f));
                Line($"{i + 1}. {when}  Δv {Instruments.Speed(node.DeltaV)}  → {orbit}", node == Game.Selected ? Warn : left <= 0.3 ? Warn : Ink);
            }
        }
        if (Game.SubCurrent is { } now && (live.Count > 0 || Game.Helm.EngineOn))
        {
            var e3 = now.After;
            var stop = e3.Bound ? $"近 {Instruments.Distance(e3.Periapsis)} 遠 {Instruments.Distance(e3.Apoapsis)}" : "脱出軌道";
            Line($"今止めると {stop}" + (now.Closest is { } ca ? $"  {Game.NameOf(now.Watched)}まで {Instruments.Distance(ca.Distance)}" : ""), Color.Color8(255, 255, 255), 14);
        }
        if (Game.PlanDeviation() is { } dev)
            Line($"計画固定中  ずれ {Instruments.Distance(dev)}   「再計画」で作り直す", dev > Tuning.DockRange * 50 ? Warn : Good, 14);
        yy += 2;
        ButtonRow(x, yy, Game.PlanCommands());
        yy += ButtonHeight;
        y = yy;
    }
}

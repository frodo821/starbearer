using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>The clock and the instrument panels.</summary>
public partial class Hud
{
    /// <summary>Mission clock, time rate, and the countdown to whatever comes next.</summary>
    void DrawClock()
    {
        var v = Game.V!;
        var x = Size.X - 345f;
        var y = 188f;
        DrawRect(new Rect2(x - 12, y - 14, 345, 96), new Color(0.01f, 0.02f, 0.05f, 0.72f));
        Text(new Vector2(x, y + 6), Instruments.Clock(v.Time), Ink, 22);
        Text(new Vector2(x + 192, y + 6), "航海", Dim, 12);

        var running = Game.Screen != Screen.Plan && Game.CurrentRate > 0;
        var rate = running ? Instruments.Rate(Game.CurrentRate) : "時間停止";
        Text(new Vector2(x + 230, y + 6), rate, running ? Ink : Warn, 14);
        Text(new Vector2(x, y + 28), Instruments.Clock(v.AbsoluteTime), Dim, 15);
        Text(new Vector2(x + 132, y + 28), "通算", Dim, 12);

        var (label, left) = NextEvent();
        if (left is { } t)
        {
            Text(new Vector2(x, y + 54), Instruments.Countdown(t), t < 1 ? Warn : Good, 19);
            Text(new Vector2(x + 180, y + 54), label, Dim, 13);
        }
        else Text(new Vector2(x, y + 54), "予定なし", Dim, 15);
    }

    (string, double?) NextEvent()
    {
        var v = Game.V!;
        if (v.Phase == Phase.Sublight)
        {
            if (Game.IgnitionIn is { } ignition) return ("次の点火推奨", ignition);
            if (Game.SubCurrent is { Closest: { } c } now) return ($"{Game.NameOf(now.Watched)}への最接近", c.Time - v.AbsoluteTime);
            return ("", null);
        }
        if (Game.Forecast is not { } fc) return ("", null);
        if (fc.Encounters.Count > 0)
            return ($"{v.G.Stars[fc.Encounters[0].Star].Name} 近日点", fc.Encounters[0].Time);
        return fc.End == PredictionEnd.Stall ? ("失速", fc.Duration) : ("", null);
    }

    public string ClockTextForTest => Instruments.Clock(Game.V!.Time);

    void DrawInstruments()
    {
        var v = Game.V!; var g = Game.G!;
        DrawRect(new Rect2(10, 8, 345, Game.Screen == Screen.Orbit ? 700 : 420), new Color(0.01f, 0.02f, 0.05f, 0.72f));   // keeps gauges legible over the chart and the sky
        float x = 24, y = 34;
        Text(new Vector2(x, y), (v.Hold.Count == 0 ? "空荷" : $"積荷 {v.Hold.Count} 件") + $"  →  {g.Stars[v.TargetStar].Name}", Good, 20); y += 28;
        Text(new Vector2(x, y), $"質量 {v.Mass:F2}   積荷 {v.CargoMass:F1}   容積 {Missions.Cubic(Missions.VolumeUsed(v.Hold))}/{Missions.Cubic(v.Class.Capacity)}" + (v.Hold.Count > 0 ? $"   耐熱 {v.HeatTolerance:F2}" : ""), Dim, 15); y += 20;
        foreach (var c in v.Hold.Take(3)) { Text(new Vector2(x, y), c.IsJob ? $"{c.Mission.Cargo.Name} → {g.StarAt(c.Mission.Target).Name}" : $"{c.Mission.Cargo.Name} {c.Mission.Cargo.Volume:F0} 単位（自分の荷）", c.IsJob && g.StarOf(c.Mission.Target) == v.TargetStar ? Ink : Dim, 14); y += 18; }
        y += 8;
        Bar(x, ref y, "燃料", v.Fuel / v.Class.Tank, Ink, $"Δv {v.DeltaVRemaining:F2}");
        if (Game.Screen == Screen.Plan)
        {
            // Ghost of what the kick would spend.
            var after = Math.Max(0, v.Fuel - v.FuelFor(Game.PlanDv)) / v.Class.Tank;
            var now = v.Fuel / v.Class.Tank;
            DrawRect(new Rect2(x + 44 + 180 * (float)after, y - 21, 180 * (float)(now - after), 11), Warn);
        }
        Bar(x, ref y, "船体", v.Hull, v.Hull < 0.4 ? Bad : Ink, $"{v.Hull:P0}");
        Bar(x, ref y, "積荷", v.CargoIntegrity, v.CargoIntegrity < 0.6 ? Warn : Ink, $"{v.CargoIntegrity:P0}");
        Bar(x, ref y, "熱", Math.Min(v.Heat, 1.5) / 1.5, v.Heat > 1 ? Bad : v.Heat > v.HeatTolerance ? Warn : Ink, $"{v.Heat:F2}");
        Bar(x, ref y, "生命", v.LifeRemaining / Tuning.LifeSupportDays, Ink, v.Docked ? $"{v.LifeRemaining:F0}日 停泊中" : $"{v.LifeRemaining:F0}日");
        y += 24;
        Text(new Vector2(x, y), $"速度 {(v.Phase == Phase.Sublight ? v.SubVelocity : v.Velocity).Length:F3}", Ink, 16); y += 24;
        Text(new Vector2(x, y), $"位置誤差 ±{v.NavSigma:F3} ly", v.NavSigma > 0.1 ? Warn : Ink, 16); y += 24;
        if (Game.Screen != Screen.Orbit)
        {
            Text(new Vector2(x, y), $"残 Δv {v.DeltaVRemaining:F2}   推力 {Game.EngineAcceleration:F2} /日", Ink, 16); y += 24;
        }
        y += 8;

        if (Game.Screen == Screen.Orbit)
        {
            DrawOrbitPanel(x, y);
            DrawFtlState();
            DrawCompass();
            if (Game.OrbitSky) DrawContacts();
            DrawClock();
            return;
        }

        var fc = Game.Forecast;
        if (Game.Screen == Screen.Plan)
        {
            Text(new Vector2(x, y), $"キック Δv {Game.PlanDv:F3}  (燃料 {v.FuelFor(Game.PlanDv):F2})", Warn, 18); y += 26;
            if (v.Phase == Phase.Sublight)
            {
                var gain = v.SubVelocity.Dot(Game.PlanDir.Normalized());
                Text(new Vector2(x, y), $"軌道速度の寄与 {gain:+0.00;-0.00}  {(gain >= 0 ? "追い風" : "向かい風")}", gain >= 0 ? Good : Warn, 15); y += 22;
            }
        }
        if (fc != null)
        {
            Text(new Vector2(x, y), "予測", Dim, 15); y += 22;
            foreach (var e in fc.Encounters.Take(5))
            {
                var isT = e.Star == v.TargetStar;
                var line = $"{e.Time,5:F1}日  {g.Stars[e.Star].Name}  近日 {e.Periapsis:F2}";
                if (isT) line += $"  減速 Δv {Game.CaptureDv(e):F2}";
                Text(new Vector2(x, y), line, isT ? Good : Ink, 15); y += 20;
                if (isT)
                {
                    var el = Sublight.DescentOrbit(g.Stars[e.Star], e.Periapsis, e.PeriapsisSpeed);
                    Text(new Vector2(x + 12, y), el.Bound ? $"近日点で降下 → {el.Periapsis:F2}-{el.Apoapsis:F2} AU" : "近日点で降下 → 脱出軌道", el.Bound ? Good : Warn, 14); y += 20;
                }
            }
            var (txt, col) = fc.End switch
            {
                PredictionEnd.Stall => ($"{fc.Duration:F0}日後に失速", Warn),
                PredictionEnd.Collision => ($"{fc.Duration:F1}日後に {g.Stars[fc.CollisionStar!.Value].Name} へ突入", Bad),
                PredictionEnd.Lost => ("星域から離脱", Warn),
                PredictionEnd.Captured => ($"{g.Stars[fc.Encounters[^1].Star].Name} の井戸に捕まる。降下するか噴射で抜ける", Warn),
                _ => ("", Ink),
            };
            if (txt != "") Text(new Vector2(x, y), txt, col, 16);
            if (Game.DescentForecast is { } df && Game.Screen == Screen.Flight)
            {
                y += 22;
                var el = df.After;
                Text(new Vector2(x, y), el.Bound ? $"今降下すると {el.Periapsis:F2}-{el.Apoapsis:F2} AU" : "今降下すると 脱出軌道", el.Bound ? Good : Warn, 15);
            }
        }

        DrawFtlState();
        DrawRangeGauge();
        DrawCompass();
        if (!Game.MapOpen) DrawContacts();
        DrawClock();
    }

    /// <summary>Whether the ship is in FTL at all, then how close it is to falling out.</summary>
    void DrawFtlState()
    {
        var v = Game.V!;
        var planning = Game.Screen == Screen.Plan;
        var inFtlWell = v.Phase != Phase.Sublight && v.NearestWell(out _) < double.MaxValue;
        var from = v.Phase == Phase.Sublight ? v.JumpState.Velocity : v.Velocity;
        var speed = planning ? (from + Game.PlanDir * Game.PlanDv).Length : from.Length;
        var well = v.NearestWell(out var nearStar);
        var captureLimit = inFtlWell && nearStar != null ? Physics.CaptureSpeed(nearStar, well) : (double?)null;
        var (label, col) = ShipState();
        var size = 24;
        var w = F.GetStringSize(label, HorizontalAlignment.Left, -1, size).X + 40;
        var box = new Rect2((Size.X - w) / 2, 14, w, 40);
        DrawRect(box, new Color(0, 0, 0, 0.55f));
        DrawRect(box, col, false, 2);
        Text(box.Position + new Vector2(20, 29), label, col, size);

        if (Game.Screen == Screen.Orbit) return;

        // Speed against the stall threshold.
        const float width = 340, top = 66, maxSpeed = 1.2f;
        var x0 = (Size.X - width) / 2;
        DrawRect(new Rect2(x0, top, width, 12), new Color(1, 1, 1, 0.1f));
        var fill = Mathf.Clamp((float)speed / maxSpeed, 0, 1);
        DrawRect(new Rect2(x0, top, width * fill, 12), speed < Tuning.StallSpeed ? Bad : col);
        var tick = x0 + width * (float)Tuning.StallSpeed / maxSpeed;
        DrawLine(new Vector2(tick, top - 4), new Vector2(tick, top + 16), Bad, 2);
        Text(new Vector2(tick - 26, top + 30), "失速", Bad, 13);
        Text(new Vector2(x0 + width + 10, top + 12), $"{speed:F2} ly/日", Ink, 15);
        if (captureLimit is { } limit)
        {
            var mark = x0 + width * Mathf.Clamp((float)limit / maxSpeed, 0, 1);
            DrawLine(new Vector2(mark, top - 4), new Vector2(mark, top + 16), Good, 2);
            Text(new Vector2(mark + 5, top + 30), $"捕獲 {limit:F2}", Good, 13);
            if (!planning) Text(new Vector2(x0 + width + 10, top + 32), speed <= limit ? "今降下すれば軌道に乗る" : "機首を逆行へ向けて噴射", Good, 15);
        }
    }

    /// <summary>How far the current trajectory goes, with everything it meets marked along the way.</summary>
    void DrawRangeGauge()
    {
        var v = Game.V!; var g = Game.G!; var fc = Game.Forecast;
        // Clear of the attitude buttons, throttle and dial on the right.
        var room = Size.X - 460 - 40;
        var width = Mathf.Min(room, 900f);
        var x0 = 40 + (room - width) / 2;
        var top = Size.Y - ConsoleHeight - 112;
        var direct = (v.ChartPositions[v.TargetStar] - v.EstimatedPosition).Length;

        double total = 0;
        var cum = new List<(double time, double dist)>();
        if (fc != null)
            for (int i = 0; i < fc.Track.Count; i++)
            {
                if (i > 0) total += (fc.Track[i].Position - fc.Track[i - 1].Position).Length;
                cum.Add((fc.Track[i].Time, total));
            }
        var scale = Math.Max(Math.Max(total, direct) * 1.05, 1);
        float Px(double d) => x0 + width * (float)(d / scale);
        double DistAt(double t) { foreach (var c in cum) if (c.time >= t) return c.dist; return total; }

        var endCol = fc?.End switch { PredictionEnd.Stall => Warn, PredictionEnd.Collision => Bad, _ => Ink };
        DrawRect(new Rect2(x0, top, width, 12), new Color(1, 1, 1, 0.1f));
        DrawRect(new Rect2(x0, top, Px(total) - x0, 12), new Color(endCol, 0.75f));

        var targetHit = false;
        if (fc != null)
        {
            int n = 0;
            foreach (var e in fc.Encounters)
            {
                var px = Px(DistAt(e.Time));
                var isT = e.Star == v.TargetStar;
                targetHit |= isT;
                var col = isT ? Good : Ink;
                DrawLine(new Vector2(px, top - 6), new Vector2(px, top + 18), col, 2);
                Text(new Vector2(px + 4, top - (n++ % 2 == 0 ? 10 : 26)), g.Stars[e.Star].Name, col, 13);
            }
        }
        if (!targetHit)
        {
            // Not on this trajectory: show the straight-line distance so the shortfall is visible.
            var px = Px(direct);
            DrawDashedLine(new Vector2(px, top - 14), new Vector2(px, top + 18), Good, 2, 3);
            Text(new Vector2(px + 4, top + 46), $"{g.Stars[v.TargetStar].Name} 直線 {direct:F1} ly", Good, 13);
        }

        if (fc == null) { Text(new Vector2(x0, top + 28), "航程 なし", Dim, 15); return; }
        var ending = fc.End switch
        {
            PredictionEnd.Stall => "で失速",
            PredictionEnd.Collision => "で恒星に突入",
            PredictionEnd.Lost => "で星域を離脱",
            PredictionEnd.Captured => "で井戸に捕まる",
            _ => "以上",
        };
        var line = $"航程 {total:F1} ly / {fc.Duration:F0} 日{ending}";
        if (targetHit)
        {
            var need = Game.CaptureDv(fc.Encounters.First(e => e.Star == v.TargetStar));
            var have = v.DeltaVRemaining - (Game.Screen == Screen.Plan ? Game.PlanDv : 0);
            line += $"   捕獲に Δv {need:F2} / 残 {Math.Max(have, 0):F2}";
            Text(new Vector2(x0, top + 28), line, need > have ? Bad : Good, 15);
        }
        else Text(new Vector2(x0, top + 28), line, endCol, 15);
    }

    void Bar(float x, ref float y, string label, double frac, Color c, string value)
    {
        Text(new Vector2(x, y + 11), label, Dim, 14);
        var o = new Vector2(x + 44, y);
        DrawRect(new Rect2(o, 180, 11), new Color(1, 1, 1, 0.1f));
        DrawRect(new Rect2(o, 180 * (float)Math.Clamp(frac, 0, 1), 11), c);
        Text(new Vector2(o.X + 188, y + 11), value, Dim, 14);
        y += 21;
    }
}

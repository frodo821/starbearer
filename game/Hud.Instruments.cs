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
    const float NavLeft = 10, NavWidth = 380, ShipWidth = 340;
    float NavRight => NavLeft + NavWidth;
    float NavTop => 8;
    /// <summary>Panels sized to what they held last frame: drawn behind, their contents decide how tall.</summary>
    float _navHeight = 300, _shipHeight = 260;

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

    /// <summary>The ship, as in Elite's right panel: where the cargo is bound, the bars, what is fitted.</summary>
    void DrawShipPanel()
    {
        var v = Game.V!; var g = Game.G!;
        var left = Size.X - ShipWidth - 10;
        Panel(new Rect2(left, 8, ShipWidth, _shipHeight), "");
        float x = left + PanelPad, y = 34;
        Text(new Vector2(x, y), (v.Hold.Count == 0 ? "空荷" : $"積荷 {v.Hold.Count} 件") + $"  →  {g.Stars[v.TargetStar].Name}", Good, 18);
        var manifest = new Command(Key.I, "I", "積荷");
        Button(new Vector2(left + ShipWidth - PanelPad - ButtonWidth(manifest), 14), manifest);
        y += 22;
        Text(new Vector2(x, y), $"質量 {v.Mass:F2}   積荷 {v.CargoMass:F1}   容積 {Missions.Cubic(Missions.VolumeUsed(v.Hold))}/{Missions.Cubic(v.Class.Capacity)}", Dim, 13); y += 18;
        if (v.Hold.Count > 0) { Text(new Vector2(x, y), $"耐熱 {v.HeatTolerance:F2}", Dim, 13); y += 18; }
        foreach (var c in v.Hold.Take(3)) { Text(new Vector2(x, y), c.IsJob ? $"{c.Mission.Cargo.Name} → {g.StarAt(c.Mission.Target).Name}" : $"{c.Mission.Cargo.Name} {c.Mission.Cargo.Volume:F0} 単位（自分の荷）", c.IsJob && g.StarOf(c.Mission.Target) == v.TargetStar ? Ink : Dim, 13); y += 18; }
        y += 6;
        Bar(x, ref y, "燃料", v.Fuel / v.Class.Tank, Ink, Instruments.WellDeltaV(v.DeltaVRemaining));
        if (Game.Screen == Screen.Plan)
        {
            // Ghost of what the kick would spend.
            var after = Math.Max(0, v.Fuel - v.FuelFor(Game.PlanDv)) / v.Class.Tank;
            var now = v.Fuel / v.Class.Tank;
            DrawRect(new Rect2(x + 44 + 180 * (float)after, y - 21, 180 * (float)(now - after), 11), Warn);
        }
        Bar(x, ref y, "推進剤", v.ReactionMass / v.Class.ReactionTank, v.SublightDeltaV < 0.006 ? Warn : Ink, Instruments.Speed(v.SublightDeltaV));
        Bar(x, ref y, "船体", v.Hull, v.Hull < 0.4 ? Bad : Ink, $"{v.Hull:P0}");
        Bar(x, ref y, "積荷", v.CargoIntegrity, v.CargoIntegrity < 0.6 ? Warn : Ink, $"{v.CargoIntegrity:P0}");
        Bar(x, ref y, "熱", Math.Min(v.Heat, 1.5) / 1.5, v.Heat > 1 ? Bad : v.Heat > v.HeatTolerance ? Warn : Ink, $"{v.Heat:F2}");
        Bar(x, ref y, "生命", v.LifeRemaining / Tuning.LifeSupportDays, Ink, v.Docked ? $"{v.LifeRemaining:F0}日 停泊中" : $"{v.LifeRemaining:F0}日");
        y += 8;
        Text(new Vector2(x, y), $"推力 {Game.EngineAcceleration:F2} /日", Ink, 14); y += 20;
        Text(new Vector2(x, y), $"計算機 {(Game.Owns(Modules.Transfer) ? "搭載" : "なし")}   自動噴射 {(Game.Owns(Modules.AutoBurn) ? (v.AutoBurn ? "ON" : "OFF") : "なし")}", Dim, 13); y += 8;
        _shipHeight = y + PanelPad - 8;
    }

    /// <summary>Navigation down the left, as in Elite's left panel: in orbit the orbit, the target and the plan; in FTL the forecast and who is near.</summary>
    void DrawNavPanel()
    {
        var v = Game.V!; var g = Game.G!;
        Panel(new Rect2(NavLeft, NavTop, NavWidth, _navHeight), "");
        float x = NavLeft + PanelPad, y = NavTop + 4;
        var width = NavWidth - 2 * PanelPad;
        if (Game.Screen == Screen.Orbit) { DrawOrbitNav(x, ref y, width); _navHeight = y - NavTop + PanelPad; return; }

        Section(x, ref y, width, "航法");
        void Line(string s, Color c, int size = 15) { Text(new Vector2(x, y + 14), s, c, size); y += 20; }
        Line($"速度 {(v.Phase == Phase.Sublight ? Instruments.Speed(v.SubVelocity.Length) : $"{v.Velocity.Length:F3} ly/日")}   位置誤差 ±{v.NavSigma:F3} ly", v.NavSigma > 0.1 ? Warn : Ink);
        if (Game.Screen == Screen.Flight && v.Phase != Phase.Sublight)
        {
            var speed = v.Velocity.Length;
            Line($"失速線 {Tuning.StallSpeed:F2}   余裕 {speed - Tuning.StallSpeed:+0.00;-0.00} ly/日", speed < Tuning.StallSpeed * 1.3 ? Bad : Dim, 14);
            var well = v.NearestWell(out var near);
            if (near != null)
            {
                var capture = Physics.CaptureSpeed(near, well);
                Line($"捕獲速度 {capture:F2}  " + (speed <= capture ? "今降下すれば軌道に乗る" : "速すぎる: 機首を逆行へ噴射"), speed <= capture ? Good : Warn, 14);
            }
        }
        if (Game.Screen == Screen.Plan)
        {
            Line($"キック Δv {Instruments.WellDeltaV(Game.PlanDv)}  (燃料 {v.FuelFor(Game.PlanDv):F2})", Warn, 17);
            if (v.Phase == Phase.Sublight)
            {
                var gain = v.SubVelocity.Dot(Game.PlanDir.Normalized());
                Line($"軌道速度の寄与 {(gain >= 0 ? "+" : "-")}{Instruments.Speed(Math.Abs(gain))}  {(gain >= 0 ? "追い風" : "向かい風")}", gain >= 0 ? Good : Warn);
            }
        }
        if (Game.Screen == Screen.Flight) { y += 4; ButtonRow(x, y, Game.FlightNavCommands()); y += ButtonHeight + 4; }

        if (Game.Forecast is { } fc)
        {
            Section(x, ref y, width, "予測");
            foreach (var e in fc.Encounters.Take(5))
            {
                var isT = e.Star == v.TargetStar;
                var line = $"{Instruments.Span(e.Time)}  {g.Stars[e.Star].Name}  近日 {e.Periapsis:F2}";
                if (isT) line += $"  減速 Δv {Instruments.WellDeltaV(Game.CaptureDv(e))}";
                Line(line, isT ? Good : Ink, 14);
                if (isT)
                {
                    var el = Sublight.DescentOrbit(g.Stars[e.Star], e.Periapsis, e.PeriapsisSpeed);
                    Line(el.Bound ? $"  近日点で降下 → {Instruments.Distance(el.Periapsis)} - {Instruments.Distance(el.Apoapsis)}" : "  近日点で降下 → 脱出軌道", el.Bound ? Good : Warn, 13);
                }
            }
            var (txt, col) = fc.End switch
            {
                PredictionEnd.Stall => ($"{Instruments.Span(fc.Duration)} 後に失速", Warn),
                PredictionEnd.Collision => ($"{Instruments.Span(fc.Duration)} 後に {g.Stars[fc.CollisionStar!.Value].Name} へ突入", Bad),
                PredictionEnd.Lost => ("星域から離脱", Warn),
                PredictionEnd.Captured => ($"{g.Stars[fc.Encounters[^1].Star].Name} の井戸に捕まる。降下か噴射で抜ける", Warn),
                _ => ("", Ink),
            };
            if (txt != "") Line(txt, col);
            if (Game.DescentForecast is { } df && Game.Screen == Screen.Flight)
                Line(df.After.Bound ? $"今降下すると {Instruments.Distance(df.After.Periapsis)} - {Instruments.Distance(df.After.Apoapsis)}" : "今降下すると 脱出軌道", df.After.Bound ? Good : Warn);
        }
        Section(x, ref y, width, "周囲の天体");
        DrawContacts(x, ref y);
        _navHeight = y - NavTop + PanelPad;
    }

    void DrawInstruments()
    {
        DrawNavPanel();
        DrawShipPanel();
        // The top: the advice, then in FTL only its clock and the heat warnings; planning a jump, the speed and range it needs.
        DrawAdvice();
        if (Game.Screen == Screen.Flight) DrawFtlClock();
        if (Game.Screen == Screen.Plan) { DrawSpeedBar(); DrawRangeGauge(); }
        DrawHelm();
    }

    /// <summary>
    /// The top of the screen in FTL: when the next star's periapsis comes and when the drive stalls, and
    /// under them, only when they apply, the heat warnings. The rest (speed against the stall and
    /// capture lines) is in the navigation panel.
    /// </summary>
    void DrawFtlClock()
    {
        var v = Game.V!; var g = Game.G!;
        var parts = new List<(string Text, Color Colour)>();
        if (Game.Forecast is { } fc)
        {
            if (fc.Encounters.Count > 0)
            {
                var e = fc.Encounters[0];
                parts.Add(($"{g.Stars[e.Star].Name} 近日点まで {Instruments.Countdown(e.Time)}", e.Star == v.TargetStar ? Good : Ink));
            }
            if (fc.End == PredictionEnd.Stall) parts.Add(($"失速まで {Instruments.Countdown(fc.Duration)}", fc.Duration < 10 ? Bad : Warn));
            if (fc.End == PredictionEnd.Collision) parts.Add(($"突入まで {Instruments.Countdown(fc.Duration)}", Bad));
        }
        if (parts.Count == 0) parts.Add(("予定なし", Dim));
        const int size = 20;
        var sep = "     ";
        var width = parts.Sum(p => F.GetStringSize(p.Text, HorizontalAlignment.Left, -1, size).X) + (parts.Count - 1) * F.GetStringSize(sep, HorizontalAlignment.Left, -1, size).X;
        var x = Size.X / 2 - width / 2;
        var top = AdviceBottom + 6;
        DrawRect(new Rect2(x - 16, top, width + 32, 36), new Color(0, 0, 0, 0.55f));
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) { Text(new Vector2(x, top + 26), sep, Dim, size); x += F.GetStringSize(sep, HorizontalAlignment.Left, -1, size).X; }
            Text(new Vector2(x, top + 26), parts[i].Text, parts[i].Colour, size);
            x += F.GetStringSize(parts[i].Text, HorizontalAlignment.Left, -1, size).X;
        }
        // Heat, only when it matters: out of the shield deep in a well, or the hull or cargo heating past what they take.
        var y = top + 60;
        var r = v.NearestWell(out var star);
        if (star != null && r < star.Soi * 0.7 && !Game.Shielded) { Centered(y, "シールド外! 機首を恒星へ (姿勢儀の「恒」をタップ)", Bad, 20); y += 26; }
        if (v.Heat > 1) Centered(y, $"熱 {v.Heat:F2}: 船体が傷んでいる", Bad, 18);
        else if (v.Hold.Count > 0 && v.Heat > v.HeatTolerance) Centered(y, $"熱 {v.Heat:F2}: 積荷の耐熱 {v.HeatTolerance:F2} を超えた", Warn, 18);
    }

    /// <summary>Planning a jump: the speed it leaves at against the stall line, and the capture line in a well.</summary>
    void DrawSpeedBar()
    {
        var v = Game.V!;
        var inFtlWell = v.Phase != Phase.Sublight && v.NearestWell(out _) < double.MaxValue;
        var from = v.Phase == Phase.Sublight ? v.JumpState.Velocity : v.Velocity;
        var speed = (from + Game.PlanDir * Game.PlanDv).Length;
        var well = v.NearestWell(out var nearStar);
        var captureLimit = inFtlWell && nearStar != null ? Physics.CaptureSpeed(nearStar, well) : (double?)null;
        var col = StateColour();

        // Speed against the stall threshold.
        const float width = 340, maxSpeed = 1.2f;
        const float top = AdviceBottom + 12;
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
        }
    }

    /// <summary>How far the current trajectory goes, with everything it meets marked along the way.</summary>
    void DrawRangeGauge()
    {
        var v = Game.V!; var g = Game.G!; var fc = Game.Forecast;
        // Up top under the speed bar, between the side panels: the helm has the bottom.
        var room = Size.X - 2 * (NavRight + 30);
        var width = Mathf.Min(room, 760f);
        var x0 = (Size.X - width) / 2;
        var top = 205f;
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
        var line = $"航程 {total:F1} ly / {Instruments.Span(fc.Duration)} {ending}";
        if (targetHit)
        {
            var need = Game.CaptureDv(fc.Encounters.First(e => e.Star == v.TargetStar));
            var have = v.DeltaVRemaining - (Game.Screen == Screen.Plan ? Game.PlanDv : 0);
            line += $"   捕獲に Δv {Instruments.WellDeltaV(need)} / 残 {Instruments.WellDeltaV(Math.Max(have, 0))}";
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

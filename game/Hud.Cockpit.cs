using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>The cockpit: markers on the sky, the attitude dial with its hold buttons, and the throttle.</summary>
public partial class Hud
{
    void DrawCockpit()
    {
        var g = Game.G!; var v = Game.V!;
        var ship = v.EstimatedPosition;

        // Chart labels: the planner's idea of where stars are, offset from what the eye sees.
        for (int i = 0; i < g.Stars.Length; i++)
        {
            var to = v.ChartPositions[i] - ship;
            if (!Project(Sky.ToV(to), out var p)) continue;
            var isTarget = i == v.TargetStar;
            var near = to.Length < 14;
            if (!isTarget && !near) continue;
            var col = isTarget ? Good : Dim;
            DrawArc(p, isTarget ? 13 : 8, 0, Mathf.Tau, 24, col, 1f, true);
            Text(p + new Vector2(14, 5), $"{g.Stars[i].Name}  {to.Length:F1}ly", col, 14);
        }

        DrawForecast(ship);

        if (Game.Screen == Screen.Plan && Project(Sky.ToV(Game.PlanDir), out var aim))
        {
            DrawArc(aim, 10, 0, Mathf.Tau, 24, Warn, 1.5f, true);
            DrawLine(aim + new Vector2(-18, 0), aim + new Vector2(18, 0), Warn, 1);
            DrawLine(aim + new Vector2(0, -18), aim + new Vector2(0, 18), Warn, 1);
        }
        if (Game.Screen == Screen.Flight && v.Velocity.Length > 0 && Project(Sky.ToV(v.Velocity), out var pro))
        {
            DrawArc(pro, 7, 0, Mathf.Tau, 16, Ink, 1.5f, true);
            DrawCircle(pro, 1.5f, Ink);
        }

        if (Game.Screen != Screen.Plan)
        {
            if (Project(Sky.ToV(Game.Helm.Nose), out var nose))
            {
                DrawLine(nose + new Vector2(-20, 0), nose + new Vector2(-7, 0), Warn, 2);
                DrawLine(nose + new Vector2(20, 0), nose + new Vector2(7, 0), Warn, 2);
                DrawLine(nose + new Vector2(0, -7), nose + new Vector2(0, -16), Warn, 2);
                DrawArc(nose, 3, 0, Mathf.Tau, 10, Warn, 1.5f, true);
            }
            DrawEdgeArrow(Game.Helm.Nose, "機首", Warn);
        }
        DrawEdgeArrow((v.ChartPositions[v.TargetStar] - ship).Normalized(), "目的地", Good);
        if (v.Velocity.Length > 1e-6) DrawEdgeArrow(v.Velocity.Normalized(), "順行", new Color(0.45f, 1f, 0.6f));
        if (Game.Forecast is { Encounters.Count: > 0 } soon && soon.Encounters[0].Star != v.TargetStar)
            DrawEdgeArrow((v.ChartPositions[soon.Encounters[0].Star] - ship).Normalized(), "次の星", Ink);

    }

    (Vec3d Dir, string Label, Color Color, bool Primary, ViewHold Hold)[] Markers()
    {
        var v = Game.V!; var list = new List<(Vec3d, string, Color, bool, ViewHold)>();
        var green = new Color(0.45f, 1f, 0.6f);
        var blue = new Color(0.45f, 0.8f, 1f);
        var magenta = new Color(1f, 0.5f, 0.9f);
        void Add(ViewHold h, string label, Color c, bool primary)
        {
            if (Game.DirectionOf(h) is { } d) list.Add((d, label, c, primary, h));
        }
        Add(ViewHold.Prograde, "順", green, true); Add(ViewHold.Retrograde, "逆", green, false);
        Add(ViewHold.RadialOut, "外", blue, false); Add(ViewHold.RadialIn, "内", blue, false);
        Add(ViewHold.Normal, "法", magenta, false); Add(ViewHold.AntiNormal, "反", magenta, false);
        Add(ViewHold.Star, "恒", new Color(1f, 0.85f, 0.4f), true);
        Add(ViewHold.Target, "目", Good, true);
        Add(ViewHold.Node, "噴", Warn, true);
        if (v.Phase != Phase.Sublight && Game.Forecast is { Encounters.Count: > 0 } fc && fc.Encounters[0].Star != v.TargetStar)
            list.Add(((v.ChartPositions[fc.Encounters[0].Star] - v.EstimatedPosition).Normalized(), "次", Ink, true, ViewHold.Free));
        if (Game.Screen == Screen.Plan) list.Add((Game.PlanDir.Normalized(), "照", Warn, true, ViewHold.Free));
        var cf = Game.CameraForward();
        list.Add((cf, "視", new Color(1, 1, 1, 0.6f), false, ViewHold.Free));
        return list.ToArray();
    }

    public int MarkerCount => Markers().Length;

    bool Project(Vector3 dir, out Vector2 p)
    {
        p = default;
        var world = dir.Normalized() * Sky.Radius;
        if (Game.Camera.IsPositionBehind(world)) return false;
        p = Game.Camera.UnprojectPosition(world);
        return true;
    }

    /// <summary>Who is around you, and whether you are heading for them.</summary>
    void DrawContacts(float x, ref float y)
    {
        var v = Game.V!;
        Text(new Vector2(x, y + 12), "名前           距離   進路差   接近   到達", Dim, 12);
        y += 22;
        foreach (var c in Instruments.Nearby(v.G, v.ChartPositions, v.EstimatedPosition, Game.CurrentVelocity, 5))
        {
            var target = c.Star.Id == v.TargetStar;
            var eta = c.Eta is { } e && e < 999 ? $"{e:F0}日" : "—";
            var col = target ? Good : c.OffPrograde < 12 ? Warn : Ink;
            Text(new Vector2(x, y + 14), c.Star.Name, col, 14);
            Text(new Vector2(x + 104, y + 14), $"{c.Distance,5:F1}ly", col, 14);
            Text(new Vector2(x + 166, y + 14), $"{c.OffPrograde,3:F0}°", col, 14);
            Text(new Vector2(x + 212, y + 14), $"{c.ClosingSpeed:+0.00;-0.00}", col, 14);
            Text(new Vector2(x + 272, y + 14), eta, col, 14);
            y += 20;
        }
    }

    /// <summary>Arrow at the screen edge for anything important that has left the view.</summary>
    void DrawEdgeArrow(Vec3d dir, string label, Color col)
    {
        var world = Sky.ToV(dir);
        var cam = Game.Camera;
        var centre = Size / 2;
        var behind = cam.IsPositionBehind(world * Sky.Radius);
        var screen = behind ? centre + (centre - cam.UnprojectPosition(-world * Sky.Radius)) : cam.UnprojectPosition(world * Sky.Radius);
        if (!behind && new Rect2(Vector2.Zero, Size).Grow(-30).HasPoint(screen)) return;
        var d = (screen - centre).Normalized();
        if (d == Vector2.Zero) d = Vector2.Up;
        var scale = Mathf.Min((Size.X / 2 - 380) / Mathf.Max(Mathf.Abs(d.X), 0.001f), (Size.Y / 2 - ConsoleHeight - 30) / Mathf.Max(Mathf.Abs(d.Y), 0.001f));   // stays clear of the side panels and the console
        var p = centre + d * scale;
        var side = new Vector2(-d.Y, d.X);
        DrawColoredPolygon(new[] { p + d * 12, p - d * 6 + side * 8, p - d * 6 - side * 8 }, col);
        Text(p - d * 26 + new Vector2(-14, 5), label, col, 13);
    }

    void DrawForecast(Vec3d ship)
    {
        var fc = Game.Forecast; if (fc == null) return;
        var v = Game.V!;
        Vector2? last = null;
        foreach (var pt in fc.Track)
        {
            var to = pt.Position - ship;
            if (to.Length < 0.05) continue;
            if (!Project(Sky.ToV(to), out var p)) { last = null; continue; }
            if (last is { } l && l.DistanceTo(p) < 300)
                DrawLine(l, p, new Color(1f, 0.85f, 0.4f, 0.75f), 1.5f, true);
            last = p;
        }
        // The cone of uncertainty, seen from the ship, closes as an angle around each encounter.
        foreach (var e in fc.Encounters.Take(3))
        {
            var to = e.Point - ship;
            if (!Project(Sky.ToV(to), out var p)) continue;
            var sigma = Game.SigmaAt(e.Time) + Tuning.ChartErrorLy * Voyage.ChartFactor(v.ClosestApproach[e.Star]);
            var angle = Math.Atan2(sigma, Math.Max(to.Length, 0.1));
            var px = (float)(angle / Mathf.DegToRad(Game.Camera.Fov) * Size.Y);
            DrawArc(p, Mathf.Clamp(px, 3, 260), 0, Mathf.Tau, 40, new Color(1f, 0.6f, 0.3f, 0.6f), 1f, true);
        }
    }

    const float CompassRadius = 104, HoldW = 58, HoldH = 26, HoldGap = 4, BurnWidth = 290;
    /// <summary>Room between the helm's rows and the bottom band.</summary>
    const float HelmGap = 10;

    /// <summary>
    /// Everything a burn is flown with sits in one place at the bottom middle, as around KSP's navball:
    /// the dial in the centre, the attitude holds either side, the throttle to the left and the burn's
    /// numbers to the right, time and the clock over it, the buttons that fly the ship under it.
    /// </summary>
    Vector2 CompassCentre => new(Size.X / 2, HelmRowsTop - 44 - CompassRadius);
    float HelmRowsTop => Size.Y - ConsoleHeight - HelmGap - 2 * ButtonHeight - ButtonGap;
    /// <summary>Where the time controls' row sits, and the clock line over it.</summary>
    float TimeRowTop => CompassCentre.Y - CompassRadius - 22 - ButtonHeight;
    /// <summary>The top of the helm's cluster: the chart and the node panel keep above it.</summary>
    float ClusterTop => TimeRowTop - 26;
    float HoldsLeft => CompassCentre.X - CompassRadius - 14 - HoldW;
    float HoldsRight => CompassCentre.X + CompassRadius + 14;
    float BurnLeft => HoldsRight + HoldW + 16;

    /// <summary>The flight controls and their readouts, gathered round the dial. Docked or planning, nothing to fly.</summary>
    void DrawHelm()
    {
        var v = Game.V!;
        DrawTimeRow();
        DrawCompass();
        if (Game.Screen == Screen.Plan || v.Docked) return;
        var (thrust, nose) = Game.HelmCommands();
        ButtonRow(Size.X / 2 - RowWidth(thrust) / 2, HelmRowsTop, thrust);
        ButtonRow(Size.X / 2 - RowWidth(nose) / 2, HelmRowsTop + ButtonHeight + ButtonGap, nose);
        if (Game.Screen == Screen.Orbit) DrawBurnPanel();
    }

    /// <summary>Over the dial: the clocks and what comes next, then stop and go, the rates, and on to the burn.</summary>
    void DrawTimeRow()
    {
        var v = Game.V!;
        var commands = Game.TimeCommands();
        var x = Size.X / 2 - RowWidth(commands) / 2;
        var y = TimeRowTop;
        var shown = Game.RateShown;
        foreach (var c in commands)
        {
            var current = c.Key == Key.Key1 + shown && !Game.Paused && Game.Screen != Screen.Plan;
            if (current) DrawRect(new Rect2(x, y, ButtonWidth(c), ButtonHeight), new Color(Ink, 0.25f));
            x += Button(new Vector2(x, y), c) + ButtonGap;
        }
        // One line over the buttons, centred: the voyage clock, the countdown to what comes next, and when that is.
        var parts = new List<(string Text, Color Colour, int Size)> { ("航海 ", Dim, 12), (Instruments.Clock(v.Time), Ink, 15) };
        var (label, left) = NextEvent();
        var running = Game.Screen != Screen.Plan && Game.CurrentRate > 0;
        if (left is { } t)
        {
            parts.Add(("    " + Instruments.Countdown(t), t < 1 ? Warn : Good, 15));
            parts.Add(("  " + label, Dim, 13));
            parts.Add(("  (" + Instruments.Date(v.AbsoluteTime + t) + ")", Dim, 12));
        }
        else parts.Add(("    " + (running ? Instruments.Date(v.AbsoluteTime) : "時間停止  " + Instruments.Date(v.AbsoluteTime)), running ? Dim : Warn, 13));
        var width = parts.Sum(p => F.GetStringSize(p.Text, HorizontalAlignment.Left, -1, p.Size).X);
        var px = Size.X / 2 - width / 2;
        var back = new Rect2(px - 10, y - 26, width + 20, 22);
        DrawRect(back, PanelFill);
        foreach (var (text, col, size) in parts)
        {
            Text(new Vector2(px, y - 9), text, col, size);
            px += F.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        }
    }

    /// <summary>A span of real time as the pilot reads it.</summary>
    static string RealTime(double seconds) => Instruments.Span(seconds / Instruments.SecondsPerDay);

    /// <summary>
    /// Right of the dial, what the next node asks of the pilot: what is left of it and the throttle;
    /// how long until the best moment to light (half the burn before the node) and how much burn is
    /// left, each in game time and real time; and whether the nose is on it. Real time to ignition is at
    /// the rate time runs now; the burn's is at the rate a burn runs at.
    /// </summary>
    void DrawBurnPanel()
    {
        if (Game.NextNode is not { } burn || Game.IgnitionIn is not { } wait) return;
        const float rowH = 20;
        var rows = 5 + (Game.NoseOffNode != null ? 1 : 0);
        var height = 24 + rows * rowH + 6;
        var box = new Rect2(BurnLeft, CompassCentre.Y - height / 2, BurnWidth, height);
        Panel(box, "噴射", opaque: true);
        float x = box.Position.X + PanelPad, c1 = x + 64, c2 = x + 172, y = box.Position.Y + 24 + 14;
        Text(new Vector2(x, y), "ノード残", Dim, 13);
        Text(new Vector2(c1, y), $"Δv {Instruments.Speed(burn.DeltaV)}" + (Game.NodeOvershot ? "  過ぎた" : ""), Game.NodeOvershot ? Warn : Ink, 14);
        y += rowH;
        Text(new Vector2(x, y), "推力", Dim, 13);
        Text(new Vector2(c1, y), Game.EngineAcceleration > 0 ? $"{Game.Helm.Throttle * 100:F0}%" : "0%  噴射できない (W で上げる)", Game.EngineAcceleration > 0 ? Ink : Warn, 14);
        y += rowH;
        Text(new Vector2(c1, y), "ゲーム内", Dim, 11);
        Text(new Vector2(c2, y), "実時間", Dim, 11);
        y += rowH;
        // Until ignition.
        var rate = Game.CurrentRate;
        var lit = Game.Helm.EngineOn;
        Text(new Vector2(x, y), "点火まで", Dim, 13);
        if (lit) Text(new Vector2(c1, y), "噴射中", Warn, 14);
        else
        {
            var col = wait < 0.3 ? Warn : Good;
            Text(new Vector2(c1, y), wait <= 0 ? "今" : Instruments.Span(wait), col, 14);
            Text(new Vector2(c2, y), wait <= 0 ? "今" : rate > 0 ? RealTime(wait / rate) : "時間停止", rate > 0 || wait <= 0 ? col : Dim, 14);
        }
        y += rowH;
        // The burn left.
        Text(new Vector2(x, y), "噴射残", Dim, 13);
        if (Game.EngineAcceleration > 0)
        {
            var days = burn.DeltaV / Game.EngineAcceleration;
            var col = days > 3 || lit ? Warn : Ink;
            Text(new Vector2(c1, y), Instruments.Span(days), col, 14);
            Text(new Vector2(c2, y), RealTime(days / Game.BurnRateNow), col, 14);
        }
        y += rowH;
        if (Game.NoseOffNode is { } off)
        {
            Text(new Vector2(x, y), "機首ずれ", Dim, 13);
            Text(new Vector2(c1, y), $"{off:F1}°" + (off > 5 ? "  「ノード」で保持" : ""), off > 5 ? Warn : Good, 14);
        }
    }

    Vector2 CompassPt(Vec3d d)
    {
        var (right, up) = Game.Helm.NoseFrame();
        var (x, y) = Instruments.CompassPoint(d, right, up, Game.Helm.Nose);
        return CompassCentre + new Vector2((float)x, -(float)y) * CompassRadius;
    }

    public Vector2 CompassMarkerForTest(ViewHold hold) => CompassPt(Game.DirectionOf(hold)!.Value);

    static readonly (ViewHold Hold, Color Colour)[] HoldChoices =
    {
        (ViewHold.Free, new(0.75f, 0.9f, 1f)), (ViewHold.Star, new(1f, 0.85f, 0.4f)),
        (ViewHold.Prograde, new(0.45f, 1f, 0.6f)), (ViewHold.Retrograde, new(0.45f, 1f, 0.6f)),
        (ViewHold.RadialOut, new(0.45f, 0.8f, 1f)), (ViewHold.RadialIn, new(0.45f, 0.8f, 1f)),
        (ViewHold.Normal, new(1f, 0.5f, 0.9f)), (ViewHold.AntiNormal, new(1f, 0.5f, 0.9f)),
        (ViewHold.Node, new(1f, 0.7f, 0.25f)), (ViewHold.Target, new(0.5f, 1f, 0.7f)),
    };

    /// <summary>The attitude holds as buttons either side of the dial: each pair's first on the left, its opposite on the right.</summary>
    IEnumerable<(Rect2 Box, ViewHold Hold, string Label, Color Colour, bool Enabled)> HoldButtons()
    {
        if (Game.Screen is not (Screen.Flight or Screen.Orbit)) yield break;
        var top = CompassCentre.Y - (5 * (HoldH + HoldGap) - HoldGap) / 2;
        for (int i = 0; i < HoldChoices.Length; i++)
        {
            var (hold, col) = HoldChoices[i];
            var box = new Rect2(i % 2 == 0 ? HoldsLeft : HoldsRight, top + i / 2 * (HoldH + HoldGap), HoldW, HoldH);
            yield return (box, hold, Helm.NameOf(hold), col, hold == ViewHold.Free || Game.DirectionOf(hold) != null);
        }
    }

    public Vector2 HoldButtonForTest(ViewHold hold) => HoldButtons().First(b => b.Hold == hold).Box.GetCenter();

    void DrawHoldButtons()
    {
        foreach (var (box, hold, label, col, enabled) in HoldButtons())
        {
            var on = hold == Game.Helm.Hold;
            DrawRect(box.Grow(2), new Color(0.01f, 0.02f, 0.05f, 0.82f));
            if (on) DrawRect(box, new Color(col, 0.3f));
            var ink = enabled ? col : new Color(Dim, 0.25f);
            DrawRect(box, ink, false, on ? 2 : 1);
            var tw = F.GetStringSize(label, HorizontalAlignment.Left, -1, 13).X;
            Text(new Vector2(box.GetCenter().X - tw / 2, box.Position.Y + 18), label, ink, 13);
        }
    }

    /// <summary>Tap a hold button or a marker to hold it; tap the middle of the dial to hand attitude back to the pilot.</summary>
    public bool CompassTap(Vector2 at)
    {
        foreach (var b in HoldButtons())
            if (b.Box.HasPoint(at))
            {
                if (b.Enabled) Game.SetHold(b.Hold);
                return true;
            }
        if (at.DistanceTo(CompassCentre) > CompassRadius + 8) return false;
        var hit = Markers().Where(m => m.Hold != ViewHold.Free && CompassPt(m.Dir).DistanceTo(at) < 13)
            .OrderBy(m => CompassPt(m.Dir).DistanceTo(at)).ToList();
        if (hit.Count > 0) Game.SetHold(hit[0].Hold);
        else if (at.DistanceTo(CompassCentre) < 14) Game.SetHold(ViewHold.Free);
        return true;
    }

    /// <summary>The all-sphere attitude display: nose at the centre, dead astern on the rim.</summary>
    void DrawCompass()
    {
        var v = Game.V!;
        var c = CompassCentre;
        var radius = CompassRadius;

        DrawCircle(c, radius + 6, new Color(0.01f, 0.02f, 0.05f, 0.82f));
        foreach (var f in new[] { 0.25f, 0.5f, 0.75f, 1f })
            DrawArc(c, radius * f, 0, Mathf.Tau, 48, new Color(1, 1, 1, f == 0.5f ? 0.35f : 0.13f), 1, true);
        DrawLine(c + new Vector2(-12, 0), c + new Vector2(-4, 0), Ink, 2);
        DrawLine(c + new Vector2(12, 0), c + new Vector2(4, 0), Ink, 2);
        DrawLine(c + new Vector2(0, -4), c + new Vector2(0, -12), Ink, 2);
        DrawCircle(c, 2, Ink);

        // Reference plane: the orbit in normal space, the galactic plane in FTL. Reads as a horizon.
        var normal = Game.MotionFrame() is { } mf && v.Phase == Phase.Sublight ? mf.N : new Vec3d(0, 1, 0);
        var e1 = normal.AnyPerpendicular(); var e2 = normal.Cross(e1);
        Vector2? last = null;
        for (int i = 0; i <= 72; i++)
        {
            var a = i * Math.Tau / 72;
            var p = CompassPt(e1 * Math.Cos(a) + e2 * Math.Sin(a));
            if (last is { } l && l.DistanceTo(p) < radius * 0.6f) DrawLine(l, p, new Color(0.5f, 0.8f, 1f, 0.4f), 1, true);
            last = p;
        }

        foreach (var (dir, label, col, primary, hold) in Markers())
        {
            var p = CompassPt(dir);
            if (primary) DrawCircle(p, 8, new Color(0, 0, 0, 0.6f));
            DrawArc(p, primary ? 8 : 6, 0, Mathf.Tau, 14, col, hold != ViewHold.Free && hold == Game.Helm.Hold ? 3f : primary ? 1.6f : 1f, true);
            Text(p + new Vector2(-6, 5), label, col, primary ? 12 : 11);
        }
        Text(c + new Vector2(-radius, -radius - 12), "姿勢儀  中心=機首  タップで姿勢保持", Dim, 12);
        Text(c + new Vector2(-radius, radius + 22), $"姿勢保持 {Helm.NameOf(Game.Helm.Hold)}", Game.Helm.Hold == ViewHold.Free ? Dim : Good, 13);
        DrawThrottle(c);
        DrawHoldButtons();
        Text(c + new Vector2(-radius, radius + 40), $"カメラ {(Game.CameraFollow ? "機首固定" : "自由")} (B)", Dim, 12);
    }

    void DrawThrottle(Vector2 compass)
    {
        const float height = 200, width = 16;
        var x = HoldsLeft - 36;
        var top = compass.Y - height / 2;
        DrawRect(new Rect2(x - 8, top - 30, width + 24, height + 70), new Color(0.01f, 0.02f, 0.05f, 0.72f));
        var col = Game.Helm.EngineOn ? Warn : Ink;
        DrawRect(new Rect2(x, top, width, height), new Color(1, 1, 1, 0.12f));
        var fill = height * (float)Game.Helm.Throttle;
        DrawRect(new Rect2(x, top + height - fill, width, fill), col);
        Text(new Vector2(x - 4, top - 10), $"{Game.Helm.Throttle * 100:F0}%", col, 14);
        Text(new Vector2(x - 6, top + height + 18), Game.Helm.EngineOn ? "点火" : "停止", Game.Helm.EngineOn ? Warn : Dim, 13);
        Text(new Vector2(x - 6, top + height + 34), "E", Dim, 11);
    }
}

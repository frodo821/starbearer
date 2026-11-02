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

        var r = v.NearestWell(out var star);
        if (star != null && Game.Screen == Screen.Flight)
        {
            var pass = Game.Forecast?.Encounters.FirstOrDefault(e => e.Star == star.Id);
            var periapsis = pass is { Star: var id } e0 && id == star.Id ? $"   近日点 {e0.Periapsis:F2}" : "";
            Centered(138, $"{star.Name}  距離 {r:F3} ly{periapsis}   時間 {Instruments.Rate(Game.CurrentRate)}", Warn, 22);
            if (r < star.Soi * 0.7)
                Centered(166, Game.Shielded ? "シールド: 恒星に正対" : "シールド外! 機首を恒星へ! (姿勢儀の「恒」をタップ)", Game.Shielded ? Good : Bad, 20);
        }
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
    void DrawContacts()
    {
        var v = Game.V!;
        var x = Size.X - 345f; var y = 22f;
        DrawRect(new Rect2(x - 12, 8, 345, 158), new Color(0.01f, 0.02f, 0.05f, 0.72f));
        var (pos, vel) = (v.EstimatedPosition, Game.CurrentVelocity);
        Text(new Vector2(x, y + 6), "周囲の天体   距離   進路差   接近   到達", Dim, 13);
        y += 28;
        foreach (var c in Instruments.Nearby(v.G, v.ChartPositions, pos, vel, 5))
        {
            var target = c.Star.Id == v.TargetStar;
            var eta = c.Eta is { } e && e < 999 ? $"{e:F0}日" : "—";
            var col = target ? Good : c.OffPrograde < 12 ? Warn : Ink;
            Text(new Vector2(x, y), c.Star.Name, col, 14);
            Text(new Vector2(x + 112, y), $"{c.Distance,5:F1}ly", col, 14);
            Text(new Vector2(x + 176, y), $"{c.OffPrograde,3:F0}°", col, 14);
            Text(new Vector2(x + 222, y), $"{c.ClosingSpeed:+0.00;-0.00}", col, 14);
            Text(new Vector2(x + 278, y), eta, col, 14);
            y += 22;
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

    const float CompassRadius = 112;
    Vector2 CompassCentre => new(Size.X - CompassRadius - 26, Size.Y - CompassRadius - 150);

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

    /// <summary>The attitude holds as buttons in two columns beside the dial, left of the throttle.</summary>
    IEnumerable<(Rect2 Box, ViewHold Hold, string Label, Color Colour, bool Enabled)> HoldButtons()
    {
        if (Game.Screen is not (Screen.Flight or Screen.Orbit)) yield break;
        const float w = 58, h = 26, gap = 4;
        var c = CompassCentre;
        var right = c.X - CompassRadius - 66;
        var top = c.Y - (5 * (h + gap) - gap) / 2;
        for (int i = 0; i < HoldChoices.Length; i++)
        {
            var (hold, col) = HoldChoices[i];
            var box = new Rect2(right - (2 - i % 2) * (w + gap), top + i / 2 * (h + gap), w, h);
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
        var x = compass.X - CompassRadius - 44;
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

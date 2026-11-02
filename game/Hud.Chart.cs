using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

public partial class Hud
{

    Vector2 MapPoint(Vec3d p) => Game.Chart.Project(p, Size);

    /// <summary>Stars, wells and the FTL path. Shared by the galaxy map and the zoomed-out orbit view.</summary>
    void DrawGalaxyLayer(int here = -1)
    {
        var g = Game.G!; var v = Game.V!;
        foreach (var n in Game.Chart.Zoom < 200 ? g.Nebulae : Array.Empty<Nebula>())
        {
            var c = MapPoint(n.Center);
            for (int i = 4; i >= 1; i--)
                DrawCircle(c, (float)n.Radius * Game.Chart.Zoom * i * 0.45f, new Color(0.35f, 0.5f, 0.8f, 0.018f * (float)n.Density));
        }
        var current = CurrentStar();
        for (int i = 0; i < g.Stars.Length; i++)
        {
            var s = g.Stars[i];
            var p = MapPoint(v.ChartPositions[i]);
            var col = Sky.StarColor(s.Temperature);
            if (i == here) continue;   // the orbit view draws its own star, at its true place
            DrawCircle(p, 2.5f + (float)s.Mass, col);
            // Chart ring: how far this star may be from where it is drawn.
            var err = (float)(Tuning.ChartErrorLy * Voyage.ChartFactor(v.ClosestApproach[i]) * Game.Chart.Zoom);
            if (err > 2) DrawArc(p, err * 2, 0, Mathf.Tau, 20, new Color(1f, 0.6f, 0.3f, 0.45f), 1f, true);
            var port = g.Ports.Contains(i);
            var depot = g.Depots.Contains(i);
            var lc = i == v.TargetStar ? Good : v.Hold.Any(c => c.Mission.Target == i) ? Warn : port || depot ? Ink : Dim;
            if (i == current) DrawHere(p, s.Name + (port ? " ◆" : depot ? " ◇" : ""), ring: true);
            else Text(p + new Vector2(8, 4), s.Name + (port ? " ◆" : depot ? " ◇" : ""), lc, 13);
        }
        var fc = Game.Forecast;
        // Up close, in orbit, the FTL line would only cross the orbit it starts from.
        var fade = here < 0 ? 1 : Mathf.Clamp(1.5f - Game.Chart.Zoom / 120, 0, 1);
        if (fc != null && fade > 0)
        {
            Vector2? last = null;
            foreach (var pt in fc.Track)
            {
                var p = MapPoint(pt.Position);
                var t = (float)Math.Clamp(pt.Speed / 0.8, 0, 1);
                if (last is { } l) DrawLine(l, p, new Color(1f, 0.45f + 0.45f * t, 0.3f, 0.9f * fade), 1.8f, true);
                last = p;
            }
        }
    }

    /// <summary>The system the ship is in: in normal space its star, in flight the well it is passing through; -1 between stars.</summary>
    int CurrentStar()
    {
        var v = Game.V!;
        return v.Phase == Phase.Sublight ? v.SystemStar : v.NearestWell(out var well) < double.MaxValue && well != null ? well.Id : -1;
    }

    /// <summary>The system the ship is in: its name in full on a dark backing, and a ring when it is only a point on the chart.</summary>
    void DrawHere(Vector2 at, string name, bool ring)
    {
        if (ring)
        {
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 300f);
            DrawArc(at, 12, 0, Mathf.Tau, 32, Colors.White, 2f, true);
            DrawArc(at, 17 + 4 * pulse, 0, Mathf.Tau, 32, new Color(Colors.White, 0.15f + 0.3f * pulse), 1.5f, true);
        }
        var label = "現在地  " + name;
        var width = F.GetStringSize(label, HorizontalAlignment.Left, -1, 15).X;
        var p = at + new Vector2(ring ? 24 : 12, -10);
        DrawRect(new Rect2(p + new Vector2(-5, -16), width + 10, 22), new Color(0.01f, 0.02f, 0.05f, 0.8f));
        DrawRect(new Rect2(p + new Vector2(-5, -16), width + 10, 22), new Color(Colors.White, 0.5f), false, 1);
        Text(p, label, Colors.White, 15);
    }

    public Vector2 SystemPoint(int star, Vec3d p) => MapPoint(Game.V!.ChartPositions[star] + p * Sublight.Scale);

    /// <summary>Close enough to a system, its station's orbit comes into view.</summary>
    void DrawStations()
    {
        if (Game.Chart.Zoom < 40) return;
        var g = Game.G!; var v = Game.V!;
        foreach (var star in g.Ports.Concat(g.Depots))
        {
            var st = g.StationAt(star);
            var host = (st.Host ?? g.System(star)).StarPosition(v.AbsoluteTime);
            if (!new Rect2(Vector2.Zero, Size).Grow(200).HasPoint(MapPoint(v.ChartPositions[star]))) continue;
            Vector2? last = null;
            for (int i = 0; i <= 64; i++)
            {
                var a = i * Math.Tau / 64;
                var p = SystemPoint(star, host + (st.U * Math.Cos(a) + st.W * Math.Sin(a)) * st.Radius);
                if (last is { } l) DrawLine(l, p, new Color(Good, 0.35f), 1, true);
                last = p;
            }
            var at = SystemPoint(star, host + st.Position(v.AbsoluteTime));
            DrawRect(new Rect2(at - new Vector2(3, 3), 6, 6), Good);
            Text(at + new Vector2(7, -5), star == v.TargetStar ? "目的港" : g.Ports.Contains(star) ? "港" : "補給港", Good, 12);
        }
    }

    /// <summary>The orbit a descent right now would leave the ship on.</summary>
    void DrawDescent()
    {
        if (Game.DescentForecast is not { } f || Game.DescentStar is not { } star) return;
        var track = f.Track;
        for (int i = 1; i < f.DrawnCount; i++)
            DrawLine(SystemPoint(star.Id, track[i - 1].Position), SystemPoint(star.Id, track[i].Position), new Color(0.6f, 0.9f, 1f, 0.8f), 1.4f, true);
    }

    void DrawMap()
    {
        var g = Game.G!; var v = Game.V!;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.02f, 0.05f, 0.92f));

        DrawGalaxyLayer();
        DrawStations();
        DrawDescent();
        var me = MapPoint(v.EstimatedPosition);
        DrawArc(me, Mathf.Max(4f, (float)v.NavSigma * Game.Chart.Zoom * 2), 0, Mathf.Tau, 20, Ink, 1.5f, true);
        DrawCircle(me, 2.5f, Ink);
        var c0 = Size / 2;
        DrawLine(c0 + new Vector2(-7, 0), c0 + new Vector2(7, 0), Dim, 1);
        DrawLine(c0 + new Vector2(0, -7), c0 + new Vector2(0, 7), Dim, 1);
        Centered(118, "ダブルタップ その星を中心に   2本指 移動   ドラッグ 回転", Dim, 14);
        Centered(138, "ピンチ 拡大（寄ると港の軌道）   R 自機へ   T 目的地へ", Dim, 14);
    }

    Vector2 _mapPress;
    bool _mapMoved;

    public void MapMouse(InputEventMouseButton mb)
    {
        if (mb.ButtonIndex is MouseButton.Right or MouseButton.Left) _mapDragging = mb.Pressed;
        if (mb.ButtonIndex == MouseButton.Right)
        {
            // A right drag turns the chart; a right click without moving opens the menu.
            if (mb.Pressed) { _mapPress = mb.Position; _mapMoved = false; }
            else if (!_mapMoved) MapMenu(mb.Position);
            return;
        }
        if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) Game.Chart.ZoomBy(1.1f);
        if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) Game.Chart.ZoomBy(1 / 1.1f);
        if (mb.ButtonIndex != MouseButton.Left) return;
        if (mb.Pressed)
        {
            _mapPress = mb.Position; _mapMoved = false;
            if (mb.DoubleClick && PickStar(mb.Position) is int star)
            {
                Game.Chart.Center = Game.V!.ChartPositions[star];
                if (_aimBeforeTap is { } aim) Game.SetPlanDir(aim);   // the first tap only meant to select
                _mapMoved = true;
            }
            return;
        }
        if (_mapMoved || Game.Screen != Screen.Plan) return;   // a drag rotates; only a tap picks
        if (PickStar(mb.Position) is int pick)
        {
            _aimBeforeTap = Game.PlanDir;
            Game.SetPlanDir(Game.V!.ChartPositions[pick] - Game.V.EstimatedPosition);
        }
    }

    Vec3d? _aimBeforeTap;

    int? PickStar(Vector2 at)
    {
        var v = Game.V!;
        var pick = Enumerable.Range(0, Game.G!.Stars.Length)
            .OrderBy(i => MapPoint(v.ChartPositions[i]).DistanceTo(at)).First();
        return MapPoint(v.ChartPositions[pick]).DistanceTo(at) < 24 ? pick : null;
    }

    public void MapDrag(InputEventMouseMotion mm)
    {
        if (Input.IsMouseButtonPressed(MouseButton.Middle)) { Game.Chart.Pan(-mm.Relative); return; }
        if (!_mapDragging) return;
        if (mm.Position.DistanceTo(_mapPress) > 5) _mapMoved = true;
        Game.Chart.Rotate(mm.Relative);
    }
}

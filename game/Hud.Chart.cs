using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

public partial class Hud
{

    /// <summary>The middle of the chart: the open space over the helm, not the screen's middle, where the helm sits.</summary>
    Vector2 MapCentre => new(Size.X / 2, Mathf.Max(ClusterTop / 2 + 20, 180));
    Vector2 MapPoint(Vec3d p) => Game.Chart.Project(p, MapCentre * 2);

    /// <summary>Stars, wells and the FTL path. Shared by the galaxy map and the zoomed-out orbit view.</summary>
    /// <summary>
    /// A dashed line cut to the screen first: zoomed into a moon's sphere, a line between stars runs
    /// hundreds of millions of pixels, and dashing all of it would stall the frame.
    /// </summary>
    void DashedOnScreen(Vector2 a, Vector2 b, Color col, float width, float dash, bool aligned = false)
    {
        var view = new Rect2(Vector2.Zero, Size).Grow(20);
        var d = b - a;
        double t0 = 0, t1 = 1;
        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-12) return q >= 0;
            var r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
        if (!(Clip(-d.X, a.X - view.Position.X) && Clip(d.X, view.End.X - a.X) && Clip(-d.Y, a.Y - view.Position.Y) && Clip(d.Y, view.End.Y - a.Y))) return;
        DrawDashedLine(a + d * (float)t0, a + d * (float)t1, col, width, dash, aligned);
    }

    void DrawGalaxyLayer(int here = -1)
    {
        var g = Game.G!; var v = Game.V!;
        foreach (var n in Game.Chart.Zoom < 200 ? g.Nebulae : Array.Empty<Nebula>())
        {
            var c = MapPoint(n.Center);
            for (int i = 4; i >= 1; i--)
                DrawCircle(c, (float)n.Radius * Game.Chart.Zoom * i * 0.45f, new Color(0.35f, 0.5f, 0.8f, 0.018f * (float)n.Density));
        }
        // Gate links, faint, under the stars.
        foreach (var (gate, exit) in g.GateLinks)
            if (gate < exit && g.Alive(gate) && g.Alive(exit))
                DashedOnScreen(MapPoint(v.ChartPositions[g.StarOf(gate)]), MapPoint(v.ChartPositions[g.StarOf(exit)]), new Color(0.8f, 0.6f, 1f, 0.45f), 1.5f, 8, true);
        var current = CurrentStar();
        var zoom = Game.Chart.Zoom;
        var view = new Rect2(Vector2.Zero, Size).Grow(60);
        var bound = v.Hold.Where(c => c.IsJob).Select(c => g.StarOf(c.Mission.Target)).ToHashSet();
        // The company's other ships, where they wait.
        var save = Game.Save!;
        var parked = save.Fleet.Where(x => x.Id != save.Flown).GroupBy(x => g.StarOf(x.Station)).ToDictionary(k => k.Key, k => k.Count());
        for (int i = 0; i < g.Stars.Length; i++)
        {
            var s = g.Stars[i];
            var p = MapPoint(v.ChartPositions[i]);
            if (i == here || !view.HasPoint(p)) continue;   // the orbit view draws its own star, at its true place
            DrawCircle(p, 2.5f + (float)s.Mass, Sky.StarColor(s.Temperature));
            // Chart ring: how far this star may be from where it is drawn.
            var err = (float)(Tuning.ChartErrorLy * Voyage.ChartFactor(v.ClosestApproach[i]) * zoom);
            if (err > 2) DrawArc(p, err * 2, 0, Mathf.Tau, 20, new Color(1f, 0.6f, 0.3f, 0.45f), 1f, true);
            var station = g.HasStation(i);
            DrawClaim(p, g.Claim(i));
            var name = s.Name + Missions.Mark(g, i) + (parked.TryGetValue(i, out var ships) ? $"  自船×{ships}" : "");
            if (ships > 0) DrawRect(new Rect2(p + new Vector2(-9, -9), 18, 18), new Color(Good, 0.8f), false, 1.5f);
            if (i == current) { DrawHere(p, name, ring: true); continue; }
            var key = i == v.TargetStar || bound.Contains(i) || ships > 0;
            // Zoomed out, names give way to the regions' as those come in; where the ship is bound, and its ships, stay.
            var shown = key ? 1 : 1 - RegionFade;
            if (!key && (shown <= 0 || zoom < 8 && !station)) continue;
            var c = i == v.TargetStar || ships > 0 ? Good : bound.Contains(i) ? Warn : station ? Ink : Dim;
            Text(p + new Vector2(8, 4), name, new Color(c, c.A * shown), 13);
        }
        DrawRegionNames();
        DrawSystemsInView(here);
        DrawSystemJobs();
        DrawWinds();
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

    /// <summary>
    /// Whose a system is: a ring in its holder's colour, and for a contested one, the ring split between
    /// the holder and its rival, with a short tick across.
    /// </summary>
    void DrawClaim(Vector2 p, (int Holder, bool Contested) claim)
    {
        if (claim.Holder < 0) return;
        var c = new Color(Palette.Faction(claim.Holder), 0.85f);
        if (!claim.Contested) { DrawArc(p, 6.5f, 0, Mathf.Tau, 20, c, 1.5f, true); return; }
        var rival = new Color(Palette.Faction(Game.G!.Factions.Select(f => f.Id).First(f => f != claim.Holder)), 0.85f);
        DrawArc(p, 7f, -Mathf.Pi / 2, Mathf.Pi / 2, 12, c, 2f, true);
        DrawArc(p, 7f, Mathf.Pi / 2, Mathf.Pi * 1.5f, 12, rival, 2f, true);
        DrawLine(p + new Vector2(-10, 10), p + new Vector2(10, -10), new Color(Palette.Warn, 0.9f), 1.5f, true);
    }

    /// <summary>Zoomed out to where regions read, each one's name over its middle.</summary>
    /// <summary>How far the regions' names have come in, 0 to 1, as the chart zooms out.</summary>
    float RegionFade => Mathf.Clamp((16 - (float)Game.Chart.Zoom) / 8, 0, 1);

    void DrawRegionNames()
    {
        var fade = RegionFade;
        if (fade <= 0) return;
        var g = Game.G!;
        var mine = CurrentStar() is var here and >= 0 ? g.Stars[here].Region : -1;
        foreach (var region in g.Regions)
        {
            var label = region.Name + " 宙域";
            var size = 24;
            var width = F.GetStringSize(label, HorizontalAlignment.Left, -1, size).X;
            var at = MapPoint(region.Center) + new Vector2(-width / 2, 0);
            Text(at, label, new Color(region.Id == mine ? Good : Ink, 0.4f * fade), size);
            if (region.Id < g.RegionOwners.Length && g.RegionOwners[region.Id] is var f and >= 0)
            {
                var owner = g.Factions[f].Name;
                var w = F.GetStringSize(owner, HorizontalAlignment.Left, -1, 16).X;
                Text(MapPoint(region.Center) + new Vector2(-w / 2, 22), owner, new Color(Palette.Faction(f), 0.55f * fade), 16);
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
        var region = CurrentStar() is var star and >= 0 ? $"   {Game.G!.Regions[Game.G.Stars[star].Region].Name} 宙域" : "";
        var label = "現在地  " + name + region;
        var width = F.GetStringSize(label, HorizontalAlignment.Left, -1, 15).X;
        var p = at + new Vector2(ring ? 24 : 12, -10);
        DrawRect(new Rect2(p + new Vector2(-5, -16), width + 10, 22), new Color(0.01f, 0.02f, 0.05f, 0.8f));
        DrawRect(new Rect2(p + new Vector2(-5, -16), width + 10, 22), new Color(Colors.White, 0.5f), false, 1);
        Text(p, label, Colors.White, 15);
    }

    public Vector2 SystemPoint(int star, Vec3d p) => MapPoint(Game.V!.ChartPositions[star] + p * Sublight.ChartLyPerAu);

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
        DrawDescent();
        var me = MapPoint(v.EstimatedPosition);
        DrawArc(me, Mathf.Max(4f, (float)v.NavSigma * Game.Chart.Zoom * 2), 0, Mathf.Tau, 20, Ink, 1.5f, true);
        DrawCircle(me, 2.5f, Ink);
        var c0 = MapCentre;
        DrawLine(c0 + new Vector2(-7, 0), c0 + new Vector2(7, 0), Dim, 1);
        DrawLine(c0 + new Vector2(0, -7), c0 + new Vector2(0, 7), Dim, 1);
        Centered(118, "ダブルタップ その星を中心に   2本指 移動   ドラッグ 回転", Dim, 14);
        Centered(138, "ピンチ 拡大（寄るとその星系）   R 自機へ   T 目的地へ   V 風向   P 仕事", Dim, 14);
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

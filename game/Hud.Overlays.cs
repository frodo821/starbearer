using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>
/// What the chart adds over the stars: any system drawn as a system once the chart is close enough to
/// it, the winds a jump from the present orbit would meet, and where the jobs of this system go.
/// </summary>
public partial class Hud
{
    /// <summary>Pixels per AU from which a system is drawn with its planets, moons and stations, not as a point.</summary>
    const float SystemScale = 35;

    bool SystemsShown => Game.Chart.PixelsPerAu >= SystemScale;

    /// <summary>Every system on screen but the one drawn by the orbit view itself, as a system, when the chart is close enough.</summary>
    void DrawSystemsInView(int here)
    {
        if (!SystemsShown) return;
        var g = Game.G!; var v = Game.V!;
        var view = new Rect2(Vector2.Zero, Size);
        for (int i = 0; i < g.Stars.Length; i++)
        {
            if (i == here) continue;
            var c = MapPoint(v.ChartPositions[i]);
            var reach = (float)(Sublight.SystemRadius(g.Stars[i]) * Game.Chart.PixelsPerAu);
            if (!view.Grow(reach).HasPoint(c)) continue;
            DrawSystemAt(i);
        }
    }

    /// <summary>A system away from the ship, where the chart shows it: its star, planets and moons with their orbits, and its stations.</summary>
    void DrawSystemAt(int star)
    {
        var g = Game.G!; var v = Game.V!;
        var t = v.AbsoluteTime;
        Vector2 At(Vec3d p) => SystemPoint(star, p);
        var c = At(Vec3d.Zero);
        var col = Sky.StarColor(g.Stars[star].Temperature);
        for (int i = 4; i >= 1; i--) DrawCircle(c, 5f * i * i * 0.5f, new Color(col, 0.1f));
        DrawBodiesOf(g.System(star), At);
        var bound = v.Hold.Where(x => x.IsJob).Select(x => x.Mission.Target).ToHashSet();
        var many = g.StationsIn(star).Count() > 1;
        foreach (var id in g.StationsIn(star))
        {
            var st = g.StationAt(id);
            var host = (st.Host ?? g.System(star)).StarPosition(t);
            Vector2? last = null;
            for (int i = 0; i <= 64; i++)
            {
                var a = i * Math.Tau / 64;
                var p = At(host + (st.U * Math.Cos(a) + st.W * Math.Sin(a)) * st.Radius);
                if (last is { } l) DrawLine(l, p, new Color(Good, 0.3f), 1, true);
                last = p;
            }
            var sp = At(host + st.Position(t));
            DrawRect(new Rect2(sp - new Vector2(3, 3), 6, 6), Good);
            var name = many ? Missions.KindLabel(g.KindOf(id)) : Missions.ShortName(g.KindOf(id));   // several: told apart
            Text(sp + new Vector2(7, -5), name + (bound.Contains(id) ? "（届け先）" : star == v.TargetStar ? "（目的地）" : ""), Good, 12);
        }
    }

    /// <summary>A system's planets and moons, with their orbits, through a mapping to the screen.</summary>
    void DrawBodiesOf(Center system, Func<Vec3d, Vector2> point)
    {
        var t = Game.V!.AbsoluteTime;
        var scale = Game.Chart.PixelsPerAu;
        foreach (var body in system.Tree.Skip(1))
        {
            var around = body.Parent!.StarPosition(t);
            var (u, w, rad) = (body.U, body.W, body.OrbitRadius);
            if (rad * scale > 12)
                for (int i = 0; i < 96; i++)
                    DrawLine(point(around + (u * Math.Cos(i * Math.Tau / 96) + w * Math.Sin(i * Math.Tau / 96)) * rad),
                        point(around + (u * Math.Cos((i + 1) * Math.Tau / 96) + w * Math.Sin((i + 1) * Math.Tau / 96)) * rad), new Color(Dim, 0.3f), 1, true);
            var at = point(body.StarPosition(t));
            var col = body.Kind switch { BodyKind.Giant => new Color(0.95f, 0.8f, 0.55f), BodyKind.Moon => new Color(0.75f, 0.75f, 0.8f), _ => new Color(0.55f, 0.75f, 0.9f) };
            DrawCircle(at, Mathf.Max(body.Kind == BodyKind.Moon ? 2f : 3.5f, (float)body.BodyRadius * scale), col);
            if (body.Edge * scale > 10 || body.Kind != BodyKind.Moon) Text(at + new Vector2(7, 12), body.Name, new Color(col, 0.8f), 12);
        }
    }

    /// <summary>
    /// Where a jump from the present orbit gets a tailwind and where a headwind: the orbit's speed is
    /// carried into the well, so a kick along it adds to it and one against it pays it back first. Two
    /// cones from the ship, a fixed size on screen, with the speed carried.
    /// </summary>
    void DrawWinds()
    {
        var v = Game.V!;
        if (!Game.ShowWinds || v.Phase != Phase.Sublight) return;
        var (_, carried) = v.JumpState;
        var speed = carried.Length;
        if (speed < 1e-9) return;
        var axis = carried / speed;
        var apex = v.System.Position + (v.Primary.StarPosition(v.AbsoluteTime) + v.SubPosition) * Sublight.ChartLyPerAu;
        var length = 130.0 / Game.Chart.Zoom;
        Cone(apex, axis, length, Good, $"追い風 {speed:F2} ly/日");
        Cone(apex, -axis, length, Bad, $"向かい風 {speed:F2} ly/日");
    }

    /// <summary>A cone in space from an apex along an axis, its rim a quarter of its length across, drawn to the chart.</summary>
    void Cone(Vec3d apex, Vec3d axis, double length, Color col, string label)
    {
        const int n = 32;
        var tip = apex + axis * length;
        var radius = length * 0.45;   // about 24 degrees off the axis
        var u = axis.AnyPerpendicular().Normalized();
        var w = axis.Cross(u);
        var a = MapPoint(apex);
        var rim = Enumerable.Range(0, n + 1).Select(i => MapPoint(tip + (u * Math.Cos(i * Math.Tau / n) + w * Math.Sin(i * Math.Tau / n)) * radius)).ToArray();
        var fill = new Color(col, 0.07f);
        for (int i = 0; i < n; i++)
        {
            // Drawn as bare triangles: seen edge on, a cone's slices are slivers that polygon triangulation refuses.
            DrawPrimitive(new[] { a, rim[i], rim[i + 1] }, new[] { fill, fill, fill }, new Vector2[3]);
        }
        for (int i = 0; i < n; i++) DrawLine(rim[i], rim[i + 1], new Color(col, 0.7f), 1.2f, true);
        for (int i = 0; i < n; i += 4) DrawLine(a, rim[i], new Color(col, 0.3f), 1, true);
        Text(MapPoint(tip + axis * length * 0.15) + new Vector2(-30, 0), label, col, 13);
    }

    List<(int From, Mission Job)> _systemJobs = new();
    string _systemJobsKey = "";

    /// <summary>
    /// The jobs posted at this system's stations: the board of the port the ship is checked in at as it
    /// stands, the others as their latest news shows them (in the same system that is today's).
    /// </summary>
    List<(int From, Mission Job)> SystemJobs(int star)
    {
        var g = Game.G!; var s = Game.Save!;
        var m = s.MarketIn(g);
        var key = $"{star}:{s.PortStation}:{s.Deliveries}:{m.Day:F3}:{s.Hold.Count}";
        if (key == _systemJobsKey) return _systemJobs;
        _systemJobsKey = key;
        _systemJobs = g.StationsIn(star).SelectMany(id =>
            (id == s.PortStation ? Missions.OffersFor(s, g) : Missions.Board(g, m.KnownAs(g, s.PortStation, id) ?? m, id, s.Deliveries, s.Hold))
            .Select(job => (id, job))).ToList();
        return _systemJobs;
    }

    /// <summary>Where the jobs of the system the ship is in go: a line to each star, with how many and the best pay.</summary>
    void DrawSystemJobs()
    {
        if (!Game.ShowJobs) return;
        var star = CurrentStar();
        if (star < 0) return;
        var g = Game.G!; var v = Game.V!;
        var from = MapPoint(v.ChartPositions[star]);
        var col = new Color(1f, 0.75f, 0.35f);
        foreach (var to in SystemJobs(star).GroupBy(x => g.StarOf(x.Job.Target)))
        {
            var label = $"仕事 {to.Count()}件  最高 {to.Max(x => x.Job.Pay):F0}";
            if (to.Key == star)
            {
                Text(from + new Vector2(14, 22), "星系内の" + label, col, 12);
                continue;
            }
            var p = MapPoint(v.ChartPositions[to.Key]);
            DashedOnScreen(from, p, new Color(col, 0.55f), 1.2f, 6);
            if (new Rect2(Vector2.Zero, Size).HasPoint(p)) Text(p + new Vector2(8, 18), label, col, 12);
        }
    }
}

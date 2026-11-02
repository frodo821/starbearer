using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>The orbit chart: paths, stages, nodes, bodies and the station, drawn around the ship's primary.</summary>
public partial class Hud
{
    static readonly Color[] StageColors =
    {
        new(0.5f, 0.8f, 1f, 0.9f),      // coasting before any burn
        new(1f, 0.75f, 0.3f, 0.95f),
        new(0.6f, 1f, 0.6f, 0.95f),
        new(1f, 0.55f, 0.75f, 0.95f),
        new(0.75f, 0.65f, 1f, 0.95f),
    };

    static Color StageColor(int stage) => StageColors[Math.Min(stage, StageColors.Length - 1)];

    /// <summary>A point relative to the ship's primary, on the one chart.</summary>
    public Vector2 OrbitPoint(Vec3d p) => StarPoint(Game.V!.Primary.StarPosition(Game.V.AbsoluteTime) + p);

    /// <summary>A point relative to this system's star.</summary>
    Vector2 StarPoint(Vec3d p) => MapPoint(Game.V!.System.Position + p * Sublight.Scale);


    Stage? StageOf(Maneuver n) => Game.SubForecast?.StageOf(n);

    /// <summary>Where a leg of the path sits on screen. Later legs follow their star as the chart shows it.</summary>
    public Func<Vec3d, Vector2> LegMap(OrbitForecast leg)
    {
        if (leg.Center!.Key == Game.V!.Primary.Key) return OrbitPoint;
        var body = leg.Center.StarPosition(leg.Track[0].Time);   // where it will be when the ship gets there
        return p => SystemPoint(leg.Star, body + p);
    }

    Vector2? NodePoint(Maneuver n) => Game.SubForecast?.LegOf(n) is { } leg ? LegMap(leg)(StageOf(n)!.Value.Position) : null;

    /// <summary>The points of the drawn path still to come, soonest first. A closed coast comes round
    /// again, so the part behind the ship counts as its next lap.</summary>
    IEnumerable<(Vector2 At, double Time)> PathAhead(OrbitForecast fc)
    {
        var now = Game.V!.AbsoluteTime;
        foreach (var leg in fc.Legs)
        {
            var map = LegMap(leg);
            var ahead = Enumerable.Range(0, leg.DrawnCount).Where(i => leg.Track[i].Time >= now);
            foreach (var i in ahead) yield return (map(leg.Track[i].Position), leg.Track[i].Time);
            if (!Closed(leg)) continue;
            var lap = leg.Track[leg.DrawnCount - 1].Time - leg.Track[0].Time;
            foreach (var i in Enumerable.Range(0, leg.DrawnCount).Where(i => leg.Track[i].Time < now))
                yield return (map(leg.Track[i].Position), leg.Track[i].Time + Math.Ceiling((now - leg.Track[i].Time) / lap) * lap);
        }
    }

    static bool Closed(OrbitForecast leg) => leg.Exit == null && !leg.Impact && !leg.IsDrift && leg.Stages.Count == 0;

    /// <summary>First track index to draw: an open path has nothing behind the ship.</summary>
    int DrawFrom(OrbitForecast leg)
    {
        if (Closed(leg)) return 1;
        var now = Game.V!.AbsoluteTime;
        for (int i = 1; i < leg.DrawnCount; i++) if (leg.Track[i].Time >= now) return i;
        return leg.DrawnCount;
    }

    void Chevron(Vector2 tip, Vector2 way, Color c, float size)
    {
        var side = new Vector2(-way.Y, way.X);
        DrawLine(tip, tip - way * size + side * (size * 0.6f), c, 2f, true);
        DrawLine(tip, tip - way * size - side * (size * 0.6f), c, 2f, true);
    }

    /// <summary>Arrowheads along a path, spaced on screen, so the way round an orbit can be read anywhere on it.</summary>
    void DrawFlow(OrbitForecast f, Color c, Func<Vec3d, Vector2>? map = null)
    {
        map ??= OrbitPoint;
        const float Spacing = 130;
        var run = Spacing * 0.6f;
        for (int i = DrawFrom(f); i < f.DrawnCount; i++)
        {
            var (a, b) = (map(f.Track[i - 1].Position), map(f.Track[i].Position));
            var length = a.DistanceTo(b);
            if (length < 0.01f) continue;
            run -= length;
            if (run > 0) continue;
            run += Spacing;
            Chevron(b, (b - a) / length, c, 9);
        }
    }

    /// <summary>The path past a change of primary: where a body lets go or takes over, the run through open
    /// space, and the pass around whatever comes next. Each leg is drawn on its own body.</summary>
    void DrawHandover(OrbitForecast f, Color c, int stage = 0, Vector2? arrived = null)
    {
        var map = LegMap(f);
        var v = Game.V!;
        stage += f.Enter != null ? f.Stages.Count : 0;
        if (f.Enter is { } entry)
        {
            var into = map(f.Track[^1].Position);
            DrawArc(into, 6, 0, Mathf.Tau, 16, Good, 1.6f, true);
            if (arrived is not { } near || near.DistanceTo(into) > 90)
                Text(into + new Vector2(10, -6), $"Encounter {entry.Body.Name} " + Instruments.Countdown(entry.Time - v.AbsoluteTime), Good, 13);
            if (f.Beyond is { } inside) DrawPass(inside, c, stage, into);
            return;
        }
        if (f.Exit is not { } e) return;
        var at = map(e.Position);
        DrawArc(at, 6, 0, Mathf.Tau, 16, c, 1.6f, true);
        // Zoomed out a whole pass is a few pixels, and the two labels would land on each other.
        if (e.Time > v.AbsoluteTime && (arrived is not { } came || came.DistanceTo(at) > 90))
            Text(at + new Vector2(10, -6), "Disengage " + Instruments.Countdown(e.Time - v.AbsoluteTime), c, 13);
        stage += f.Stages.Count;
        if (f.Center?.Parent != null)
        {
            if (f.Beyond is { } outer) DrawPass(outer, c, stage, at);
            return;
        }
        if (f.Drift is not { } drift) return;
        stage = DrawDrift(drift, map, c, stage);
        if (f.Next is not { } n) return;
        var star = Game.G!.Stars[n.Star];
        var hit = SystemPoint(n.Star, (n.Point - star.Position) / Sublight.Scale);
        DrawArc(hit, 6, 0, Mathf.Tau, 16, Good, 1.6f, true);
        Text(hit + new Vector2(10, -6), $"Encounter {star.Name} " + Instruments.Countdown(n.Time - v.AbsoluteTime), Good, 13);
        if (f.Beyond is { } pass) DrawPass(pass, c, stage, hit);
    }

    /// <summary>The dashed run through open space, one length per burn. Returns the stage it ends on.</summary>
    int DrawDrift(OrbitForecast drift, Func<Vec3d, Vector2> map, Color c, int stage)
    {
        var from = 0;
        foreach (var to in drift.Stages.Select(s => s.TrackIndex).Append(drift.DrawnCount - 1))
        {
            var line = stage == 0 ? new Color(c, 0.7f) : new Color(StageColor(stage), 0.8f);
            DrawDashedLine(map(drift.Track[from].Position), map(drift.Track[to].Position), line, 1.4f, 5);
            from = to;
            if (to != drift.DrawnCount - 1) stage++;
        }
        return stage;
    }

    void DrawClosest(Approach a, Func<Vec3d, Vector2> map)
    {
        var (ps, pt) = (map(a.Ship), map(a.Station));
        DrawDashedLine(ps, pt, Warn, 1, 3);
        DrawArc(ps, 6, 0, Mathf.Tau, 16, Warn, 1.4f, true);
        DrawArc(pt, 6, 0, Mathf.Tau, 4, Good, 1.4f, true);
        Text(ps + new Vector2(9, 16), $"{a.Distance:F3} AU", Warn, 13);
    }

    /// <summary>The path around a star not yet reached, coloured by the stage flown so far.</summary>
    void DrawPass(OrbitForecast pass, Color c, int stage, Vector2 hit)
    {
        var map = LegMap(pass);
        var at = 0;
        var line = new Color(stage == 0 ? Good : StageColor(stage), 0.8f);
        for (int i = 1; i < pass.DrawnCount; i++)
        {
            while (at < pass.Stages.Count && i > pass.Stages[at].TrackIndex)
            {
                at++;
                line = new Color(StageColor(stage + at), 0.8f);
            }
            DrawLine(map(pass.Track[i - 1].Position), map(pass.Track[i].Position), line, 1.4f, true);
        }
        DrawFlow(pass, line, map);
        if (pass.Impact) Text(map(pass.Track[^1].Position) + new Vector2(10, 14), "天体に衝突", Bad, 13);
        if (pass.Closest is { } a) DrawClosest(a, map);
        DrawHandover(pass, c, stage, hit);
    }

    void Polyline3(Func<double, Vec3d> f, Color c, float width)
    {
        for (int i = 0; i < 96; i++)
            DrawLine(OrbitPoint(f(i * Math.Tau / 96)), OrbitPoint(f((i + 1) * Math.Tau / 96)), c, width, true);
    }

    void DrawOrbitMap()
    {
        var v = Game.V!;
        var star = v.System;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.015f, 0.035f, 0.9f));
        DrawGalaxyLayer(v.SystemStar);
        var c = StarPoint(Vec3d.Zero);
        var col = Sky.StarColor(star.Temperature);
        for (int i = 4; i >= 1; i--) DrawCircle(c, 5f * i * i * 0.5f, new Color(col, 0.13f));
        DrawCircle(c, 5, col);
        if (Game.Chart.PixelsPerAu < 35)
        {
            // Zoomed out to the galaxy the system is one point, but where the ship is bound still reads.
            if (Game.SubForecast is { } leaving) DrawHandover(leaving, Warn);
            DrawHere(c, star.Name, ring: true);
            return;
        }
        DrawHere(c, star.Name, ring: false);
        DrawBodies();
        DrawTarget();
        DrawLocalStation();
        if (Game.SubForecast is { } fc)
        {
            DrawPaths(fc);
            DrawApsides(fc);
            foreach (var (pos, asc) in fc.PlaneCrossings)
            {
                var p = OrbitPoint(pos);
                DrawArc(p, 5, 0, Mathf.Tau, 12, new Color(1f, 0.5f, 0.9f), 1.4f, true);
                Text(p + new Vector2(7, 4), asc ? "AN" : "DN", new Color(1f, 0.5f, 0.9f), 12);
            }
            if (fc.Watched is { } watched)
            {
                if (fc.Crossing is { } x)
                {
                    var (sh, sp) = (OrbitPoint(x.Ship), OrbitPoint(x.Station));
                    DrawCircle(sh, 3, new Color(1f, 0.8f, 0.4f));
                    DrawRect(new Rect2(sp - new Vector2(4, 4), 8, 8), new Color(Good, 0.55f));
                    Text(sp + new Vector2(8, 14), $"到着時の{Main.NameOf(watched)}", new Color(Good, 0.8f), 12);
                }
                if (fc.Closest is { } a) DrawClosest(a, OrbitPoint);
            }
        }
        DrawNodes();
        DrawShipMarker();
    }

    /// <summary>The system's planets and moons, with their orbits.</summary>
    void DrawBodies()
    {
        var v = Game.V!;
        var t = v.AbsoluteTime;
        foreach (var body in Game.G!.System(v.SystemStar).Tree.Skip(1))
        {
            var around = body.Parent!.StarPosition(t);
            var (u, w, rad) = (body.U, body.W, body.OrbitRadius);
            if (rad * Game.Chart.PixelsPerAu > 12)
                for (int i = 0; i < 96; i++)
                    DrawLine(StarPoint(around + (u * Math.Cos(i * Math.Tau / 96) + w * Math.Sin(i * Math.Tau / 96)) * rad),
                        StarPoint(around + (u * Math.Cos((i + 1) * Math.Tau / 96) + w * Math.Sin((i + 1) * Math.Tau / 96)) * rad), new Color(Dim, 0.3f), 1, true);
            var at = StarPoint(body.StarPosition(t));
            var col = body.Kind switch { BodyKind.Giant => new Color(0.95f, 0.8f, 0.55f), BodyKind.Moon => new Color(0.75f, 0.75f, 0.8f), _ => new Color(0.55f, 0.75f, 0.9f) };
            DrawCircle(at, Mathf.Max(body.Kind == BodyKind.Moon ? 2f : 3.5f, (float)body.BodyRadius * Game.Chart.PixelsPerAu), col);
            if (body.Edge * Game.Chart.PixelsPerAu > 10 || body.Kind != BodyKind.Moon) Text(at + new Vector2(7, 12), body.Name, new Color(col, 0.8f), 12);
        }
    }

    static readonly Color ApsisColor = new(0.55f, 0.85f, 1f);

    /// <summary>The periapses and apoapses still ahead on the plan, each on its own leg.</summary>
    IEnumerable<(Vector2 At, Apsis Apsis)> ApsidesAhead(OrbitForecast fc)
    {
        var now = Game.V!.AbsoluteTime;
        foreach (var leg in fc.Legs)
        {
            var map = LegMap(leg);
            foreach (var a in leg.Apsides.Where(a => a.Time >= now)) yield return (map(a.Position), a);
        }
    }

    void DrawApsides(OrbitForecast fc)
    {
        var now = Game.V!.AbsoluteTime;
        foreach (var (at, a) in ApsidesAhead(fc))
        {
            DrawCircle(at, 4.5f, ApsisColor);
            DrawArc(at, 7, 0, Mathf.Tau, 16, new Color(ApsisColor, 0.6f), 1.2f, true);
            Text(at + new Vector2(9, a.Apo ? -8 : 16), $"{(a.Apo ? "Ap" : "Pe")} {a.Radius:F3} AU", ApsisColor, 12);
            Text(at + new Vector2(9, a.Apo ? 6 : 30), Instruments.Countdown(a.Time - now), new Color(ApsisColor, 0.6f), 11);
        }
    }

    /// <summary>The time of the apsis marker under a tap, if any.</summary>
    double? ApsisAt(Vector2 at) => Game.SubForecast is { } fc
        ? ApsidesAhead(fc).Where(x => x.At.DistanceTo(at) < 12).OrderBy(x => x.At.DistanceTo(at)).Select(x => (double?)x.Apsis.Time).FirstOrDefault()
        : null;

    public Vector2? ApsisPointForTest(bool apo) => Game.SubForecast is { } fc ? ApsidesAhead(fc).Where(x => x.Apsis.Apo == apo).Select(x => (Vector2?)x.At).FirstOrDefault() : null;

    /// <summary>Screen point of a body of this system now.</summary>
    public Vector2 BodyPointForTest(Center body) => BodyPoint(body);
    Vector2 BodyPoint(Center body) => StarPoint(body.StarPosition(Game.V!.AbsoluteTime));

    /// <summary>The chosen body stands out: its orbit drawn brighter and corner marks round it. The body the
    /// markers are measured against on the way, when that is another, is marked too.</summary>
    void DrawTarget()
    {
        var v = Game.V!;
        var t = v.AbsoluteTime;
        void Mark(Center body, string label, Color c)
        {
            var around = body.Parent!.StarPosition(t);
            var (u, w, rad) = (body.U, body.W, body.OrbitRadius);
            if (rad * Game.Chart.PixelsPerAu > 12)
                for (int i = 0; i < 96; i++)
                    DrawLine(StarPoint(around + (u * Math.Cos(i * Math.Tau / 96) + w * Math.Sin(i * Math.Tau / 96)) * rad),
                        StarPoint(around + (u * Math.Cos((i + 1) * Math.Tau / 96) + w * Math.Sin((i + 1) * Math.Tau / 96)) * rad), new Color(c, 0.45f), 1.4f, true);
            var at = BodyPoint(body);
            var r = Mathf.Max(9f, (float)body.BodyRadius * Game.Chart.PixelsPerAu + 5);
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                var corner = at + new Vector2(sx * r, sy * r);
                DrawLine(corner, corner - new Vector2(sx * 5, 0), c, 1.6f, true);
                DrawLine(corner, corner - new Vector2(0, sy * 5), c, 1.6f, true);
            }
            Text(at + new Vector2(r + 4, -r), label, c, 12);
        }
        if (v.Goal is Center goal) Mark(goal, "目標", Good);
        if (Game.SubForecast?.Watched is Center via && via != v.Goal) Mark(via, "経由", new Color(Good, 0.6f));
    }

    /// <summary>The planet or moon under a tap, if any. The station is left to the path it sits on.</summary>
    IOrbiter? OrbiterAt(Vector2 at)
    {
        if (Game.Chart.PixelsPerAu < 35) return null;
        return Game.TargetChoices().OfType<Center>()
            .Select(c => (c, d: BodyPoint(c).DistanceTo(at)))
            .Where(x => x.d < Mathf.Max(12f, (float)x.c.BodyRadius * Game.Chart.PixelsPerAu + 4))
            .OrderBy(x => x.d).Select(x => (IOrbiter?)x.c).FirstOrDefault();
    }

    /// <summary>Where the system's station is on the orbit chart, if it has one.</summary>
    Vector2? StationPoint()
    {
        var v = Game.V!;
        if (v.LocalStation is not { } st) return null;
        var t = v.AbsoluteTime;
        var host = (st.Host ?? Game.G!.System(v.SystemStar)).StarPosition(t) - v.Primary.StarPosition(t);
        return OrbitPoint(host + st.Position(t));
    }

    public Vector2? StationPointForTest => StationPoint();

    void DrawLocalStation()
    {
        var v = Game.V!;
        if (v.LocalStation is not { } st) return;
        var t = v.AbsoluteTime;
        var host = (st.Host ?? Game.G!.System(v.SystemStar)).StarPosition(t) - v.Primary.StarPosition(t);
        var (u, w, rad) = (st.U, st.W, st.Radius);
        Polyline3(a => host + (u * Math.Cos(a) + w * Math.Sin(a)) * rad, new Color(Good, 0.35f), 1);
        var sp = StationPoint()!.Value;
        DrawRect(new Rect2(sp - new Vector2(4, 4), 8, 8), Good);
        Text(sp + new Vector2(8, -6), Missions.ShortName(Missions.KindOf(Game.G!, v.SystemStar)) + (v.SystemStar == v.TargetStar ? "（目的地）" : ""), Good, 13);
        if (st.Host?.Key != v.Primary.Key) return;
        // Drop line from the ship to the station's plane, so height reads in 3D.
        var height = v.SubPosition.Dot(st.PlaneNormal);
        DrawDashedLine(OrbitPoint(v.SubPosition), OrbitPoint(v.SubPosition - st.PlaneNormal * height), new Color(Good, 0.5f), 1, 3);
    }

    /// <summary>The plan coloured by stage, with the real path on top: where the ship goes if nothing more is flown.</summary>
    void DrawPaths(OrbitForecast fc)
    {
        var hasPlan = fc.AllStages.Any();
        int stage = 0;
        for (int i = hasPlan ? DrawFrom(fc) : fc.DrawnCount; i < fc.DrawnCount; i++)
        {
            while (stage < fc.Stages.Count && i > fc.Stages[stage].TrackIndex) stage++;
            var col = stage == 0 ? new Color(0.55f, 0.6f, 0.7f, 0.35f) : StageColor(stage);
            DrawLine(OrbitPoint(fc.Track[i - 1].Position), OrbitPoint(fc.Track[i].Position), col, 1.6f, true);
        }
        if (Game.SubCurrent is { } now)
        {
            var line = hasPlan ? new Color(1, 1, 1, 0.75f) : StageColors[0];
            for (int i = DrawFrom(now); i < now.DrawnCount; i++)
                DrawLine(OrbitPoint(now.Track[i - 1].Position), OrbitPoint(now.Track[i].Position), line, hasPlan ? 1f : 1.6f, true);
            DrawFlow(now, line);
            if (!hasPlan) DrawHandover(now, Warn);
        }
        if (hasPlan) { DrawFlow(fc, StageColor(fc.Stages.Count)); DrawHandover(fc, Warn); }
    }

    void DrawNodes()
    {
        var ordered = Game.V!.Nodes.OrderBy(n => n.Time).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var n = ordered[i];
            if (NodePoint(n) is not { } np) continue;
            var selected = n == Game.Selected;
            var nodeCol = StageColor(i + 1);
            DrawArc(np, selected ? 10 : 7, 0, Mathf.Tau, 4, nodeCol, 2, true);
            Text(np + new Vector2(11, -6), $"{i + 1}", nodeCol, 13);
            if (selected && Game.HandlesOpen) DrawHandles(n, np);
        }
    }

    void DrawHandles(Maneuver n, Vector2 np)
    {
        if (DeleteButton(n) is { } del)
        {
            DrawCircle(del, 10, new Color(Bad, 0.85f));
            DrawLine(del + new Vector2(-4, -4), del + new Vector2(4, 4), Colors.White, 2, true);
            DrawLine(del + new Vector2(-4, 4), del + new Vector2(4, -4), Colors.White, 2, true);
            Text(del + new Vector2(14, 5), "削除", Bad, 12);
        }
        foreach (var h in Handles(n))
        {
            var tip = np + h.Dir * h.Reach;
            DrawLine(np, tip, new Color(h.Color, 0.7f), 2, true);
            DrawCircle(tip, 7, h.Color);
            Text(tip + new Vector2(9, 4), h.Label, h.Color, 12);
        }
    }

    void DrawShipMarker()
    {
        var v = Game.V!;
        var me = OrbitPoint(v.SubPosition);
        DrawCircle(me, 4, Ink);
        var heading = OrbitPoint(v.SubPosition + v.SubVelocity.Normalized() * (1.0 / Game.Chart.PixelsPerAu));
        if (heading.DistanceTo(me) <= 0.01f) return;
        var way = (heading - me).Normalized();
        DrawLine(me, me + way * 24, Ink, 2f, true);
        Chevron(me + way * 24, way, Ink, 8);
    }
}

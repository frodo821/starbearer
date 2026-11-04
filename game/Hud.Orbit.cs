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
    Vector2 StarPoint(Vec3d p) => MapPoint(Game.V!.System.Position + p * Sublight.ChartLyPerAu);


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
        var hit = SystemPoint(n.Star, (n.Point - star.Position) * Sublight.AuPerLy);
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
            DashedOnScreen(map(drift.Track[from].Position), map(drift.Track[to].Position), line, 1.4f, 5);
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
        if (!Marker("最接近", ps).Shown) return;
        Text(ps + new Vector2(9, 16), "最接近 " + Instruments.Distance(a.Distance), Warn, 13);
        Text(ps + new Vector2(9, 31), $"{Instruments.Countdown(a.Time - Game.V!.AbsoluteTime)}  相対 {Instruments.Speed(a.RelativeSpeed)}", new Color(Warn, 0.7f), 11);
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
        ResetMarkers();
        DrawFocus();
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
        DrawParkedShips();
        if (Game.SubForecast is { } fc)
        {
            DrawPaths(fc);
            DrawApsides(fc);
            foreach (var (pos, asc, time, incl) in fc.PlaneCrossings.Where(x => x.Time >= v.AbsoluteTime))
            {
                var p = OrbitPoint(pos);
                var pink = new Color(1f, 0.5f, 0.9f);
                DrawArc(p, 5, 0, Mathf.Tau, 12, pink, 1.4f, true);
                Text(p + new Vector2(7, 4), asc ? "AN" : "DN", pink, 12);
                if (!Marker(asc ? "AN" : "DN", p).Shown) continue;
                Text(p + new Vector2(7, 19), $"相対傾斜 {incl:F1}°", pink, 12);
                Text(p + new Vector2(7, 33), Instruments.Countdown(time - v.AbsoluteTime), new Color(pink, 0.6f), 11);
            }
            if (fc.Watched is { } watched)
            {
                if (fc.Crossing is { } x)
                {
                    var (sh, sp) = (OrbitPoint(x.Ship), OrbitPoint(x.Station));
                    DrawCircle(sh, 3, new Color(1f, 0.8f, 0.4f));
                    DrawRect(new Rect2(sp - new Vector2(4, 4), 8, 8), new Color(Good, 0.55f));
                    Text(sp + new Vector2(8, 14), $"到着時の{Game.NameOf(watched)}", new Color(Good, 0.8f), 12);
                }
                if (fc.Closest is { } a) DrawClosest(a, OrbitPoint);
            }
        }
        DrawNodes();
        DrawShipMarker();
        // The open node's panel last, over everything on the chart.
        if (Game.HandlesOpen && Game.Selected is { } open && Game.V!.Nodes.Contains(open)) DrawNodePanel(open);
    }

    /// <summary>The system's planets and moons, with their orbits.</summary>
    void DrawBodies() => DrawBodiesOf(Game.G!.System(Game.V!.SystemStar), StarPoint);

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
            // The mark and its name always; how high and when, on the pointer or pinned by a tap.
            var shown = Marker(a.Apo ? "Ap" : "Pe", at).Shown;
            Text(at + new Vector2(9, a.Apo ? -8 : 16), (a.Apo ? "Ap" : "Pe") + (shown ? " " + Instruments.Distance(a.Radius) : ""), ApsisColor, 12);
            if (shown) Text(at + new Vector2(9, a.Apo ? 6 : 30), Instruments.Countdown(a.Time - now), new Color(ApsisColor, 0.6f), 11);
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

    /// <summary>Where the station the ship deals with is on the orbit chart, if the system has one.</summary>
    Vector2? StationPoint() => Game.V!.LocalStation is { } st ? StationPoint(st) : null;

    /// <summary>Where a station of this system is on the orbit chart.</summary>
    Vector2 StationPoint(Station st)
    {
        var v = Game.V!;
        var t = v.AbsoluteTime;
        var host = (st.Host ?? Game.G!.System(v.SystemStar)).StarPosition(t) - v.Primary.StarPosition(t);
        return OrbitPoint(host + st.Position(t));
    }

    public Vector2? StationPointForTest => StationPoint();
    public Vector2 PointOfStationForTest(Station st) => StationPoint(st);

    /// <summary>The system's stations, the one the ship deals with first.</summary>
    IEnumerable<Station> SystemStations()
    {
        var v = Game.V!;
        var local = v.LocalStation;
        return Game.G!.StationsIn(v.SystemStar).Select(Game.G.StationAt).OrderBy(st => st.Id == local?.Id ? 0 : 1).ThenBy(st => st.Id);
    }

    /// <summary>The station under a tap, if any, nearest first.</summary>
    Station? StationUnder(Vector2 at) => Game.Chart.PixelsPerAu < 35 ? null
        : SystemStations().Select(st => (st, d: StationPoint(st).DistanceTo(at))).Where(x => x.d < 14).OrderBy(x => x.d).Select(x => x.st).FirstOrDefault();

    /// <summary>
    /// Every station of the system: the one the ship deals with bright, with its orbit, the rest dim, so
    /// one can be picked. A station a job in the hold is bound for says so.
    /// </summary>
    void DrawLocalStation()
    {
        var v = Game.V!;
        var g = Game.G!;
        var t = v.AbsoluteTime;
        var bound = v.Hold.Where(c => c.IsJob).Select(c => c.Mission.Target).ToHashSet();
        var many = g.StationsIn(v.SystemStar).Count() > 1;
        foreach (var other in SystemStations().Skip(1).Reverse())
        {
            var host = (other.Host ?? g.System(v.SystemStar)).StarPosition(t) - v.Primary.StarPosition(t);
            Polyline3(a => host + (other.U * Math.Cos(a) + other.W * Math.Sin(a)) * other.Radius, new Color(Good, 0.12f), 1);
            var op = StationPoint(other);
            var col = new Color(Good, 0.55f);
            DrawRect(new Rect2(op - new Vector2(3, 3), 6, 6), col);
            Text(op + new Vector2(7, -5), Missions.KindLabel(g.KindOf(other.Id)) + (bound.Contains(other.Id) ? "（届け先）" : ""), col, 12);
        }
        if (v.LocalStation is not { } st) return;
        {
            var host = (st.Host ?? g.System(v.SystemStar)).StarPosition(t) - v.Primary.StarPosition(t);
            var (u, w, rad) = (st.U, st.W, st.Radius);
            Polyline3(a => host + (u * Math.Cos(a) + w * Math.Sin(a)) * rad, new Color(Good, 0.35f), 1);
        }
        var sp = StationPoint(st);
        DrawRect(new Rect2(sp - new Vector2(4, 4), 8, 8), Good);
        var tag = bound.Contains(st.Id) ? "（届け先）" : !many && v.SystemStar == v.TargetStar ? "（目的地）" : "";
        Text(sp + new Vector2(8, -6), (many ? Missions.KindLabel(g.KindOf(st.Id)) : Missions.ShortName(g.KindOf(st.Id))) + tag, Good, 13);
        if (st.Host?.Key != v.Primary.Key) return;
        // Drop line from the ship to the station's plane, so height reads in 3D.
        var height = v.SubPosition.Dot(st.PlaneNormal);
        DrawDashedLine(OrbitPoint(v.SubPosition), OrbitPoint(v.SubPosition - st.PlaneNormal * height), new Color(Good, 0.5f), 1, 3);
    }

    /// <summary>The company's ships left in orbit in this system: their orbits, and where they are on them.</summary>
    void DrawParkedShips()
    {
        var t = Game.V!.AbsoluteTime;
        var col = new Color(1f, 0.85f, 0.45f);
        foreach (var po in Game.ParkedHere())
        {
            var host = po.Host.StarPosition(t);
            Polyline3(a => host + po.Position(t + a / Math.Tau * po.Period), new Color(col, 0.3f), 1);
            var at = StarPoint(host + po.Position(t));
            DrawPolygon(new[] { at + new Vector2(0, -6), at + new Vector2(5, 0), at + new Vector2(0, 6), at + new Vector2(-5, 0) }, new[] { col });
            Text(at + new Vector2(8, -6), Game.NameOf(po), col, 12);
        }
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
        foreach (var h in Handles(n))
        {
            var tip = np + h.Dir * h.Reach;
            DrawLine(np, tip, new Color(h.Color, 0.7f), 2, true);
            DrawCircle(tip, 7, h.Color);
            Text(tip + new Vector2(9, 4), h.Label, h.Color, 12);
        }
    }

    const float NodePanelWidth = 270, NodePanelHeight = 156;

    /// <summary>Beside the node, clear of its handles: to the right, or to the left where the right is taken by the helm.</summary>
    Rect2? NodePanelBox(Maneuver n)
    {
        if (NodePoint(n) is not { } np) return null;
        // Out past the drawn path on the node's side, so the panel never sits over the orbit's markers
        // (Ap, Pe, the closest approach) a pilot may want to tap next; failing room there, the other side.
        var path = Game.SubForecast is { } fc ? PathAhead(fc).Select(x => x.At).Append(np).ToList() : new List<Vector2> { np };
        var (lo, hi) = (path.Min(p => p.X), path.Max(p => p.X));
        var right = Mathf.Max(np.X + HandleReach + 24, hi + 16);
        var left = Mathf.Min(np.X - HandleReach - 24, lo - 16) - NodePanelWidth;
        var (minX, maxX) = (NavRight + 8, Size.X - ShipWidth - 20 - NodePanelWidth);
        var outward = np.X >= (lo + hi) / 2 ? right : left;
        var other = outward == right ? left : right;
        // Zoomed in, the path runs off the screen and there is no outside of it: then beside the node.
        var beside = new[] { np.X + HandleReach + 24, np.X - HandleReach - 24 - NodePanelWidth };
        var x = new[] { outward, other }.Concat(beside).Cast<float?>().FirstOrDefault(c => c >= minX && c <= maxX)
            ?? Mathf.Clamp(np.X + HandleReach + 24, minX, Size.X - NodePanelWidth - 10);
        var y = Mathf.Clamp(np.Y - NodePanelHeight / 2, NavTop, ClusterTop - NodePanelHeight - 8);   // clear of the helm below
        return new Rect2(x, y, NodePanelWidth, NodePanelHeight);
    }

    /// <summary>
    /// A step of the node's panel: a round figure about a hundredth of the orbital speed where the node
    /// lies, so a step means as much around a moon as around a star.
    /// </summary>
    double NodeStep(Maneuver n)
    {
        var speed = StageOf(n) is { } s ? s.Velocity.Length : Game.V!.SubVelocity.Length;
        return Math.Pow(10, Math.Floor(Math.Log10(Math.Max(speed, 1e-9) * 0.01)));
    }

    public double NodeStepForTest => NodeStep(Game.Selected!);
    public Rect2? NodePanelForTest => Game.Selected is { } n ? NodePanelBox(n) : null;
    public Vector2? NodePointForTest(Maneuver n) => NodePoint(n);

    /// <summary>Where the + (or −) of an axis sits in the open node's panel.</summary>
    public Vector2 NodeNudgeForTest(int axis, bool plus)
    {
        var box = NodePanelBox(Game.Selected!)!.Value;
        return new Vector2(box.End.X - PanelPad - (plus ? 15 : 49), box.Position.Y + 48 + axis * 23 + 13);
    }

    Rect2 NodeDeleteBox(Rect2 panel) => new(panel.End.X - PanelPad - 64, panel.End.Y - 34, 64, 26);

    /// <summary>The node's own panel, as KSP opens on a node: when, how much, a step at a time along each axis, the next and previous node, and deleting it.</summary>
    void DrawNodePanel(Maneuver n)
    {
        if (NodePanelBox(n) is not { } box) return;
        var v = Game.V!;
        var ordered = v.Nodes.OrderBy(m => m.Time).ToList();
        var index = ordered.IndexOf(n);
        Panel(box, "", opaque: true);   // over the chart's labels and paths, which must not show through
        var x = box.Position.X + PanelPad;
        var y = box.Position.Y + 20;
        var left = n.Time - v.AbsoluteTime;
        Text(new Vector2(x, y), $"ノード {index + 1} / {ordered.Count}", StageColor(index + 1), 15);
        var when = left > 0 ? Instruments.Countdown(left) : "点火!";
        Text(new Vector2(box.End.X - PanelPad - F.GetStringSize(when, HorizontalAlignment.Left, -1, 14).X, y), when, left <= 0.3 ? Warn : Ink, 14);
        y += 20;
        var burn = Game.EngineAcceleration > 0 ? $"   噴射 {Instruments.Span(n.DeltaV / Game.EngineAcceleration)}" : "";
        Text(new Vector2(x, y), $"Δv {Instruments.Speed(n.DeltaV)}" + burn, Ink, 14);
        y += 8;
        var step = NodeStep(n) * (Input.IsKeyPressed(Key.Shift) ? 0.1 : 1);
        var axes = new (string Label, double Value, Color Col)[]
        {
            ("順行", n.Prograde, new Color(0.45f, 1f, 0.6f)), ("外", n.Radial, new Color(0.45f, 0.8f, 1f)), ("法線", n.Normal, new Color(1f, 0.5f, 0.9f)),
        };
        for (int i = 0; i < axes.Length; i++)
        {
            var (label, value, col) = axes[i];
            var axis = i;
            Text(new Vector2(x, y + 18), label, col, 13);
            Text(new Vector2(x + 44, y + 18), (value >= 0 ? "+" : "") + Instruments.Speed(value), Ink, 14);
            ActionButton(new Rect2(box.End.X - PanelPad - 64, y + 3, 30, 20), "−", () => Game.NudgeNode(axis, -step), col);
            ActionButton(new Rect2(box.End.X - PanelPad - 30, y + 3, 30, 20), "+", () => Game.NudgeNode(axis, step), col);
            y += 23;
        }
        var by = box.End.Y - 34;
        ActionButton(new Rect2(x, by, 34, 26), "◀", () => Game.StepNode(-1), Ink, ordered.Count > 1);
        ActionButton(new Rect2(x + 38, by, 34, 26), "▶", () => Game.StepNode(1), Ink, ordered.Count > 1);
        Text(new Vector2(x + 80, by + 18), "±" + Instruments.Speed(step), Dim, 11);
        ActionButton(NodeDeleteBox(box), "削除", () => { Game.SelectNodeQuietly(n); Game.DeleteNode(); }, Bad);
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

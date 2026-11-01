using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

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

    public void ZoomOrbit(float f) => ZoomMap(f);
    public void RotateOrbit(Vector2 d) => RotateMap(d);
    public void PanOrbit(Vector2 d) => PanMap(d);

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

    public void OrbitTap(Vector2 at)
    {
        var v = Game.V!;
        if (Game.HandlesOpen && Game.Selected is { } open && DeleteButton(open) is { } del && del.DistanceTo(at) < 14)
        {
            Game.SelectNodeQuietly(open);
            Game.DeleteNode();
            return;
        }
        foreach (var n in v.Nodes)
            if (NodePoint(n) is { } np && np.DistanceTo(at) < 14) { Game.SelectNode(n); return; }
        if (Game.SubForecast is { } fc && EarliestNear(fc, at, 14) is { } time) { Game.AddNode(time); return; }
        Game.HandlesOpen = false;
    }

    /// <summary>The time on the path nearest a screen point, on the first pass that comes within reach:
    /// where the path overlaps itself, a tap means the soonest one.</summary>
    double? EarliestNear(OrbitForecast fc, Vector2 at, float reach)
    {
        double? time = null;
        var closest = reach;
        foreach (var (point, when) in PathAhead(fc))
        {
            var d = point.DistanceTo(at);
            if (d < closest) { closest = d; time = when; }
            else if (time != null && d >= reach) break;
        }
        return time;
    }

    /// <summary>Screen position of the node's delete button, placed clear of the handles.</summary>
    public Vector2? DeleteButton(Maneuver n) => NodePoint(n) is { } np ? np + new Vector2(0, -HandleReach - 26) : null;

    const float HandleReach = 56f, DvPerPixel = 0.0005f;
    Maneuver? _heldNode;
    Vector2 _heldAxis;
    int _heldKind;   // 0 prograde, 1 radial, 2 normal, 3 slides along the path

    public bool OrbitHolding => _heldNode != null;

    (Vector2 Dir, Color Color, string Label, int Kind, float Sign, float Reach)[] Handles(Maneuver n)
    {
        if (StageOf(n) is not { } s) return Array.Empty<(Vector2, Color, string, int, float, float)>();
        var (t, r, nor) = Sublight.Frame(s.Position, s.Velocity);
        var map = LegMap(Game.SubForecast!.LegOf(n)!);
        var o = map(s.Position);
        var unit = 1.0 / Game.OrbitZoom;
        var list = new List<(Vector2, Color, string, int, float, float)>();
        void Add(Vec3d axis, Color c, string label, int kind, float sign)
        {
            var screen = map(s.Position + axis * unit) - o;
            if (screen.Length() < 0.2f) return;                  // pointing at the camera: unusable
            list.Add((screen.Normalized(), c, label, kind, sign, HandleReach * Mathf.Clamp(screen.Length(), 0.45f, 1f)));
        }
        var green = new Color(0.45f, 1f, 0.6f);
        var blue = new Color(0.45f, 0.8f, 1f);
        var magenta = new Color(1f, 0.5f, 0.9f);
        Add(t, green, "順行", 0, 1); Add(-t, green, "逆行", 0, -1);
        Add(r, blue, "外", 1, 1); Add(-r, blue, "内", 1, -1);
        Add(nor, magenta, "法線", 2, 1); Add(-nor, magenta, "反法線", 2, -1);
        return list.ToArray();
    }

    public void OrbitPress(Vector2 at)
    {
        if (!Game.HandlesOpen || Game.Selected is not { } n || NodePoint(n) is not { } np) return;
        foreach (var h in Handles(n))
            if ((np + h.Dir * h.Reach).DistanceTo(at) < 14)
            {
                _heldNode = n; _heldAxis = h.Dir * h.Sign; _heldKind = h.Kind;
                return;
            }
        if (np.DistanceTo(at) < 14) { _heldNode = n; _heldKind = 3; }
    }

    public void OrbitHoldMove(InputEventMouseMotion mm)
    {
        var n = _heldNode!;
        if (_heldKind == 3)
        {
            var fc = Game.SubForecast;
            if (fc == null) return;
            n.Time = PathAhead(fc).OrderBy(t => t.At.DistanceSquaredTo(mm.Position)).First().Time;
        }
        else
        {
            var scale = DvPerPixel * (Input.IsKeyPressed(Key.Shift) ? 0.1f : 1f);
            var change = mm.Relative.Dot(_heldAxis) * scale;
            if (_heldKind == 0) n.Prograde += change;
            else if (_heldKind == 1) n.Radial += change;
            else n.Normal += change;
        }
        Game.NodeEdited();
    }

    public void OrbitRelease() => _heldNode = null;
    public Vector2 HandleTip(int i) { var h = Handles(Game.Selected!)[i]; return NodePoint(Game.Selected!)!.Value + h.Dir * h.Reach; }
    public int HandleCount => Game.Selected != null ? Handles(Game.Selected).Length : 0;

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
        Text(c + new Vector2(10, -8), star.Name, Dim, 14);
        if (Game.OrbitZoom < 35)
        {
            // Zoomed out to the galaxy the system is one point, but where the ship is bound still reads.
            if (Game.SubForecast is { } leaving) DrawHandover(leaving, Warn);
            return;
        }
        DrawBodies();

        DrawLocalStation();
        if (Game.SubForecast is { } fc)
        {
            DrawPaths(fc);
            foreach (var (pos, asc) in fc.PlaneCrossings)
            {
                var p = OrbitPoint(pos);
                DrawArc(p, 5, 0, Mathf.Tau, 12, new Color(1f, 0.5f, 0.9f), 1.4f, true);
                Text(p + new Vector2(7, 4), asc ? "AN" : "DN", new Color(1f, 0.5f, 0.9f), 12);
            }
            if (v.LocalStation != null)
            {
                if (fc.Crossing is { } x)
                {
                    var (sh, sp) = (OrbitPoint(x.Ship), OrbitPoint(x.Station));
                    DrawCircle(sh, 3, new Color(1f, 0.8f, 0.4f));
                    DrawRect(new Rect2(sp - new Vector2(4, 4), 8, 8), new Color(Good, 0.55f));
                    Text(sp + new Vector2(8, 14), "到着時の港", new Color(Good, 0.8f), 12);
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
            if (rad * Game.OrbitZoom > 12)
                for (int i = 0; i < 96; i++)
                    DrawLine(StarPoint(around + (u * Math.Cos(i * Math.Tau / 96) + w * Math.Sin(i * Math.Tau / 96)) * rad),
                        StarPoint(around + (u * Math.Cos((i + 1) * Math.Tau / 96) + w * Math.Sin((i + 1) * Math.Tau / 96)) * rad), new Color(Dim, 0.3f), 1, true);
            var at = StarPoint(body.StarPosition(t));
            var col = body.Kind switch { BodyKind.Giant => new Color(0.95f, 0.8f, 0.55f), BodyKind.Moon => new Color(0.75f, 0.75f, 0.8f), _ => new Color(0.55f, 0.75f, 0.9f) };
            DrawCircle(at, Mathf.Max(body.Kind == BodyKind.Moon ? 2f : 3.5f, (float)body.BodyRadius * Game.OrbitZoom), col);
            if (body.Edge * Game.OrbitZoom > 10 || body.Kind != BodyKind.Moon) Text(at + new Vector2(7, 12), body.Name, new Color(col, 0.8f), 12);
        }
    }

    void DrawLocalStation()
    {
        var v = Game.V!;
        if (v.LocalStation is not { } st) return;
        var t = v.AbsoluteTime;
        var host = (st.Host ?? Game.G!.System(v.SystemStar)).StarPosition(t) - v.Primary.StarPosition(t);
        var (u, w, rad) = (st.U, st.W, st.Radius);
        Polyline3(a => host + (u * Math.Cos(a) + w * Math.Sin(a)) * rad, new Color(Good, 0.35f), 1);
        var sp = OrbitPoint(host + st.Position(t));
        DrawRect(new Rect2(sp - new Vector2(4, 4), 8, 8), Good);
        Text(sp + new Vector2(8, -6), v.SystemStar == v.TargetStar ? "目的港" : Game.G!.Ports.Contains(v.SystemStar) ? "港" : "補給港", Good, 13);
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
        var heading = OrbitPoint(v.SubPosition + v.SubVelocity.Normalized() * (1.0 / Game.OrbitZoom));
        if (heading.DistanceTo(me) <= 0.01f) return;
        var way = (heading - me).Normalized();
        DrawLine(me, me + way * 24, Ink, 2f, true);
        Chevron(me + way * 24, way, Ink, 8);
    }

    void DrawOrbitPanel(float x, float y)
    {
        var v = Game.V!;
        var el = Sublight.OrbitOf(v.Primary.Mu, v.SubPosition, v.SubVelocity);
        void Line(string s, Color c) { Text(new Vector2(x, y), s, c, 15); y += 20; }
        Line(el.Bound ? $"近点 {el.Periapsis:F2}  遠点 {el.Apoapsis:F2} AU" : "脱出軌道", el.Bound ? Ink : Warn);
        Line($"{(el.Bound ? $"周期 {el.Period:F1} 日   " : "")}{(Game.CurrentRate == 0 ? "一時停止 (P)" : $"時間 {Instruments.Rate(Game.CurrentRate)}")}", Dim);
        Line($"亜光速 Δv 残 {v.SublightDeltaV:F2}", Ink);
        var st = v.LocalStation;
        if (st != null)
        {
            var (stationAt, stationSpeed) = v.StationNow();
            var d = (stationAt - v.SubPosition).Length;
            var rel = (stationSpeed - v.SubVelocity).Length;
            Line($"主天体 {v.Primary.Name}", Dim);
            Line($"港まで {d:F3} AU   相対 {rel:F3}", d < Tuning.DockRange * 3 ? Good : Ink);
            var incl = Sublight.RelativeInclination(st, v.SubPosition, v.SubVelocity);
            var last = Game.SubForecast?.Stages.LastOrDefault();   // the plan within this star's reach
            var planned = last is { Node: not null } ls ? Sublight.RelativeInclination(st, ls.Position, ls.Velocity + Sublight.Impulse(ls.Position, ls.Velocity, ls.Node)) : incl;
            Line($"相対傾斜 {incl:F1}°" + (last is { Node: not null } ? $" → {planned:F1}°" : ""), planned < 1.5 ? Good : Warn);
            if (Game.SubForecast?.Closest is { } a)
                Line($"最接近 {a.Distance:F3} AU  {a.Time - v.AbsoluteTime:F1}日後  相対 {a.RelativeSpeed:F2}", a.Distance < 0.05 ? Good : Warn);
            if (Game.SubForecast?.Crossing is { } x2)
                Line($"交差時の港 {Math.Abs(x2.LeadDegrees):F0}° {(x2.LeadDegrees >= 0 ? "先" : "後ろ")}  {x2.Time - v.AbsoluteTime:F1}日後", Math.Abs(x2.LeadDegrees) < 8 ? Good : Warn);
            else Line("港の軌道半径に届いていない", Warn);
            Line($"港 半径 {st.Radius:F2} AU   入港 {Tuning.DockRange:F2} AU / 相対 {Tuning.DockRelativeSpeed:F2}", Dim);
        }
        if (Game.SubForecast?.Impact == true) Line("天体に衝突する軌道", Bad);
        y += 4;
        var fc = Game.SubForecast;
        var live = fc?.AllStages.Where(s => v.Nodes.Contains(s.Node)).ToList() ?? new List<Stage>();
        if (Game.SubCurrent is { } now && (live.Count > 0 || Game.EngineOn))
        {
            var e3 = now.After;
            var stop = e3.Bound ? $"近 {e3.Periapsis:F2} 遠 {e3.Apoapsis:F2}" : "脱出軌道";
            Line($"今止めると {stop}" + (now.Closest is { } ca ? $"  港まで {ca.Distance:F3}" : ""), Color.Color8(255, 255, 255));
        }
        if (Game.PlanDeviation() is { } dev)
            Line($"計画固定中  ずれ {dev:F3} AU   Y で再計画", dev > 0.05 ? Warn : Good);
        if (live.Count > 0)
        {
            var total = live.Sum(s => s.Node.DeltaV);
            Line($"計画 {live.Count} 段  Δv {total:F3} / 残 {v.SublightDeltaV:F2}", total > v.SublightDeltaV ? Bad : Ink);
            for (int i = 0; i < Math.Min(live.Count, 5); i++)
            {
                var s = live[i];
                var left = s.Node.Time - v.AbsoluteTime;
                var when = left > 0 ? $"{left:F1}日後" : "点火!";
                var orbit = s.After.Bound ? $"{s.After.Periapsis:F2}-{s.After.Apoapsis:F2}" : "脱出";
                Line($"{i + 1}. {when}  Δv {s.Node.DeltaV:F3}  → {orbit}", s.Node == Game.Selected ? Warn : left <= 0.3 ? Warn : Ink);
            }
        }
        if (Game.NextNode is { } burn)
        {
            Line($"ノード残 Δv {burn.DeltaV:F3}   スロットル {Game.Throttle * 100:F0}%", Ink);
            if (Game.EngineAcceleration > 0)
            {
                var days = burn.DeltaV / Game.EngineAcceleration;
                Line($"噴射 {days:F2} 日 (実時間 {days / Game.BurnTimeRate:F1} 秒)", days > 3 ? Warn : Good);
                var start = burn.Time - days / 2 - v.AbsoluteTime;
                Line(start > 0 ? $"点火推奨 {start:F2} 日後" : "点火推奨 今", start < 0.3 ? Warn : Ink);
            }
            else Line("スロットル0%: 噴射できない (I で上げる)", Warn);
            if (Game.NoseOffNode is { } off)
                Line($"機首と噴射方向のずれ {off:F1}°" + (off > 5 ? "  「噴」をタップ" : ""), off > 5 ? Warn : Good);
        }
        if (Game.Selected is { } n && v.Nodes.Contains(n)) Line($"順 {n.Prograde:+0.000;-0.000} 径 {n.Radial:+0.000;-0.000} 法 {n.Normal:+0.000;-0.000}", Warn);
        y += 4;
        Line($"計算機 {(Game.Owns(Modules.Transfer) ? "搭載" : "なし")}   自動噴射 {(Game.Owns(Modules.AutoBurn) ? (v.AutoBurn ? "ON" : "OFF") : "なし")}", Dim);
    }
}

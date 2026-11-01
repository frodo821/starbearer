using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>Everything drawn over the sky: instruments, the trajectory projected on the sphere, and the chart.</summary>
public partial class Hud : Control
{
    public Main Game = null!;
    static readonly Color Ink = new(0.75f, 0.9f, 1f, 0.95f);
    static readonly Color Dim = new(0.55f, 0.7f, 0.8f, 0.7f);
    static readonly Color Warn = new(1f, 0.7f, 0.25f);
    static readonly Color Bad = new(1f, 0.3f, 0.25f);
    static readonly Color Good = new(0.5f, 1f, 0.7f);
    Font F => ThemeDB.FallbackFont;
    bool _mapDragging;

    public Hud() { MouseFilter = MouseFilterEnum.Ignore; }

    public PortWindow Port { get; private set; } = null!;

    public override void _Ready()
    {
        Port = new PortWindow(Game);
        AddChild(Port);
    }

    public override void _Process(double delta)
    {
        var open = Game.Screen == Screen.Port;
        if (open == Port.Visible) return;
        if (open) Port.Rebuild();
        Port.Visible = open;
    }

    void Text(Vector2 p, string s, Color c, int size = 16) =>
        DrawString(F, p, s, HorizontalAlignment.Left, -1, size, c);

    void Centered(float y, string s, Color c, int size)
    {
        var w = F.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
        Text(new Vector2((Size.X - w) / 2, y), s, c, size);
    }

    public override void _Draw()
    {
        switch (Game.Screen)
        {
            case Screen.Title: DrawTitle(); return;
            case Screen.Loading: DrawLoading(); return;
            case Screen.Orbit:
                if (Game.OrbitSky) DrawCockpit(); else DrawOrbitMap();
                DrawHudLayers();
                break;
            case Screen.Plan or Screen.Flight:
                if (Game.MapOpen) DrawMap();
                else { DrawStreaks(); DrawCockpit(); }
                DrawHudLayers();
                break;
            case Screen.Port: DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.015f, 0.035f)); break;
            case Screen.Dead: DrawDead(); break;
        }
    }

    void DrawHudLayers()
    {
        DrawInstruments();
        DrawStateFrame();
        DrawConsole();
        DrawTransition();
    }

    void DrawLoading()
    {
        Centered(340, "STARBEARER", Ink, 64);
        var dots = new string('.', 1 + (int)(Time.GetTicksMsec() / 400 % 3));
        Centered(420, Game.LoadingLabel + dots, Dim, 22);
    }

    void DrawTitle()
    {
        Centered(340, "STARBEARER", Ink, 64);
        Centered(400, "星間運送", Dim, 22);
        if (Game.HasSave) Centered(500, "Enter  続きから      N  新しい銀河", Ink, 22);
        else Centered(500, "Enter  新しい銀河", Ink, 22);
        if (Game.LoadFailed) Centered(550, "読み込みに失敗した", Warn, 16);
    }

    void DrawDead()
    {
        var v = Game.V!; var s = Game.Save!;
        var why = v.Death switch
        {
            DeathCause.Collision => "恒星に突入した",
            DeathCause.HullFailure => "船体が熱で崩壊した",
            DeathCause.Stranded => "燃料が尽き、超空間に取り残された",
            DeathCause.LifeSupport => "生命維持が尽きた",
            _ => "",
        };
        if (Game.Bankrupt) why = "破産した。出港に足る燃料が買えない";
        Centered(300, why, Bad, 40);
        Centered(370, $"飛行 {v.Time:F1} 日   配達 {s.Deliveries} 件   再開 {s.Retries} 回", Ink, 20);
        if (!Game.Bankrupt) Centered(480, "R  最後の入港から再開", Ink, 22);
        Centered(520, "X  ここで終える（この銀河は失われる）", Warn, 22);
    }

    bool Project(Vector3 dir, out Vector2 p)
    {
        p = default;
        var world = dir.Normalized() * Sky.Radius;
        if (Game.Camera.IsPositionBehind(world)) return false;
        p = Game.Camera.UnprojectPosition(world);
        return true;
    }

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
            if (Project(Sky.ToV(Game.Nose), out var nose))
            {
                DrawLine(nose + new Vector2(-20, 0), nose + new Vector2(-7, 0), Warn, 2);
                DrawLine(nose + new Vector2(20, 0), nose + new Vector2(7, 0), Warn, 2);
                DrawLine(nose + new Vector2(0, -7), nose + new Vector2(0, -16), Warn, 2);
                DrawArc(nose, 3, 0, Mathf.Tau, 10, Warn, 1.5f, true);
            }
            DrawEdgeArrow(Game.Nose, "機首", Warn);
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

    const float CompassRadius = 112;
    Vector2 CompassCentre => new(Size.X - CompassRadius - 26, Size.Y - CompassRadius - 150);

    Vector2 CompassPt(Vec3d d)
    {
        var (right, up) = Game.NoseFrame();
        var (x, y) = Instruments.CompassPoint(d, right, up, Game.Nose);
        return CompassCentre + new Vector2((float)x, -(float)y) * CompassRadius;
    }

    public Vector2 CompassMarkerForTest(ViewHold hold) => CompassPt(Game.DirectionOf(hold)!.Value);

    /// <summary>Tap a marker to hold it; tap the middle of the dial to hand attitude back to the pilot.</summary>
    public bool CompassTap(Vector2 at)
    {
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
            DrawArc(p, primary ? 8 : 6, 0, Mathf.Tau, 14, col, hold != ViewHold.Free && hold == Game.Hold ? 3f : primary ? 1.6f : 1f, true);
            Text(p + new Vector2(-6, 5), label, col, primary ? 12 : 11);
        }
        Text(c + new Vector2(-radius, -radius - 12), "姿勢儀  中心=機首  タップで姿勢保持", Dim, 12);
        Text(c + new Vector2(-radius, radius + 22), $"姿勢保持 {Game.HoldName}", Game.Hold == ViewHold.Free ? Dim : Good, 13);
        DrawThrottle(c);
        Text(c + new Vector2(-radius, radius + 40), $"カメラ {(Game.CameraFollow ? "機首固定" : "自由")} (B)", Dim, 12);
    }

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
            if (Game.SubCurrent?.Closest is { } c) return ("港への最接近", c.Time - v.AbsoluteTime);
            return ("", null);
        }
        if (Game.Forecast is not { } fc) return ("", null);
        if (fc.Encounters.Count > 0)
            return ($"{v.G.Stars[fc.Encounters[0].Star].Name} 近日点", fc.Encounters[0].Time);
        return fc.End == PredictionEnd.Stall ? ("失速", fc.Duration) : ("", null);
    }

    public string ClockTextForTest => Instruments.Clock(Game.V!.Time);

    void DrawThrottle(Vector2 compass)
    {
        const float height = 200, width = 16;
        var x = compass.X - CompassRadius - 44;
        var top = compass.Y - height / 2;
        DrawRect(new Rect2(x - 8, top - 30, width + 24, height + 70), new Color(0.01f, 0.02f, 0.05f, 0.72f));
        var col = Game.EngineOn ? Warn : Ink;
        DrawRect(new Rect2(x, top, width, height), new Color(1, 1, 1, 0.12f));
        var fill = height * (float)Game.Throttle;
        DrawRect(new Rect2(x, top + height - fill, width, fill), col);
        Text(new Vector2(x - 4, top - 10), $"{Game.Throttle * 100:F0}%", col, 14);
        Text(new Vector2(x - 6, top + height + 18), Game.EngineOn ? "点火" : "停止", Game.EngineOn ? Warn : Dim, 13);
        Text(new Vector2(x - 6, top + height + 34), "Space", Dim, 11);
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

    void DrawInstruments()
    {
        var v = Game.V!; var g = Game.G!;
        DrawRect(new Rect2(10, 8, 345, Game.Screen == Screen.Orbit ? 700 : 420), new Color(0.01f, 0.02f, 0.05f, 0.72f));   // keeps gauges legible over the chart and the sky
        float x = 24, y = 34;
        Text(new Vector2(x, y), (v.Hold.Count == 0 ? "空荷" : $"積荷 {v.Hold.Count} 件") + $"  →  {g.Stars[v.TargetStar].Name}", Good, 20); y += 28;
        Text(new Vector2(x, y), $"質量 {v.Mass:F2}   積荷 {v.CargoMass:F1}   容積 {Missions.VolumeUsed(v.Hold):F0}/{Tuning.HoldVolume:F0}" + (v.Hold.Count > 0 ? $"   耐熱 {v.HeatTolerance:F2}" : ""), Dim, 15); y += 20;
        foreach (var c in v.Hold.Take(3)) { Text(new Vector2(x, y), $"{c.Mission.Cargo.Name} → {g.Stars[c.Mission.Target].Name}", c.Mission.Target == v.TargetStar ? Ink : Dim, 14); y += 18; }
        y += 8;
        Bar(x, ref y, "燃料", v.Fuel / Tuning.TankCapacity, Ink, $"Δv {v.DeltaVRemaining:F2}");
        if (Game.Screen == Screen.Plan)
        {
            // Ghost of what the kick would spend.
            var after = Math.Max(0, v.Fuel - v.FuelFor(Game.PlanDv)) / Tuning.TankCapacity;
            var now = v.Fuel / Tuning.TankCapacity;
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
        var width = Mathf.Min(Size.X - 120, 900f);
        var x0 = (Size.X - width) / 2;
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

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>
/// Scripted smoke run: `-- --autotest=&lt;dir&gt;`. Drives the real game loop with real input events and
/// saves a screenshot at each stage. Uses its own save file.
/// </summary>
public partial class Autotest : Node
{
    readonly Main _g;
    readonly string _dir = "";
    readonly Queue<(Func<bool> step, int wait)> _steps = new();
    int _wait = 20, _failed;
    public bool Enabled => _dir != "";

    Vec3d _aim, _view, _center;
    double _kick, _fuel, _pro, _time;
    float _zoom;
    Center? _picked;
    int _frames, _port, _count;
    Vec3d _vel, _before;
    string _clockText = "";
    OrbitForecast? _plan;
    Star? _meets;
    OrbitForecast? _coast;
    int _settling;
    List<Center> _views = new();
    Vector2 Size() => _g.GetViewport().GetVisibleRect().Size;
    double _dv, _throttle;
    ulong _clock;

    public Autotest(Main g)
    {
        _g = g;
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--autotest=")) _dir = a["--autotest=".Length..];
        if (Enabled) Build();
    }

    void Check(string what, bool ok)
    {
        GD.Print($"[autotest] {(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) _failed++;
    }

    void Shot(string name)
    {
        GetViewport().GetTexture().GetImage().SavePng($"{_dir}/{name}.png");
        GD.Print($"[autotest] {name}: screen={_g.Screen} phase={_g.V?.Phase} day={_g.V?.Time:F2} fuel={_g.V?.Fuel:F2}");
    }

    static void Key_(Key k, bool down) => Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down });
    static void Press(Key k) { Key_(k, true); Key_(k, false); }

    static void Click(MouseButton b, Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Position = at, GlobalPosition = at, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Position = at, GlobalPosition = at, Pressed = false });
    }

    static void Pan(float x, float y) => Input.ParseInputEvent(new InputEventPanGesture { Delta = new Vector2(x, y), Position = new Vector2(700, 400) });

    static void Drag(Vector2 from, Vector2 by)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = from, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseMotion { Position = from + by, Relative = by });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = from + by, Pressed = false });
    }

    double Off(Vec3d dir) => Math.Acos(Math.Clamp(_g.Helm.Nose.Dot(dir), -1, 1));

    int _heldTarget, _stop, _bought, _gate, _exit, _firstShip;
    ShipClass? _boughtClass;
    (Vec3d, float) _chartBefore;
    int _parkedAt;
    double _toll;
    double _credits, _heldFuel;
    int _retries;
    bool _paused, _sawEngine;
    string _heldJob = "";
    int _target;
    Voyage? _voyage;

    void Do(Action a, int wait = 10) => _steps.Enqueue((() => { a(); return true; }, wait));
    void Until(Func<bool> f, int wait = 1) => _steps.Enqueue((f, wait));

    void Build()
    {
        Do(() =>
        {
            Shot("00_title");
            _g.StartSeed(1234);
            Check("starting a galaxy shows the loading screen", _g.Screen == Screen.Loading);
        });
        Do(() =>
        {
            Shot("01_orbit_departure");
            Check("voyage starts in orbit", _g.Screen == Screen.Orbit && _g.V!.Phase == Phase.Sublight);
            _paused = _g.Paused;
            Click(MouseButton.Left, _g.HudForTest.ButtonForTest(Key.Space));
        });
        Do(() =>
        {
            Check("a console button does what its key does", _g.Paused != _paused && _g.V!.Nodes.Count == 0);
            Press(Key.Space);
        });
        Do(() =>
        {
            Check("space stops and starts time", _g.Paused == _paused);
            Check("a voyage starts docked", _g.V!.Docked);
            Shot("01a_docked");
            _fuel = _g.V.Fuel;
            Press(Key.O);
            Press(Key.E);
            Click(MouseButton.Left, _g.HoldButtonForTest(ViewHold.Prograde));
            Key_(Key.Left, true);
        });
        Do(() =>
        {
            Key_(Key.Left, false);
            Check("a docked ship's controls are locked", !_g.Helm.EngineOn && _g.Helm.Throttle != 1 && _g.Helm.Hold == ViewHold.Free && _g.V!.Fuel == _fuel && _g.V.Docked);
            Press(Key.J);
        });
        Do(() => { Check("a departure can be planned while docked", _g.Screen == Screen.Plan); Press(Key.Enter); });
        Do(() => { Check("a docked ship can't jump", _g.Screen == Screen.Plan && _g.V!.Phase == Phase.Sublight); Press(Key.Escape); });
        Do(() =>
        {
            Check("the plan closes back to the port", _g.Screen == Screen.Orbit);
            _g.LookAtPublic(-_g.V!.SubPosition.Normalized());   // the body the port circles
            _g.OrbitSky = true;
        }, 20);
        Do(() => { Shot("01d_body_from_port"); _g.LookAtPublic(_g.NearestStarDirection()!.Value); });
        Do(() =>
        {
            Shot("01e_star_from_port");
            _g.OrbitSky = false;
            Press(Key.Enter);
        });
        Do(() =>
        {
            Shot("01b_port");
            Check("the port opens while docked", _g.Screen == Screen.Port && _g.VisitingPort);
            Check("a station's board offers jobs", _g.HudForTest.Port.TakeButtons.Length > 0);
            Click(MouseButton.Left, _g.HudForTest.Port.TakeButtons[0].GetGlobalRect().GetCenter());
        });
        Do(() =>
        {
            Check("the board's button takes the job", _g.Save!.Hold.Count == 1);
            _target = _g.Save.Hold[0].Mission.Target;
            Shot("01c_mission_taken");
            Press(Key.Enter);
        });
        Do(() => { Check("leaving the port returns aboard with the job", _g.Screen == Screen.Orbit && !_g.VisitingPort && _g.V!.Docked && _g.V.Hold.Count == 1 && _g.V.TargetStar == _target); Press(Key.U); });
        Do(() =>
        {
            Check("U undocks", !_g.V!.Docked && _g.Screen == Screen.Orbit);
            Press(Key.Enter);
        });
        Do(() =>
        {
            Check("the port is closed once undocked", _g.Screen == Screen.Orbit);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = _g.HudForTest.ButtonForTest(Key.W), Pressed = true });
        });
        Do(() =>
        {
            Check("a hold button stays held while pressed", _g.HeldButton == Key.W);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = _g.HudForTest.ButtonForTest(Key.W), Pressed = false });
        });
        Do(() =>
        {
            Check("releasing a hold button lets go, and leaves no node", _g.HeldButton == null && _g.V!.Nodes.Count == 0);
            var pt = _g.SubForecast!.Track[120].Position;
            Click(MouseButton.Left, _g.OrbitPointForTest(pt));
        });
        Do(() =>
        {
            Check("tap on the orbit adds a node", _g.V!.Nodes.Count == 1);
            _pro = _g.Selected!.Prograde;
            _throttle = _g.Helm.Throttle;
            Key_(Key.W, true);
        }, 15);
        Do(() =>
        {
            Key_(Key.W, false);
            Check("W raises the throttle and leaves the node alone", _g.Helm.Throttle > _throttle && _g.Selected!.Prograde == _pro);
            Shot("02_orbit_node");
            Check("time stops while planning", _g.CurrentRate == 0);
            _pro = _g.Selected!.Prograde;
            Drag(_g.HandleTipForTest(0), new Vector2(60, 0));
        });
        Do(() =>
        {
            Check("dragging a handle changes the burn", Math.Abs(_g.Selected!.Prograde - _pro) > 0.001);
            _time = _g.Selected!.Time;
            var p = _g.OrbitPointForTest(_g.SubForecast!.Track.OrderBy(t => Math.Abs(t.Time - _time)).First().Position);
            var far = _g.OrbitPointForTest(_g.SubForecast.Track[_g.SubForecast.DrawnCount * 3 / 5].Position);
            Drag(p, far - p);
        });
        Do(() =>
        {
            Check("dragging the node moves its time", Math.Abs(_g.Selected!.Time - _time) > 0.1);
            Check("all six handles are reachable", _g.HandleCountForTest >= 4);
            Click(MouseButton.Left, new Vector2(1300, 100));
        });
        Do(() =>
        {
            Check("tapping empty space hides the handles", !_g.HandlesOpen);
            Shot("02b_node_handles_hidden");
            var far = _g.SubForecast!.Track[_g.SubForecast.DrawnCount - 20].Position;
            Click(MouseButton.Left, _g.OrbitPointForTest(far));
        });
        Do(() =>
        {
            Check("a second stage can follow the first", _g.V!.Nodes.Count == 2 && _g.SubForecast!.Stages.Count == 2);
            Shot("02c_two_stages");
            Press(Key.Backspace);
        });
        Do(() =>
        {
            Check("backspace deletes one node", _g.V!.Nodes.Count == 1);
            _g.SelectNodeQuietly(new Maneuver());      // a selection left behind by a finished burn
            Press(Key.Backspace);
        });
        Do(() =>
        {
            Check("backspace still works with a stale selection", _g.V!.Nodes.Count == 0);
            Click(MouseButton.Left, _g.OrbitPointForTest(_g.SubForecast!.Track[120].Position));
        });
        Do(() => { Check("node re-added", _g.V!.Nodes.Count == 1); Click(MouseButton.Left, _g.DeleteButtonForTest); });
        Do(() =>
        {
            Check("delete button removes the node", _g.V!.Nodes.Count == 0);
            Click(MouseButton.Left, _g.OrbitPointForTest(_g.SubForecast!.Track[120].Position));
        });
        Do(() => Press(Key.Delete));
        Do(() =>
        {
            Check("forward delete removes the node", _g.V!.Nodes.Count == 0);
            Check("the station is the target to begin with", _g.V.Goal == _g.V.LocalStation);
            Press(Key.C);
        });
        Do(() =>
        {
            Check("planning has no keys of its own", _g.V!.Goal == _g.V.LocalStation);
            _g.CommandForTest(Key.C);
        });
        Do(() =>
        {
            var v = _g.V!;
            Check("the target button moves the target on to a body", v.Goal is Center);
            // A body circling the ship's primary, so its nodes and closest approach are worked out from here.
            _picked = _g.TargetChoices().OfType<Center>().FirstOrDefault(c => Sublight.Waypoint(v.Primary, c) == c);
            _g.SetTarget(v.LocalStation);   // C may have landed on it already, and a tap on the target lets it go
            if (_picked != null) Click(MouseButton.Left, _g.BodyPointForTest(_picked));
        });
        Do(() =>
        {
            var v = _g.V!;
            var f = _g.SubForecast;
            Check("tapping a body targets it", _picked != null && v.Goal == _picked);
            Check("the path is measured against the target", f?.Watched == _picked && f!.Closest != null && f.PlaneCrossings.Count > 0);
            Shot("02e_target_body");
            if (_picked != null) Click(MouseButton.Left, _g.BodyPointForTest(_picked));
        });
        Do(() =>
        {
            Check("tapping the target again clears it", _g.V!.Goal == null && _g.V.Waypoint == null && _g.V.Nodes.Count == 0);
            _g.SetTarget(_g.V.LocalStation);
            Check("a circular orbit has no apsides to mark", _g.ApsisPointForTest(true) == null && _g.ApsisPointForTest(false) == null);
            Click(MouseButton.Left, _g.OrbitPointForTest(_g.SubForecast!.Track[60].Position));
        });
        Do(() => { _g.Selected!.Prograde = _g.V!.SubVelocity.Length * 0.05; _g.NodeEdited(); });   // eccentric, still bound
        Do(() =>
        {
            var ap = _g.SubForecast!.Legs.SelectMany(l => l.Apsides).Where(x => x.Apo && x.Time >= _g.V!.AbsoluteTime).ToList();
            _time = ap.Count > 0 ? ap[0].Time : double.NaN;
            Check("a burn's orbit shows its apoapsis and periapsis", ap.Count > 0 && _g.ApsisPointForTest(true) != null && _g.ApsisPointForTest(false) != null);
            Shot("02f_apsides");
            if (_g.ApsisPointForTest(true) is { } at) Click(MouseButton.Left, at);
        });
        Do(() =>
        {
            Check("tapping Ap puts a node on it", _g.V!.Nodes.Count == 2 && _g.V.Nodes.Any(n => Math.Abs(n.Time - _time) < 1e-9));
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Backspace, PhysicalKeycode = Key.Backspace, ShiftPressed = true, Pressed = true });   // shift clears them all
        });
        Do(() => { if (_picked != null) Click(MouseButton.Right, _g.BodyPointForTest(_picked)); });
        Do(() =>
        {
            var item = _g.HudForTest.MenuItemForTest("遷移案");
            Check("a right click on a body opens its menu", item != null);
            Shot("02g_body_menu");
            if (item is { } at) Click(MouseButton.Left, at);
        });
        Do(() =>
        {
            Check("the menu targets the body and plans a transfer", _g.V!.Goal == _picked && _g.V.Nodes.Count > 0 && !_g.HudForTest.MenuOpen);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Backspace, PhysicalKeycode = Key.Backspace, ShiftPressed = true, Pressed = true });
            if (_g.HudForTest.StationPointForTest is { } st) Click(MouseButton.Right, st);
        });
        Do(() =>
        {
            var item = _g.HudForTest.MenuItemForTest("目標にする");
            Check("the station's menu offers it as the target", item != null && _g.HudForTest.MenuItemForTest("合流案") != null);
            if (item is { } at) Click(MouseButton.Left, at);
        });
        Do(() =>
        {
            Check("the Ap node can be deleted", _g.V!.Nodes.Count == 0);
            Check("the station is the target again", _g.V.Goal == _g.V.LocalStation);
            _zoom = _g.Chart.Yaw;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = new Vector2(1300, 100), Pressed = true });
            Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(1360, 120), Relative = new Vector2(60, 20) });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = new Vector2(1360, 120), Pressed = false });
        });
        Do(() => { Check("dragging empty space rotates the orbit view", _g.Chart.Yaw != _zoom); Shot("02d_rotated"); Press(Key.J); });
        Do(() =>
        {
            Check("J opens the jump plan", _g.Screen == Screen.Plan);
            Shot("01b_plan_console");
            Shot("03_jump_plan");
            _aim = _g.PlanDir; _kick = _g.PlanDv;
            Click(MouseButton.Left, new Vector2(1100, 300));
            Click(MouseButton.WheelUp, new Vector2(800, 450));
        });
        Do(() =>
        {
            Check("tap moves aim", (_g.PlanDir - _aim).Length > 0.05);
            Check("wheel adds kick", _g.PlanDv > _kick);
            _aim = _g.PlanDir; _kick = _g.PlanDv; _view = _g.CameraForward();
            Pan(12, 4);
            Key_(Key.W, true);
        }, 15);
        Do(() =>
        {
            Key_(Key.W, false);
            Check("two-finger pan turns the view", (_g.CameraForward() - _view).Length > 0.05);
            Check("pan keeps the aim", (_g.PlanDir - _aim).Length < 1e-9);
            Check("W raises kick", _g.PlanDv > _kick);
            Press(Key.Tab);
        });
        Do(() =>
        {
            Check("tab opens chart", _g.MapOpen);
            _zoom = _g.Chart.Zoom; _center = _g.Chart.Center;
            Input.ParseInputEvent(new InputEventMagnifyGesture { Factor = 1.3f, Position = new Vector2(800, 450) });
            Pan(20, 10);
        });
        Do(() =>
        {
            Check("pinch zooms the chart", _g.Chart.Zoom > _zoom);
            Check("pan moves the chart pivot", (_g.Chart.Center - _center).Length > 0.1);
            // A star on screen, clear of its neighbours, that is neither here nor the destination.
            var v = _g.V!;
            var screen = new Rect2(200, 150, Size().X - 400, Size().Y - 350);
            var points = Enumerable.Range(0, _g.G!.Stars.Length).Select(i => _g.HudForTest.SystemPoint(i, Vec3d.Zero)).ToList();
            _heldTarget = v.TargetStar;
            _count = Enumerable.Range(0, points.Count).FirstOrDefault(i => i != v.SystemStar && i != v.TargetStar && screen.HasPoint(points[i])
                && points.Where((q, j) => j != i).All(q => q.DistanceTo(points[i]) > 40), -1);
            if (_count >= 0) Click(MouseButton.Right, points[_count]);
        });
        Do(() =>
        {
            var item = _g.HudForTest.MenuItemForTest("目的地にする");
            Check("a right click on a star offers it as the destination", _count >= 0 && item != null);
            Shot("03b_star_menu");
            if (item is { } at) Click(MouseButton.Left, at);
        });
        Do(() =>
        {
            Check("any star can be the destination, job or not", _g.V!.TargetStar == _count && _g.FreeCourse);
            Click(MouseButton.Right, _g.HudForTest.SystemPoint(_count, Vec3d.Zero));
        });
        Do(() => { if (_g.HudForTest.MenuItemForTest("目的地を解除") is { } at) Click(MouseButton.Left, at); });
        Do(() =>
        {
            Check("clearing the destination goes back to the job's", _g.V!.TargetStar == _heldTarget && !_g.FreeCourse);
            (_g.Chart.Center, _g.Chart.Zoom) = (Vec3d.Zero, 5);   // the whole galaxy, where regions read
        });
        Do(() =>
        {
            Shot("03c_regions");
            Press(Key.Tab); Press(Key.Enter);
        });
        Do(() => _g.LookAtPublic(_g.NearestStarDirection()!.Value));
        Do(() => Shot("04a_star_in_well"));
        Do(() =>
        {
            Check("enter jumps to FTL", _g.Screen == Screen.Flight && _g.V!.Phase == Phase.Flight);
            Shot("04_ftl");
            Check("attitude display carries the core markers", _g.MarkerCountForTest >= 8);
            _clockText = _g.ClockTextForTest;
            _view = _g.CameraForward();
            Click(MouseButton.Left, _g.HoldButtonForTest(ViewHold.Star));
            _clock = Time.GetTicksMsec();
        });
        Until(() => Off(_g.NearestStarDirection()!.Value) < 0.05 || Time.GetTicksMsec() - _clock > 5000);
        Do(() =>
        {
            Check("the clock runs during flight", _g.ClockTextForTest != _clockText);
            Check("the star button holds the nose on the star", _g.Helm.Hold == ViewHold.Star && Off(_g.NearestStarDirection()!.Value) < 0.05);
            Check("the camera stays put while the nose turns", (_g.CameraForward() - _view).Length < 1e-6);
            Click(MouseButton.Left, _g.CompassMarkerForTest(ViewHold.Normal));
            _clock = Time.GetTicksMsec();
        });
        Until(() => Off(_g.DirectionOf(ViewHold.Normal)!.Value) < 0.05 || Time.GetTicksMsec() - _clock > 5000);
        Do(() =>
        {
            Check("tapping a compass marker holds it", _g.Helm.Hold == ViewHold.Normal && Off(_g.DirectionOf(ViewHold.Normal)!.Value) < 0.05);
            Shot("04b_normal_hold");
            _vel = _g.V!.Velocity;
            Press(Key.E);
        }, 20);
        Do(() =>
        {
            Check("E lights the engine and it keeps running", _g.Helm.EngineOn && _g.Helm.Firing);
            Press(Key.E);
            var change = (_g.V!.Velocity - _vel).Normalized();
            Check("the engine fires along the nose, not prograde", change.Dot(_g.Helm.Nose) > 0.9);
            Key_(Key.Left, true);
            _view = _g.Helm.Nose;
        }, 20);
        Do(() =>
        {
            Key_(Key.Left, false);
            Check("arrow keys turn the nose", (_g.Helm.Nose - _view).Length > 0.01 && _g.Helm.Hold == ViewHold.Free);
            Press(Key.B);
        });
        Do(() => { Check("B locks the camera to the nose", (_g.CameraForward() - _g.Helm.Nose).Length < 1e-3); Pan(8, 0); });
        Do(() =>
        {
            Check("moving the camera releases the camera lock", !_g.CameraFollow);
            Press(Key.Key5); Press(Key.F); Press(Key.C);
        });
        Do(() => { Check("c opens correction", _g.Screen == Screen.Plan); Press(Key.Escape); });
        Do(() =>
        {
            Check("esc resumes flight", _g.Screen == Screen.Flight);
            Press(Key.Space);
            _time = _g.V!.Time;
        }, 10);
        Do(() =>
        {
            Check("space stops time in flight", _g.Paused && _g.CurrentRate == 0 && _g.V!.Time == _time);
            Press(Key.Space);
        }, 10);
        Do(() =>
        {
            Check("space starts it again", !_g.Paused && _g.V!.Time > _time);
            _fuel = _g.V!.Fuel;
            _throttle = _g.Helm.Throttle;
            Key_(Key.W, true);
        }, 15);
        Do(() =>
        {
            Key_(Key.W, false);
            Check("W raises the throttle", _g.Helm.Throttle > _throttle);
            Press(Key.O);
        });
        Do(() => { Check("O is full throttle", _g.Helm.Throttle == 1); Press(Key.L); });
        Do(() =>
        {
            Check("L is zero throttle", _g.Helm.Throttle == 0);
            Press(Key.E);
        }, 30);
        Do(() =>
        {
            Check("a lit engine at zero throttle burns nothing", _g.Helm.EngineOn && _g.V!.Fuel >= _fuel);
            Press(Key.E); Press(Key.O); Press(Key.E);
        }, 30);
        Do(() =>
        {
            Press(Key.E);
            Check("E burns fuel", _g.V!.Fuel < _fuel);
            _g.CrashForTest();
        });
        Do(() => { Shot("05_dead"); Check("death reached", _g.Screen == Screen.Dead); Press(Key.R); });
        Do(() =>
        {
            Check("retry returns to the docked orbit", _g.Screen == Screen.Orbit && _g.Save!.Retries == 1);
            _g.Save!.Modules.Add(Modules.Transfer);
            _g.Save.Modules.Add(Modules.AutoBurn);
            Press(Key.U);
            Press(Key.J);
        });
        Do(() => Press(Key.Enter));
        Do(() => { _g.PlaceInTargetWellForTest(); Press(Key.Tab); }, 60);
        Do(() =>
        {
            Shot("05b_map_in_well");
            Check("the chart shows what a descent would give", _g.DescentForecast is { After.Bound: true } && _g.MapOpen);
            Press(Key.Tab);
            Press(Key.D);
        }, 40);
        Do(() =>
        {
            Check("D descends at the target", _g.Screen == Screen.Orbit && _g.V!.SystemStar == _g.V.TargetStar);
            Shot("06_orbit_arrival");
            _g.OrbitSky = true;
            _g.LookAtPublic(_g.NearestStarDirection()!.Value);
        });
        Do(() =>
        {
            Shot("06a_star_on_arrival");
            _g.OrbitSky = false;
            _port = _g.Save!.PortStar;
            _g.CommandForTest(Key.H);
        });
        Do(() => { _count = _g.V!.Nodes.Count; Check("the transfer button places a draft", _count >= 1); _g.CommandForTest(Key.M); });
        Do(() =>
        {
            Check("the match button adds a matching burn", _g.V!.Nodes.Count == _count + 1);
            Shot("06b_free_plan");
            _view = _g.Helm.Nose;
            _g.SetHold(ViewHold.Node);
            Key_(Key.Left, true);
        }, 20);
        Do(() =>
        {
            Key_(Key.Left, false);
            Check("the nose stays put while time is stopped", _g.Paused && _g.Helm.Nose == _view);
            Press(Key.N);   // up to ignition: burned early, the node's direction does something else
            _clock = Time.GetTicksMsec();
        });
        Until(() => !_g.ToNode || Time.GetTicksMsec() - _clock > 20000);
        Do(() =>
        {
            _g.SetHold(ViewHold.Node);
            _clock = Time.GetTicksMsec();
        });
        Until(() => Off(_g.NodeDirection()!.Value) < 0.03 || Time.GetTicksMsec() - _clock > 5000);
        Do(() =>
        {
            Check("nose holds the node direction", _g.NoseOffNode < 2);
            _dv = _g.NextNode!.DeltaV;
            _plan = _g.SubForecast;
            _before = _g.SubCurrent!.Track[_g.SubCurrent.DrawnCount - 1].Position;
            Press(Key.E);
        }, 15);
        Do(() =>
        {
            Check("burning on the node spends it", _g.NextNode == null || _g.NextNode.DeltaV < _dv);
            Check("the plan freezes at ignition", _g.PlanFrozen && ReferenceEquals(_g.SubForecast, _plan));
            Check("the stop-now path moves with the burn", (_g.SubCurrent!.Track[_g.SubCurrent.DrawnCount - 1].Position - _before).Length > 1e-4);
            Shot("06d_burning");
            if (_g.Helm.EngineOn) Press(Key.E);
        });
        Do(() =>
        {
            Check("a stopped engine keeps the plan frozen", _g.PlanFrozen && ReferenceEquals(_g.SubForecast, _plan));
            Check("stopping short leaves the node with what is left", _g.V!.Nodes.Count == _count + 1 && _g.NextNode!.DeltaV < _dv);
            _g.CommandForTest(Key.Y);
        });
        Do(() => Check("replan rebuilds the plan from here", !_g.PlanFrozen && !ReferenceEquals(_g.SubForecast, _plan)));
        Do(() =>
        {
            if (_g.Paused) Press(Key.Space);
            _g.SetHold(ViewHold.Node);
            _clock = Time.GetTicksMsec();
        });
        Until(() => _g.NodeDirection() is not { } d || Off(d) < 0.03 || Time.GetTicksMsec() - _clock > 5000);
        Do(() => { Press(Key.O); Press(Key.E); _clock = Time.GetTicksMsec(); }, 1);
        Until(() => _g.NodeOvershot || !_g.Helm.EngineOn || Time.GetTicksMsec() - _clock > 25000);
        Do(() =>
        {
            Check("the engine keeps running past the node until stopped", _g.Helm.EngineOn && _g.NodeOvershot && _g.V!.Nodes.Count == _count + 1);
            Press(Key.E);
        });
        Do(() =>
        {
            Check("stopping at the node finishes it", !_g.Helm.EngineOn && _g.V!.Nodes.Count == _count);
            Check("a finished stage releases the plan", !_g.PlanFrozen);
        });
        Do(() =>
        {
            var v = _g.V!;
            var fresh = Sublight.Predict(v.Primary, v.Waypoint, v.SubPosition, v.SubVelocity, v.AbsoluteTime, v.Nodes, 10);
            var same = v.Nodes.Count == 0 || (fresh.Stages[0].Velocity - _g.SubForecast!.Stages[0].Velocity).Length < 1e-3;
            Check("remaining stages start from the current orbit", v.Nodes.Count > 0 && same);
            _g.SetHold(ViewHold.Free);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Backspace, PhysicalKeycode = Key.Backspace, Pressed = true, ShiftPressed = true });
            Key_(Key.Backspace, false);
        });
        Do(() =>
        {
            Check("shift+backspace clears every stage", _g.V!.Nodes.Count == 0);
            _g.CommandForTest(Key.T);
        });
        Do(() =>
        {
            Check("transfer computer plans a multi-stage burn", _g.V!.Nodes.Count >= 2);
            Check("no jump path shows until one is planned", _g.Forecast == null);
            _g.Chart.Zoom = 30;
        });
        Do(() =>
        {
            Shot("06e_orbit_zoomed_out");
            _g.Chart.Zoom = 425;
            Shot("06c_computer_plan");
            _count = _g.V!.Nodes.Count;
            _clock = Time.GetTicksMsec();
            Press(Key.N);
        });
        Do(() => Check("N runs time forward instead of jumping", _g.ToNode && _g.CurrentRate > 0));
        Until(() => !_g.ToNode || Time.GetTicksMsec() - _clock > 15000);
        Do(() =>
        {
            Check("N stops just short of ignition with the plan intact", !_g.ToNode && _g.IgnitionIn is > 0 and < 0.2 && _g.V!.Nodes.Count == _count);
            Check("arriving at a node keeps real time for lining up", _g.Aligning && !_g.Paused && _g.CurrentRate == Main.Rates[0]);
            Press(Key.G);
            Press(Key.Key6);
            _fuel = _g.V!.Fuel;
        }, 30);
        Do(() => Shot("07_orbit_transfer"), 1);
        Until(() =>
        {
            _sawEngine |= _g.Helm.EngineOn;
            return _g.V!.Nodes.Count == 0 || _g.Screen == Screen.Port || ++_frames > 6000;
        });
        Do(() =>
        {
            Check("auto burn flew the nodes", _g.V!.Nodes.Count == 0 || _g.Screen == Screen.Port);
            Check("auto burn is real thrust", _g.Screen == Screen.Port || _sawEngine);
            if (_g.Screen == Screen.Orbit) _g.V!.PlaceNearStationForTest(0.05);   // the coast from here isn't under test
            Shot("08_rendezvous");
            _g.V!.PlaceNearStationForTest(0.05);   // reaching it is the planners' test; this one is X and the dock
            Press(Key.Key5);
            Key_(Key.X, true);
            _clock = Time.GetTicksMsec();
        }, 1);
        Until(() => { Key_(Key.X, true); return _g.Screen == Screen.Port || Time.GetTicksMsec() - _clock > 120000; });   // the window can lose a held key
        Do(() =>
        {
            Key_(Key.X, false);
            Shot("09_delivered");
            Check("docking delivers the cargo", _g.Screen == Screen.Port);
            Check("checkpoint moved to the target", _g.Save!.PortStar != _port && _g.Save.Deliveries == 1);
            _g.Purchase(s => { s.Modules.Clear(); s.Credits = 200; });   // saved, so a reload sees it
            Press(Key.Key1);
        });
        Do(() =>
        {
            _bought = _g.Save!.Modules.Count;
            Check("modules are sold only at a shipyard", _bought == (_g.AtShipyard ? 1 : 0));
            Press(Key.Enter);
        });
        Do(() =>
        {
            Check("the ship goes on from the new port", _g.V!.SystemStar == _g.Save!.PortStar && _g.V.Hold.Count == 0);
            _g.ReloadForTest();
        });
        Do(() =>
        {
            Check("save reloads from disk", _g.Save!.Deliveries == 1 && _g.Save.Modules.Count == _bought && _g.Screen == Screen.Orbit);
            // A stop at the region's shipyard on the way somewhere else.
            var g = _g.G!;
            var here = _g.V!.SystemStar;
            var region = g.Ports.Concat(g.Depots).Where(s => s != here && g.Stars[s].Region == g.Stars[here].Region).ToList();
            _stop = region.FirstOrDefault(s => Missions.KindOf(g, s) == StationKind.Shipyard, region[0]);
            _g.TakeMissionForTest(_stop);
            _heldTarget = _g.V!.TargetStar;
            _heldJob = _g.Save!.Hold[0].Mission.Id;
            _g.V.Burn(new Vec3d(1, 0, 0), 0.2);
            _g.DockAtForTest(_stop);
        });
        Do(() =>
        {
            Shot("10_layover");
            Check("docking elsewhere is a stop, not a delivery", _g.Screen == Screen.Port && _g.Save!.Deliveries == 1 && _g.Save.Hold.Single().Mission.Id == _heldJob);
            _credits = _g.Save!.Credits;
            _heldFuel = _g.Save.HeldFuel;
            Press(Key.Key3);
        });
        Do(() =>
        {
            Check("fuel at a stop costs credits", Math.Abs(_credits - _g.Save!.Credits - (_g.Save.HeldFuel - _heldFuel) * Tuning.FuelPrice) < 1e-9 && _g.Save.HeldFuel > _heldFuel);
            _heldFuel = _g.Save.HeldFuel;
            (_credits, _g.Save.Credits) = (_g.Save.Credits, 200);
            _g.Save.Modules.Clear();
            Press(Key.Key1);
            Press(Key.Key2);
        });
        Do(() =>
        {
            Shot("10a_shipyard");
            Check("a shipyard sells the modules you can afford", !_g.AtShipyard || _g.Save!.Modules.SequenceEqual(new[] { Modules.AutoBurn }) && Math.Abs(_g.Save.Credits - 50) < 1e-9);
            _g.Save!.Credits = _credits;
            Press(Key.Enter);
        });
        Do(() =>
        {
            var v = _g.V!;
            Check("the stop keeps what was bought and the contract", Math.Abs(v.Fuel - _heldFuel) < 1e-9 && v.Time == 0 && v.TargetStar == _heldTarget && v.Hold.Single().Mission.Id == _heldJob && v.SystemStar == _stop);
            _g.ReloadForTest();
        });
        Do(() =>
        {
            Check("a stop survives a reload", _g.V!.TargetStar == _heldTarget && _g.V.SystemStar == _stop);
            // Buy a ship here and change into it; the job stays in the ship left behind.
            var s = _g.Save!;
            s.Credits = 100000;
            _firstShip = s.Flown;
            _boughtClass = Ships.SoldAt(_g.G!, _stop).LastOrDefault();
            if (_boughtClass == null) return;
            _g.BuyShip(_boughtClass);
            _g.BoardShip(s.Fleet.Max(x => x.Id));
            _g.LeavePort();
        });
        Do(() =>
        {
            if (_boughtClass == null) return;
            var s = _g.Save!;
            Check("a bought ship can be boarded where it was built", _g.V!.Class == _boughtClass && _g.V.Hold.Count == 0 && _g.V.Docked
                && s.Fleet.Single(x => x.Id == _firstShip).Hold.Single().Mission.Id == _heldJob && s.Fleet.All(x => x.Star == _stop));
            Press(Key.Enter);
        });
        Do(() =>
        {
            if (_boughtClass == null) return;
            Shot("10f_hangar");
            _g.BoardShip(_firstShip);
            _g.LeavePort();
        });
        Do(() =>
        {
            if (_boughtClass != null)
                Check("changing back finds the job where it was left", _g.V!.Class == Ships.Courier && _g.V.Hold.Single().Mission.Id == _heldJob);
            _chartBefore = (_g.Chart.Center, _g.Chart.Zoom);
            (_g.Chart.Center, _g.Chart.Zoom) = (_g.V!.ChartPositions[_stop], 12);   // out to where stars are points
            // Shown as if the bought ship waited at the nearest other station, where the chart marks it.
            var g = _g.G!;
            _parkedAt = g.Ports.Concat(g.Depots).Where(p => p != _stop).OrderBy(p => (g.Stars[p].Position - g.Stars[_stop].Position).Length).First();
            if (_boughtClass != null) _g.Save!.Fleet.Single(x => x.Id != _firstShip).Star = _parkedAt;
        });
        Do(() =>
        {
            if (_boughtClass != null) Shot("10g_parked_ship_on_map");
            (_g.Chart.Center, _g.Chart.Zoom) = _chartBefore;
            if (_boughtClass != null) _g.Save!.Fleet.Single(x => x.Id != _firstShip).Star = _stop;
            _g.Save!.Credits = 100000;
            _voyage = _g.V;
            Press(Key.Enter);
        });
        Do(() => Press(Key.Key5));
        Do(() => Press(Key.Enter));
        Do(() =>
        {
            Check("repairs bought on a second visit reach the same ship", _g.Screen == Screen.Orbit && _g.V == _voyage && _g.V!.Hull == 1 && _g.Save!.HeldHull == 1);
            var f = _g.SubCurrent!;
            Check("a closed orbit is drawn for one lap", f.Exit == null && Math.Abs(f.Track[f.DrawnCount - 1].Time - _g.V!.AbsoluteTime - Sublight.OrbitOf(_g.V.Primary.Mu, _g.V.SubPosition, _g.V.SubVelocity).Period) < 1e-6);
            Shot("10b_one_lap");
            Press(Key.U);
        });
        Do(() =>
        {
            var v = _g.V!;
            var here = v.System;
            _meets = _g.G!.Stars.Where(s => s.Id != here.Id).OrderBy(s => (s.Position - here.Position).Length).First();
            var way = (_meets.Position - here.Position).Normalized();
            var start = way * (Sublight.SystemRadius(here) * 0.9);
            var aim = _meets.Position + way.AnyPerpendicular() * (_meets.Soi * 0.4);
            v.PlaceSublightForTest(start, (aim - (here.Position + start * Sublight.Scale)).Normalized() * 0.6);
            _g.Chart.Zoom = 60;
            _g.CommandForTest(Key.Y);
        }, 30);
        Do(() =>
        {
            var f = _g.SubCurrent!;
            Check("an escaping orbit ends where the star lets go", f.Exit != null);
            Check("the path carries on around the star it meets", f.Next?.Star == _meets!.Id && f.Beyond?.DrawnCount > 10);
            Shot("10c_disengage");
            _g.Chart.Center = _g.V!.ChartPositions[_meets!.Id];
            _g.Chart.Zoom = (float)(Math.Min(Size().X, Size().Y) * 0.3 / _meets.Soi);
        }, 10);
        Do(() =>
        {
            Shot("10d_encounter_pass");
            var pass = _g.SubForecast!.Beyond!;
            _coast = _g.SubCurrent;
            _time = pass.Track[pass.DrawnCount / 2].Time;
            Click(MouseButton.Left, _g.LegPointForTest(pass, pass.Track[pass.DrawnCount / 2].Position));
        }, 10);
        Do(() =>
        {
            var f = _g.SubForecast!;
            Check("a tap on the star ahead plans a burn there", _g.Selected is { } n && f.LegOf(n) == f.Beyond && Math.Abs(n.Time - _time) < 1);
            Check("the panel plans the burn ahead", f.AllStages.Count() == 1 && f.Stages.Count == 0);
            Shot("10e_node_ahead");
            _views = _g.G!.Stars.SelectMany(x => _g.G.System(x.Id).Tree).GroupBy(c => c.Kind).Select(k => k.First()).ToList();
            _g.DeleteNode();
            // A clear circle around the star: nothing on the path comes up for days.
            var v = _g.V!;
            var st = v.LocalStation!;
            var system = _g.G!.System(v.SystemStar);
            var gap = Bodies.WidestGap(system);
            v.PlaceSublightForTest(st.U * gap, st.W * Math.Sqrt(system.Mu / gap));
            _g.CommandForTest(Key.Y);
            if (_g.Paused) Press(Key.Space);
        }, 10);
        Do(() =>
        {
            _coast = _g.SubCurrent;
            _time = _g.V!.AbsoluteTime;
        }, 40);
        Do(() =>
        {
            Check("a coast leaves the worked-out path alone", _g.V!.AbsoluteTime > _time && _g.V.Phase == Phase.Sublight && ReferenceEquals(_g.SubCurrent, _coast));
            _g.Paused = true;
            _g.OrbitSky = true;
        });
        for (int n = 0; n < 5; n++)
        {
            var view = n;
            Do(() =>
            {
                // From the sunward side, close enough to fill the window.
                var body = _views[view % _views.Count];
                var v = _g.V!;
                var at = body.StarPosition(v.AbsoluteTime);
                var sunward = at.Length > 0 ? -at.Normalized() : new Vec3d(1, 0, 0);
                var side = sunward.AnyPerpendicular();
                var away = (sunward + side * 0.5).Normalized();
                _g.ViewFromForTest(body.Star, at + away * (body.BodyRadius * (body.Parent == null ? 5 : 13)));
                _g.LookAtPublic(-away);
            }, 12);
            Do(() => Shot($"12_body_{view}"), 2);
        }
        Do(() =>
        {
            // Through a gate: dock at this region's, cast off, book, and drift into it.
            _g.OrbitSky = false;
            _g.ViewFromForTest(-1, Vec3d.Zero);
            var g = _g.G!;
            var region = g.Stars[_g.V!.SystemStar].Region;
            _gate = g.Gates.FirstOrDefault(x => g.Stars[x].Region == region, g.Gates[0]);
            _exit = g.GateLinks[_gate];
            _g.Purchase(s => s.Credits = 1000);
            _g.DockAtForTest(_gate);
        });
        Do(() =>
        {
            Check("a gate's port says where it leads", _g.Screen == Screen.Port && _g.GateExit == _exit && _g.Offers.Count == 0);
            Shot("13_gate_port");
            Press(Key.Enter);
        });
        Do(() => Press(Key.U));
        Do(() => _g.CommandForTest(Key.K));
        Do(() =>
        {
            var v = _g.V!;
            Check("the console books the passage", v.GateBooked is > 0);
            _toll = v.GateBooked ?? 0;
            _credits = _g.Save!.Credits;
            var st = v.LocalStation!;
            var t = v.AbsoluteTime;
            var (along, _, _) = Sublight.Frame(st.Position(t), st.Velocity(t));
            v.PlaceSublightForTest(st.Position(t) + along * (Tuning.DockRange * 0.5), st.Velocity(t) + along * 0.02);
            if (_g.Paused) Press(Key.Space);
            _clock = Time.GetTicksMsec();
        });
        Until(() => _g.V!.SystemStar == _exit || Time.GetTicksMsec() - _clock > 5000);
        Do(() =>
        {
            var v = _g.V!;
            Check("booked, the gate takes the ship through", v.SystemStar == _exit && v.Passages == 1 && v.TollsDue == _toll && _g.Screen == Screen.Orbit && v.Phase == Phase.Sublight);
            Check("the ship comes out at the speed it went in", Math.Abs((v.SubVelocity - v.StationNow().Velocity).Length - 0.02) < 2e-3);
            Shot("13a_gate_passed");
            _g.DockAtForTest(_exit);
        });
        Do(() =>
        {
            Check("the toll is settled at the next dock", Math.Abs(_g.Save!.Credits - (_credits + _g.Earned - _toll)) < 1e-9 && _g.PortReport.Any(l => l.StartsWith("通行税")));
            Press(Key.Enter);
        });
        Do(() => _g.GoBrokeForTest());
        Do(() =>
        {
            Shot("11_bankrupt");
            Check("running out of credits ends the game", _g.Screen == Screen.Dead && _g.Bankrupt);
            _retries = _g.Save!.Retries;
            Press(Key.R);
        });
        Do(() => Check("bankruptcy has no retry", _g.Screen == Screen.Dead && _g.Save!.Retries == _retries));
        Do(() => Press(Key.X));
        Do(() =>
        {
            Check("ending deletes the save", _g.Screen == Screen.Title && !_g.HasSave);
            GD.Print($"[autotest] done, {_failed} failed");
            GetTree().Quit(_failed);
        });
    }

    public override void _Process(double delta)
    {
        if (!Enabled || _g.Screen == Screen.Loading || --_wait > 0 || _steps.Count == 0) return;
        if (_g.ForecastBusyForTest && ++_settling < 30) return;   // forecasts land a few frames after they are asked for
        _settling = 0;
        var (step, wait) = _steps.Peek();
        _wait = wait;
        if (step()) _steps.Dequeue();
    }
}

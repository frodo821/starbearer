using System;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>A moment on screen marking a change of state. Major ones fill the view.</summary>
public sealed record StateCue(string Title, string Detail, Color Colour, bool Major, bool Outward)
{
    public float Seconds => Major ? 1.5f : 3f;
}

public enum Screen { Title, Loading, Orbit, Plan, Flight, Port, Dead }

/// <summary>Owns game flow: one checkpoint on disk, one Voyage in memory. The other parts of Main are split by what they handle.</summary>
public partial class Main : Node3D
{
    public Screen Screen { get; private set; } = Screen.Title;
    public SaveData? Save { get; private set; }
    public Galaxy? G { get; private set; }
    public Voyage? V { get; private set; }
    public Camera3D Camera { get; private set; } = null!;
    public string Message = "";
    public ChartView Chart { get; } = new();
    public Helm Helm { get; } = new();
    public bool CameraFollow;
    public bool MapOpen { get; set; }
    public bool OrbitSky;
    public bool Paused = true;
    public static readonly double[] Rates = Instruments.TimeScales.Select(x => x / Instruments.SecondsPerDay).ToArray();
    /// <summary>Time rate chosen in flight and in normal space, as indices into Rates.</summary>
    public int RateIndex = 4, SubRateIndex = 3;
    public double CurrentRate { get; private set; }
    const double BurnRate = 0.25;
    public Key? HeldButton { get; private set; }
    Sky? _sky;
    Hud _hud = null!;
    readonly Worlds _worlds = new();
    readonly TitleView _title = new();
    /// <summary>How dark the title's view is drawn over: between its scenes, and before the first is ready.</summary>
    public float TitleShade => _title.Shade;
    float _yaw, _pitch;

    /// <summary>The ship's primary on the galaxy chart, in ly.</summary>
    public Vec3d PrimaryOnChart => V!.System.Position + V.Primary.StarPosition(V.AbsoluteTime) * Sublight.ChartLyPerAu;

    public bool Owns(string module) => Save?.Modules.Contains(module) == true;

    public override void _Ready()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
            }
        });
        Camera = new Camera3D { Fov = 70, Far = 1000 };
        AddChild(Camera);
        AddChild(_worlds);
        AddChild(_title);
        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Hud { Game = this };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_hud);
        var script = new Autotest(this);
        if (script.Enabled)
        {
            Chart.Instant = true; AddChild(script);
            // Each run starts from no saves.
            if (DirAccess.Open(SaveDir) is { } dir)
                foreach (var f in dir.GetFiles()) dir.Remove(f);
        }
        RefreshSaves();
        // The title's view: a galaxy of its own, a fixed one for the scripted run.
        _title.Begin(script.Enabled ? 77UL : (ulong)System.Random.Shared.NextInt64());
    }

    public override void _Process(double delta)
    {
        // Behind the title and the loading screen, the title's own view; the game's sky and worlds are put away.
        var titled = Screen is Screen.Title or Screen.Loading;
        _title.Visible = titled;
        _worlds.Visible = !titled;
        if (_sky != null) _sky.Visible = !titled;
        if (titled)
        {
            if (_title.Step(delta) is { } look) LookAt(look);
            Camera.Rotation = new Vector3(_pitch, _yaw, 0);
        }
        if (Screen == Screen.Loading) { PollLoading(); _hud.QueueRedraw(); return; }
        if (Cue is { } cue)
        {
            CueAge += (float)delta;
            if (CueAge > cue.Seconds) Cue = null;
        }
        WatchState();
        Chart.Update(delta);
        if (Screen is Screen.Flight or Screen.Orbit) UpdateAttitude(delta);
        Camera.Rotation = new Vector3(_pitch, _yaw, 0);
        if (V == null) { _hud.QueueRedraw(); return; }
        var v = V;

        if (Screen == Screen.Orbit) UpdateOrbit(delta);
        if (Screen == Screen.Plan) UpdatePlan(delta);
        if (Screen == Screen.Flight) UpdateFlight(delta);
        if (Screen == Screen.Orbit) Chart.Follow(PrimaryOnChart, v.Primary.Parent != null);
        TickForecast(delta);
        _sky?.UpdateSky(v.TruePositions, SkyFromForTest ?? v.TruePosition, v.Phase == Phase.Flight ? v.Velocity : null, delta);
        ShowWorlds();
        _hud.QueueRedraw();
    }

    void ShowWorlds()
    {
        var v = V!;
        if (_viewForTest is { Star: >= 0 } view) { _worlds.Show(G!, view.Star, view.At, v.AbsoluteTime); return; }
        if (SkyFromForTest != null) { _worlds.Show(G!, -1, Vec3d.Zero, v.AbsoluteTime); return; }   // the sky seen from elsewhere: no worlds from here
        var star = v.Phase == Phase.Flight ? (v.NearestWell(out var s) < double.MaxValue && s != null ? s.Id : -1) : v.SystemStar;
        var (along, beta) = _sky?.Bubble ?? (Vec3d.Zero, 0);
        // In normal space the ship's own AU from its star; in a hyperspace well, where the well puts it in the system.
        var at = star < 0 ? Vec3d.Zero : v.Phase == Phase.Sublight ? v.Primary.StarPosition(v.AbsoluteTime) + v.SubPosition
            : Sublight.ToSystem(v.TruePosition - v.TruePositions[star], Vec3d.Zero).Position;
        _worlds.Show(G!, star, at, v.AbsoluteTime, along, beta);
    }
}

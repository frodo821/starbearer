using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>A moment on screen marking a change of state. Major ones fill the view.</summary>
public sealed record StateCue(string Title, string Detail, Color Colour, bool Major, bool Outward)
{
    public float Seconds => Major ? 1.5f : 3f;
}

public enum Screen { Title, Loading, Orbit, Plan, Flight, Port, Dead }
public enum ViewHold { Free, Star, Prograde, Retrograde, RadialOut, RadialIn, Normal, AntiNormal, Node, Target }

/// <summary>Owns game flow: one checkpoint on disk, one Voyage in memory.</summary>
public partial class Main : Node3D
{
    // The scripted smoke run must never touch the player's save.
    static string SavePath => OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--autotest=")) ? "user://autotest_save.json" : "user://save.json";

    public Screen Screen { get; private set; } = Screen.Title;
    public SaveData? Save { get; private set; }
    public Galaxy? G { get; private set; }
    public Voyage? V { get; private set; }

    public Camera3D Camera { get; private set; } = null!;
    public bool MapOpen { get; set; }
    public Vec3d PlanDir;
    public double PlanDv = 0.45;
    public Prediction? Forecast { get; private set; }
    public string Message = "";
    public int RateIndex = 4;
    public static readonly double[] Rates = Instruments.TimeScales.Select(x => x / Instruments.SecondsPerDay).ToArray();
    public double CurrentRate { get; private set; }
    public double Earned { get; private set; }
    public float MapYaw = 0.6f, MapPitch = 1.1f, MapZoom = 11f;
    public Vec3d MapCenter;

    // Normal space
    public int SubRateIndex = 3;
    /// <summary>The plan: the path with its burns. Frozen once a burn starts, so it stays the reference.</summary>
    public OrbitForecast? SubForecast { get; private set; }
    /// <summary>Where the ship goes if the engine stops now and nothing more is flown.</summary>
    public OrbitForecast? SubCurrent { get; private set; }
    public bool PlanFrozen { get; private set; }
    public Maneuver? Selected;
    public bool OrbitSky;
    public bool Paused = true;
    public ViewHold Hold { get; private set; } = ViewHold.Free;
    const double SlewRadPerSecond = 1.4, TurnRadPerSecond = 0.9, BurnRate = 0.25;

    /// <summary>Engine setting, 0..1. Space only starts and stops the engine at this setting.</summary>
    public double Throttle { get; private set; } = 0.5;
    public bool EngineOn { get; private set; }

    /// <summary>The ship's nose. Separate from the camera: engine and shield both follow it.</summary>
    public Vec3d Nose = new(0, 0, -1);
    public bool CameraFollow;
    public bool Firing { get; private set; }
    public bool HandlesOpen;
    // There is one chart. The orbit view is the galaxy map looked at closely, in AU around this system.
    public float OrbitYaw { get => MapYaw; set => MapYaw = value; }
    public float OrbitPitch { get => MapPitch; set => MapPitch = value; }
    public float OrbitZoom { get => MapZoom * (float)Sublight.Scale; set => MapZoom = value / (float)Sublight.Scale; }
    public Vec3d OrbitCenter
    {
        get => (MapCenter - V!.System.Position) / Sublight.Scale;
        set => MapCenter = V!.System.Position + value * Sublight.Scale;
    }
    const float SystemZoom = 425f, GalaxyZoom = 11f;
    /// <summary>Where the chart is gliding to after a change of layer.</summary>
    (Vec3d Center, float Zoom)? _glide;
    public bool InstantChart;

    /// <summary>Moves the chart to a view over a moment, so the two scales read as one map.</summary>
    public void GlideChart(Vec3d center, float zoom)
    {
        if (InstantChart) { MapCenter = center; MapZoom = zoom; return; }
        _glide = (center, zoom);
    }

    public void StopGlide() => _glide = null;

    void UpdateGlide(double delta)
    {
        if (_glide is not { } to) return;
        var k = 1 - Math.Exp(-5 * delta);
        MapCenter += (to.Center - MapCenter) * k;
        MapZoom *= Mathf.Pow(to.Zoom / MapZoom, (float)k);
        if (Mathf.Abs(MapZoom / to.Zoom - 1) < 0.01f && (MapCenter - to.Center).Length * MapZoom < 1) { MapCenter = to.Center; MapZoom = to.Zoom; _glide = null; }
    }

    void ToggleMap()
    {
        MapOpen = !MapOpen;
        if (!MapOpen) return;
        var close = V!.Phase == Phase.Flight && V.NearestWell(out _) < double.MaxValue;
        GlideChart(V.EstimatedPosition, close ? SystemZoom / 3 : GalaxyZoom);
    }

    /// <summary>What a descent right now would leave the ship on, in AU around the star. Null outside a well.</summary>
    public OrbitForecast? DescentForecast { get; private set; }
    public Star? DescentStar { get; private set; }
    public bool Owns(string module) => Save?.Modules.Contains(module) == true;

    Sky? _sky;
    Hud _hud = null!;
    float _yaw, _pitch;
    bool _looking, _dragged;
    Vector2 _pressAt;
    double _forecastTimer;
    double _pathGoodUntil;
    Task? _loading;
    bool _consolePress;
    int _lastWell = -1, _lastPrimary = -1;
    Phase _lastPhase = Phase.Sublight;
    public Key? HeldButton { get; private set; }
    /// <summary>Time is running fast up to the next ignition.</summary>
    public bool ToNode { get; private set; }
    /// <summary>Holds real time after N arrives at a node, so the nose can be brought round before ignition.</summary>
    public bool Aligning { get; private set; }
    public StateCue? Cue { get; private set; }
    public float CueAge { get; private set; }
    Action? _loaded;
    public string LoadingLabel { get; private set; } = "";
    public bool LoadFailed { get; private set; }
    bool _forecastDirty = true;
    int _attempt;

    public bool Shielded { get; private set; }
    public bool HasSave => FileAccess.FileExists(SavePath);

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
        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Hud { Game = this };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_hud);
        var script = new Autotest(this);
        if (script.Enabled) { InstantChart = true; AddChild(script); }
    }

    public override void _Process(double delta)
    {
        if (Screen == Screen.Loading) { PollLoading(); _hud.QueueRedraw(); return; }
        if (Cue is { } cue)
        {
            CueAge += (float)delta;
            if (CueAge > cue.Seconds) Cue = null;
        }
        WatchState();
        UpdateGlide(delta);
        if (Screen is Screen.Flight or Screen.Orbit) UpdateAttitude(delta);
        Camera.Rotation = new Vector3(_pitch, _yaw, 0);
        if (V == null) { _hud.QueueRedraw(); return; }
        var v = V;

        if (Screen == Screen.Orbit) UpdateOrbit(delta);

        if (Screen == Screen.Plan)
        {
            var dir = (IsDown(Key.W) ? 1 : 0) - (IsDown(Key.S) ? 1 : 0);
            if (dir != 0) SetPlanDv(PlanDv + dir * (Input.IsKeyPressed(Key.Shift) ? 0.01 : 0.12) * delta);
        }

        if (Screen == Screen.Flight)
        {
            if (v.Phase == Phase.Arrived) { Dock(); _hud.QueueRedraw(); return; }
            if (v.Phase == Phase.Dead) { Screen = Screen.Dead; _hud.QueueRedraw(); return; }
            var r = v.NearestWell(out var star);
            var rate = Rates[RateIndex];
            if (star != null)
                rate = Math.Clamp(0.5 * r / Math.Max(v.Velocity.Length, 0.01), 0.04, rate);
            if (EngineOn) rate = Math.Min(rate, BurnRate);
            CurrentRate = rate;

            var toStar = star != null ? (v.TruePositions[star.Id] - v.TruePosition).Normalized() : Vec3d.Zero;
            Shielded = star != null && Nose.Dot(toStar) > 0.8;
            var engine = EngineCommand();
            if (engine.LengthSq > 0) _forecastDirty = true;   // the coasting forecast is "if I stop now"
            v.Advance(rate * delta, engine, Shielded);

            if (v.Phase == Phase.Arrived) Dock();
            else if (v.Phase == Phase.Dead) Screen = Screen.Dead;
            else if (v.Phase == Phase.Stalled) Message = "失速。FTLから脱落した。Cで再キック";
        }

        _forecastTimer -= delta;
        if (Screen is Screen.Plan or Screen.Flight or Screen.Orbit && (_forecastDirty || _forecastTimer <= 0))
        {
            RefreshForecast(_forecastDirty);
            _forecastDirty = false;
            _forecastTimer = 0.4;
        }
        _sky?.UpdateSky(v.TruePositions, v.TruePosition);
        _hud.QueueRedraw();
    }

}

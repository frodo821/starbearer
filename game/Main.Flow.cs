using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

public partial class Main
{

    public void StartNewGalaxy() => Begin(System.Random.Shared.NextInt64() is var n ? (ulong)n : 1);

    public void StartSeed(ulong seed) => Begin(seed);

    void Begin(ulong seed) => Load("銀河を生成しています", () => SaveData.NewGalaxy(seed), made =>
    {
        Save = made.Save;
        WriteSave();
        ShowGalaxy(made.Galaxy);
        Board();
    });

    /// <summary>Runs slow work off the main thread, with the loading screen up until it is done.</summary>
    void Load<T>(string label, Func<T> work, Action<T> done)
    {
        LoadingLabel = label;
        LoadFailed = false;
        Screen = Screen.Loading;
        var task = Task.Run(work);
        _loading = task;
        _loaded = () =>
        {
            if (task.IsCompletedSuccessfully) { done(task.Result); return; }
            GD.PushError(task.Exception?.ToString() ?? "loading failed");
            LoadFailed = true;
            Screen = Screen.Title;
        };
    }

    void PollLoading()
    {
        if (_loading is not { IsCompleted: true }) return;
        var finish = _loaded!;
        _loading = null;
        _loaded = null;
        finish();
    }

    void ShowGalaxy(Galaxy g)
    {
        G = g;
        _sky?.QueueFree();
        _sky = new Sky();
        AddChild(_sky);
        _sky.Setup(g);
    }

    public void Continue()
    {
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        Save = SaveData.FromJson(f.GetAsText());
        Board();
    }

    /// <summary>Set up the docked state from the checkpoint. Also the retry entry point.</summary>
    void Board()
    {
        var s = Save!;
        Bankrupt = false;
        if (G == null || G.Seed != s.Seed) ShowGalaxy(Galaxy.Generate(s.Seed));
        V = new Voyage(G!, s.PortStar, s.PortStar, s.Hold, (double[])s.ClosestApproach.Clone(),
            s.Seed ^ (ulong)(s.Deliveries * 31 + ++_attempt * 1009), s.TotalDays, new ShipState(s.HeldFuel, s.HeldHull, s.HeldLife));
        if (Supply.Bankrupt(s)) { Bankrupt = true; Screen = Screen.Dead; return; }
        SteerForCourse();
        PlanDir = (V.ChartPositions[V.TargetStar] - V.EstimatedPosition).Normalized();
        PlanDv = 0.35;
        Forecast = null; SubForecast = null; SubCurrent = null; PlanFrozen = false; Selected = null;
        OrbitSky = false; Paused = true;
        MapCenter = V.System.Position; MapZoom = SystemZoom; _glide = null;
        LookAt(PlanDir);
        Screen = Screen.Orbit;
        MapOpen = false;
        Nose = PlanDir.Normalized(); Hold = ViewHold.Free; CameraFollow = false; EngineOn = false; Throttle = 0.5;
        Message = s.Hold.Count == 0 ? "空荷で停泊中。Enter で港を開いてミッションを請ける" : "港に停泊中。星図と計画で発進の時機を選び、U で出港する";
        _forecastDirty = true;
    }

    /// <summary>The star the instruments steer for, when no cargo says otherwise: the nearest other station.</summary>
    int NearestStation()
    {
        var here = G!.Stars[V!.SystemStar].Position;
        return G.Ports.Concat(G.Depots).Where(i => i != V.SystemStar).MinBy(i => (G.Stars[i].Position - here).Length);
    }

    /// <summary>Holds the chosen course while cargo is bound there, else the first job in the hold.</summary>
    void SteerForCourse()
    {
        var s = Save!;
        if (s.Hold.All(c => c.Mission.Target != s.Course))
            s.Course = s.Hold.Count > 0 ? s.Hold[0].Mission.Target : -1;
        V!.TargetStar = s.Course >= 0 ? s.Course : NearestStation();
    }

    public int Course => Save!.Course;

    public IReadOnlyList<Mission> Offers => Missions.Board(G!, Save!.PortStar, Save.Deliveries, Save.Hold);

    void PortChanged()
    {
        WriteSave();
        _hud.Port.Rebuild();
    }

    public void TakeMission(Mission m)
    {
        if (!Missions.Take(Save!, m)) { Message = "カーゴの容積が足りない"; _hud.Port.Rebuild(); return; }
        Message = "";
        SteerForCourse();
        PortChanged();
    }

    public void DropCargo(Consignment c)
    {
        Missions.Drop(Save!, c);
        Message = $"{c.Mission.Cargo.Name} の仕事を破棄した";
        SteerForCourse();
        PortChanged();
    }

    public void SetCourse(int star)
    {
        Save!.Course = star;
        SteerForCourse();
        PortChanged();
    }

    public void BeginJump()
    {
        if (Screen != Screen.Orbit) return;
        CutEngine("エンジン停止");
        Screen = Screen.Plan;
        PlanDir = (V!.ChartPositions[V.TargetStar] - V.EstimatedPosition).Normalized();
        LookAt(PlanDir);
        Message = "空をタップして方位を選び、W/S でキック量を決める";
        _forecastDirty = true;
    }

    const string DockedNotice = "停泊中は操船できない。U で出港する";

    /// <summary>What a docked ship can't touch. The chart, the plan and the clock stay usable.</summary>
    static bool ShipControl(Key key) => key is Key.Space or Key.O or Key.L or Key.V or Key.B or Key.G or Key.X or Key.Z or Key.I or Key.K;

    /// <summary>Casts off, or ties back up at a port the ship hasn't left yet.</summary>
    public void ToggleDock()
    {
        if (Screen != Screen.Orbit) return;
        var v = V!;
        if (v.Docked)
        {
            v.Undock();
            ShowCue(new StateCue("出港", v.System.Name + " 港を離れた", new Color(0.5f, 1f, 0.7f), Major: false, Outward: true));
            Message = "出港した。ここから生命維持を消費する";
            _forecastDirty = true;
        }
        else if (v.RequestDock()) Dock();
        else Message = $"港まで {Tuning.DockRange:F2} AU、相対 {Tuning.DockRelativeSpeed:F2} 以内で入港できる";
    }

    /// <summary>True while the port screen was opened from the ship, so leaving it goes back aboard.</summary>
    public bool VisitingPort { get; private set; }

    public void OpenPort()
    {
        if (Screen != Screen.Orbit || !V!.Docked) return;
        CutEngine("");
        VisitingPort = true;
        PortReport = new();
        Message = "";
        Screen = Screen.Port;
    }

    public void LeavePort()
    {
        if (!VisitingPort) { Board(); return; }
        var s = Save!;
        VisitingPort = false;
        V!.Restock(new ShipState(s.HeldFuel, s.HeldHull, s.HeldLife), s.Hold);
        SteerForCourse();
        Message = "";
        Screen = Screen.Orbit;
        _forecastDirty = true;
    }

    public void TryDescend()
    {
        if (Screen != Screen.Flight) return;
        if (!V!.Descend()) { Message = "恒星の井戸の中でしか降下できない"; return; }
        EngineOn = false;
        Screen = Screen.Orbit;
        MapOpen = false; Selected = null; Paused = true; PlanFrozen = false;
        OrbitSky = false;
        GlideChart(V.System.Position, SystemZoom);
        ShowCue(new StateCue("亜光速へ降下", V.System.Name + " 星系", new Color(0.5f, 1f, 0.7f), Major: true, Outward: false));
        Message = V.LocalStation == null ? "この星系に港はない。J で再発進できる"
            : V.Hold.Any(c => c.Mission.Target == V.SystemStar) ? "亜光速へ降下。軌道をタップしてマニューバを置き、港へ軌道を合わせる"
            : "この星系の港に入港すると補給できる。J で再発進もできる";
        _forecastDirty = true;
    }

    public void Buy(string id, double price, string name)
    {
        var s = Save!;
        if (s.Modules.Contains(id)) return;
        if (s.Credits < price) { Message = $"{name}: 資金が足りない"; _hud.Port.Rebuild(); return; }
        s.Credits -= price;
        s.Modules.Add(id);
        Message = $"{name} を搭載";
        PortChanged();
    }

    /// <summary>X and Z point the nose for the pilot and fire once it is on target.</summary>
    Vec3d SublightThrust(double delta)
    {
        var v = V!;
        if (v.Docked) { Firing = false; return Vec3d.Zero; }
        var st = v.LocalStation;
        Vec3d? assist = null;
        if (st != null && IsDown(Key.X))
        {
            var toward = st.Position(v.AbsoluteTime) - v.SubPosition;
            var rel = st.Velocity(v.AbsoluteTime) - v.SubVelocity;
            var want = rel + toward.Normalized() * Math.Min(0.02, toward.Length);
            if (want.Length < 0.005) { Firing = false; return Vec3d.Zero; }
            assist = want.Normalized();
        }
        else if (st != null && IsDown(Key.Z))
        {
            var rel = st.Velocity(v.AbsoluteTime) - v.SubVelocity;
            if (rel.Length < 0.002) { Firing = false; return Vec3d.Zero; }
            assist = rel.Normalized();
        }
        if (assist is not { } dir) return EngineCommand();
        Hold = ViewHold.Free;
        SlewNose(dir, delta);
        Firing = Nose.Dot(dir) > 0.99;
        return Firing ? Nose : Vec3d.Zero;
    }

    void WriteSave()
    {
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        f.StoreString(Save!.ToJson());
    }

    public void Retry()
    {
        Save!.Retries++;
        WriteSave();
        Board();
    }

    public void EndVoyageForGood()
    {
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
        Save = null; V = null; G = null;
        _sky?.QueueFree(); _sky = null;
        Screen = Screen.Title;
    }

    /// <summary>What the last dock settled, for the port window.</summary>
    public List<string> PortReport { get; private set; } = new();
    /// <summary>The company can't pay for the fuel to leave. Unlike a death, this has no retry.</summary>
    public bool Bankrupt { get; private set; }

    public void Purchase(Action<SaveData> buy)
    {
        buy(Save!);
        PortChanged();
    }

    /// <summary>Every dock is a checkpoint: cargo bound here is paid for and the rest stays aboard.</summary>
    void Dock()
    {
        var s = Save!;
        var v = V!;
        var here = v.Hold.Where(c => c.Mission.Target == v.SystemStar).ToList();
        PortReport = here.Select(c => $"配達  {c.Mission.Cargo.Name}  健全度 {c.Integrity:P0}   報酬 {c.Payout:F0}").ToList();
        PortReport.Add($"所要 {v.Time:F1} 日   残燃料 {v.Fuel:F2}   船体 {v.Hull:P0}");
        Earned = here.Sum(c => c.Payout);
        s.Credits += Earned;
        s.Deliveries += here.Count;
        s.Hold = v.Hold.Except(here).Select(c => c.Copy()).ToList();
        s.HeldFuel = v.Fuel;
        s.HeldHull = v.Hull;
        s.HeldLife = v.LifeRemaining;
        s.TotalDays += v.Time;
        s.PortStar = v.SystemStar;
        s.ClosestApproach = v.ClosestApproach;
        WriteSave();
        VisitingPort = false;
        Message = "";
        Screen = Screen.Port;
    }

    /// <summary>Autotest only: forces the death path through the normal flow.</summary>
    public void CrashForTest() { V!.ForceDeathForTest(); Screen = Screen.Dead; }
}

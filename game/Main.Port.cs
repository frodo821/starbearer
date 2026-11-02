using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>The port: jobs, course, purchases, and docking.</summary>
public partial class Main
{
    public double Earned { get; private set; }

    /// <summary>What the last dock settled, for the port window.</summary>
    public List<string> PortReport { get; private set; } = new();

    /// <summary>True while the port screen was opened from the ship, so leaving it goes back aboard.</summary>
    public bool VisitingPort { get; private set; }

    const string DockedNotice = "停泊中は操船できない。U で出港する";

    /// <summary>What a docked ship can't touch. The chart, the plan and the clock stay usable.</summary>
    static bool ShipControl(Key key) => key is Key.E or Key.O or Key.L or Key.B or Key.G or Key.X or Key.Z or Key.W or Key.S;

    public int Course => Save!.Course;
    public IReadOnlyList<Mission> Offers => Missions.Board(G!, Save!.PortStar, Save.Deliveries, Save.Hold);

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

    public void Purchase(Action<SaveData> buy)
    {
        buy(Save!);
        PortChanged();
    }

    /// <summary>Casts off, or ties back up at a port the ship hasn't left yet.</summary>
    public void ToggleDock()
    {
        if (Screen != Screen.Orbit) return;
        var v = V!;
        if (v.Docked)
        {
            v.Undock();
            ShowCue(new StateCue("出港", v.System.Name + " 港を離れた", Palette.Good, Major: false, Outward: true));
            Message = "出港した。ここから生命維持を消費する";
            _forecastDirty = true;
        }
        else if (v.RequestDock()) Dock();
        else Message = $"港まで {Tuning.DockRange:F2} AU、相対 {Tuning.DockRelativeSpeed:F2} 以内で入港できる";
    }

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
}

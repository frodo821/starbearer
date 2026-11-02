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

    /// <summary>
    /// Holds the chosen course, cargo bound there or not, until the ship docks there. With none, the
    /// first job in the hold sets it, and with no job either the instruments steer for the nearest station.
    /// </summary>
    void SteerForCourse()
    {
        var s = Save!;
        var v = V!;
        if (s.Course == v.SystemStar && v.Docked) s.Course = -1;   // arrived
        if (s.Course < 0 && s.Hold.FirstOrDefault(c => c.Mission.Target != v.SystemStar) is { } job) s.Course = job.Mission.Target;
        v.TargetStar = s.Course >= 0 ? s.Course : NearestStation();
    }

    /// <summary>The course was picked by the pilot, not set by a job in the hold.</summary>
    public bool FreeCourse => Save!.Course >= 0 && Save.Hold.All(c => c.Mission.Target != Save.Course);

    void PortChanged()
    {
        WriteSave();
        _hud.Port.Rebuild();
    }

    public void TakeMission(Mission m)
    {
        if (!Missions.Take(Save!, m)) { Message = CargoBays.Of(Save!).Refusal(Save!.Hold, m.Cargo) ?? "積めない"; _hud.Port.Rebuild(); return; }
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

    /// <summary>Sets where the ship is bound, from a job at the port or any star on the chart.</summary>
    public void SetCourse(int star)
    {
        Save!.Course = star;
        SteerForCourse();
        Message = "目的地: " + G!.Stars[V!.TargetStar].Name;
        _forecastDirty = true;
        PortChanged();
    }

    /// <summary>Lets go of a course the pilot picked; the hold, or the nearest station, sets it again.</summary>
    public void ClearCourse()
    {
        Save!.Course = -1;
        SteerForCourse();
        Message = "目的地を解除。目的地: " + G!.Stars[V!.TargetStar].Name;
        _forecastDirty = true;
        PortChanged();
    }

    /// <summary>Where the gate of this system leads, or -1.</summary>
    public int GateExit => V != null && G!.GateLinks.TryGetValue(V.SystemStar, out var exit) ? exit : -1;

    /// <summary>What a passage through this system's gate would cost now.</summary>
    public double GateToll => Gates.Toll(Save!, V!);

    /// <summary>Books the passage through this system's gate, or lets the booking go. The toll is settled at the next dock.</summary>
    public void ToggleGate()
    {
        var v = V!;
        if (GateExit < 0) return;
        if (!v.Class.Gates) { Message = $"{v.Class.Name} はゲートを通れない"; return; }
        if (v.Docked) { Message = "出港してからゲートへ向かう"; return; }
        if (v.GateBooked != null) { v.CancelGate(); Message = "ゲート通過の予約を取り消した"; return; }
        var toll = GateToll;
        if (Save!.Credits - v.TollsDue < toll) { Message = $"通行税 {toll:F0} を払えない"; return; }
        v.BookGate(toll);
        var exit = GateExit;
        Message = $"ゲート通過を予約（通行税 {toll:F0}、次の入港で精算）。ゲートの入港距離まで寄ると {G!.Stars[exit].Name}（{G.Regions[G.Stars[exit].Region].Name} 宙域）へ出る";
    }

    /// <summary>The port the ship is at builds modules: only a shipyard does.</summary>
    public bool AtShipyard => V != null && Missions.KindOf(G!, V.SystemStar) == StationKind.Shipyard;

    /// <summary>The nearest shipyard in the region, for pointing the way to one; -1 if none.</summary>
    public int NearestShipyard()
    {
        var here = G!.Stars[V!.SystemStar];
        return G.Ports.Where(p => Missions.KindOf(G, p) == StationKind.Shipyard && G.Stars[p].Region == here.Region)
            .OrderBy(p => (G.Stars[p].Position - here.Position).Length).DefaultIfEmpty(-1).First();
    }

    public void Buy(string id, double price, string name)
    {
        var s = Save!;
        if (s.Modules.Contains(id)) return;
        if (!AtShipyard) { Message = "モジュールは造船所で買える"; _hud.Port.Rebuild(); return; }
        if (s.Credits < price) { Message = $"{name}: 資金が足りない"; _hud.Port.Rebuild(); return; }
        s.Credits -= price;
        s.Modules.Add(id);
        Message = $"{name} を搭載";
        PortChanged();
    }

    /// <summary>Fits a pressurizer to the next fluid bay without one.</summary>
    public void BuyPressurizer()
    {
        if (!AtShipyard) { Message = "モジュールは造船所で買える"; _hud.Port.Rebuild(); return; }
        if (!Modules.FitPressurizer(Save!)) { Message = $"{Modules.PressurizerName}: 付けられる液体ベイがないか、資金が足りない"; _hud.Port.Rebuild(); return; }
        Message = $"{Modules.PressurizerName} を液体ベイに搭載";
        PortChanged();
    }

    public void BuyShip(ShipClass c)
    {
        if (!Hangar.Buy(Save!, G!, c)) { Message = $"{c.Name}: " + (Save!.Credits < c.Price ? "資金が足りない" : "ここでは造っていない"); _hud.Port.Rebuild(); return; }
        Message = $"{c.Name} を購入した。この港に置いてある";
        PortChanged();
    }

    /// <summary>Changes ships here. The one left keeps its bays' cargo, fuel and modules; the voyage starts over in the new one.</summary>
    public void BoardShip(int id)
    {
        var s = Save!;
        if (id == s.Flown || !Hangar.Board(s, id)) return;
        VisitingPort = false;   // leaving the port boards the new ship from the checkpoint
        SteerForCourse();
        Message = $"{s.Ship.Type.Name} に乗り換えた";
        PortChanged();
    }

    public void SellShip(int id)
    {
        var s = Save!;
        var ship = s.Fleet.FirstOrDefault(x => x.Id == id);
        if (ship == null) return;
        var value = Ships.SaleValue(ship);
        if (!Hangar.Sell(s, G!, id)) { Message = Hangar.SaleRefusal(s, G!, ship) ?? "売れない"; _hud.Port.Rebuild(); return; }
        Message = $"{ship.Type.Name} を {value:F0} で売った";
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
        if (v.TollsDue > 0) PortReport.Add($"通行税  -{v.TollsDue:F0}");
        s.Credits = Math.Max(0, s.Credits + Earned - v.TollsDue);
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

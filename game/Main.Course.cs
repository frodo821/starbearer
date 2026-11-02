using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>Where the ship is bound: the course, the gate, and the manifest of what is aboard.</summary>
public partial class Main
{
    public int Course => Save!.Course;

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
        if (s.Course < 0 && s.Hold.FirstOrDefault(c => c.IsJob && c.Mission.Target != v.SystemStar) is { } job) s.Course = job.Mission.Target;
        v.TargetStar = s.Course >= 0 ? s.Course : NearestStation();
    }

    /// <summary>The course was picked by the pilot, not set by a job in the hold.</summary>
    public bool FreeCourse => Save!.Course >= 0 && Save.Hold.All(c => c.Mission.Target != Save.Course);

    /// <summary>The manifest window: the hold and the jobs aboard, open over the chart or the sky.</summary>
    public bool ManifestOpen { get; private set; }

    public void ToggleManifest()
    {
        ManifestOpen = !ManifestOpen;
        if (ManifestOpen) _hud.Manifest.Rebuild();
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
}

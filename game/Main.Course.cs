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

    /// <summary>The star the instruments steer for, when no cargo says otherwise: the nearest other system with a station.</summary>
    int NearestStation()
    {
        var here = G!.Stars[V!.SystemStar].Position;
        return G.Ports.Concat(G.Depots).Where(G.Alive).Select(G.StarOf).Where(i => i != V.SystemStar).MinBy(i => (G.Stars[i].Position - here).Length);
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
        if (s.Course < 0 && s.Hold.FirstOrDefault(c => c.IsJob && G!.StarOf(c.Mission.Target) != v.SystemStar) is { } job) s.Course = G!.StarOf(job.Mission.Target);
        v.TargetStar = s.Course >= 0 ? s.Course : NearestStation();
    }

    /// <summary>The course was picked by the pilot, not set by a job in the hold.</summary>
    public bool FreeCourse => Save!.Course >= 0 && Save.Hold.All(c => !c.IsJob || G!.StarOf(c.Mission.Target) != Save.Course);

    /// <summary>
    /// What the news that reached the last port says of a station's fuel: how much it had, and how many
    /// days ago. Null for something that isn't a station.
    /// </summary>
    public string? FuelNews(int station)
    {
        if (Save is not { } s || !G!.Economies.ContainsKey(station)) return null;
        var m = s.MarketIn(G);
        if (m.Known(G, s.PortStation, station, Wares.ShipFuel) is not { } k) return "燃料 知らせなし";
        var stock = k.Fill < 0.05 ? "品切れ" : k.Fill < 0.3 ? "わずか" : k.Fill < 0.8 ? "少ない" : "十分";
        var age = m.Day - k.Day;
        return $"燃料 {stock}  {Economy.Price(Wares.Get(Wares.ShipFuel), k.Fill):F0}/単位" + (age > 0.5 ? $"（{age:F0} 日前）" : "（いま）");
    }

    /// <summary>Opens what the news says of a station: its market and the jobs it had posted.</summary>
    public void ShowStationInfo(int star) => _hud.StationInfo.Open(star);
    public void CloseStationInfo() => _hud.StationInfo.Close();
    public bool StationInfoOpen => _hud.StationInfo.StationId >= 0;

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

    /// <summary>The gate station this system's gate leads to, or -1.</summary>
    public int GateExit => V?.LocalStation is { } st && G!.GateLinks.TryGetValue(st.Id, out var exit) ? exit : -1;

    /// <summary>Whether this system's gate lets the ship in: its owner must not be hostile. The exit doesn't ask.</summary>
    public bool GateAdmitted => V?.LocalStation is { } st && Factions.Admits(Save!, G!, st.Id);

    /// <summary>Whether this system's gate pair is running: both ends have their upkeep, as of the checkpoint.</summary>
    public bool GateOpen => V?.LocalStation is { } st && Gates.Open(G!, Save!.MarketIn(G!), st.Id);

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
        if (!GateOpen) { Message = "ゲートは止まっている: 維持の品物（燃料・電子機器・核分裂燃料）が尽きた。どちらかの端に届けば動き出す"; return; }
        if (!GateAdmitted) { Message = $"{Factions.OwnerName(G!, v.LocalStation!.Id)} はゲートの通航を拒否している（敵対）"; return; }
        var toll = GateToll;
        if (Save!.Credits - v.TollsDue < toll) { Message = $"通行税 {toll:F0} を払えない"; return; }
        v.BookGate(toll);
        var exit = GateExit;
        Message = $"ゲート通過を予約（通行税 {toll:F0}、次の入港で精算）。ゲートの入港距離まで寄ると {G!.StarAt(exit).Name}（{G.Regions[G.StarAt(exit).Region].Name} 宙域）へ出る";
    }
}

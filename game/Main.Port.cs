using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>The port: jobs, supplies, docking and the checkpoint a dock makes.</summary>
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

    public IReadOnlyList<Mission> Offers => Missions.OffersFor(Save!, G!);

    void PortChanged()
    {
        WriteSave();
        _hud.Port.Rebuild();
        if (ManifestOpen) _hud.Manifest.Rebuild();
    }

    public void TakeMission(Mission m)
    {
        if (!Missions.Take(Save!, G!, m)) { Refuse(CargoBays.Of(Save!).Refusal(Save!.Hold, m.Cargo) ?? "在庫が足りない"); return; }
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

    /// <summary>A purchase or a job refused: the reason goes up on the window, nothing is saved.</summary>
    void Refuse(string why)
    {
        Message = why;
        _hud.Port.Rebuild();
    }

    /// <summary>How standing moved since before, as a line for the port; null if it didn't.</summary>
    string? StandingChange(double[] before)
    {
        var g = G!; var s = Save!;
        var moved = g.Factions.Where(f => f.Id < before.Length && Math.Abs(Factions.Standing(s, f.Id) - before[f.Id]) >= 0.005)
            .Select(f => $"{f.Name} {Factions.Standing(s, f.Id) - before[f.Id]:+0.00;-0.00}").ToList();
        return moved.Count == 0 ? null : "スタンディング  " + string.Join("   ", moved);
    }

    /// <summary>
    /// What the war did over those days, as lines for the port: the company's ships lost with a station
    /// first, then the news, the most recent few.
    /// </summary>
    List<string> WarNews(double from, double to)
    {
        var s = Save!; var g = G!;
        var lines = War.Casualties(s, g).Select(x => $"自船 {x.Type.Name} を失った: {Missions.Label(g, x.Station)} が破壊された" + (x.Hold.Count > 0 ? $"（積荷 {x.Hold.Count} 件とともに）" : "")).ToList();
        var news = War.Between(s.MarketIn(g), from, to).ToList();
        lines.AddRange(news.TakeLast(4).Select(e => $"戦況  {Instruments.Day(e.Day)}  {War.Describe(g, e)}"));
        if (news.Count > 4) lines.Add($"ほか {news.Count - 4} 件（戦況タブ）");
        return lines;
    }

    /// <summary>The galaxy's day at the checkpoint, as the port shows it.</summary>
    public string GalaxyDayText => Instruments.Day(Save!.TotalDays);

    /// <summary>Fills the tank from the station's fuel stock, as far as stock and credits go.</summary>
    public void BuyFuel()
    {
        var s = Save!;
        if (s.MarketIn(G!).StockOf(G!, s.PortStation, Wares.ShipFuel) < 1e-3) { Refuse("燃料が品切れ。待てば補充される"); return; }
        Purchase(x => Trade.BuyFuel(x, G!, x.MarketIn(G!)));
    }

    public void BuyWare(string ware, int units)
    {
        var s = Save!;
        if (!Trade.Buy(s, G!, s.MarketIn(G!), ware, units)) { Refuse($"{Wares.Get(ware).Name}: 買えない（在庫・資金・ベイの空きを確認）"); return; }
        Message = $"{Wares.Get(ware).Name} を {units} 単位買った";
        SteerForCourse();
        PortChanged();
    }

    public void SellWare(string ware, int units)
    {
        var s = Save!;
        var before = s.Standing.ToArray();
        if (Trade.SellUnits(s, G!, s.MarketIn(G!), ware, units) is not { } got) { Refuse($"{Wares.Get(ware).Name}: ここでは売れない"); return; }
        Message = $"{Wares.Get(ware).Name} を {units} 単位、{got:F0} で売った" + (StandingChange(before) is { } moved ? "。" + moved : "");
        PortChanged();
    }

    /// <summary>Waits at the port: the galaxy's clock and its market move on, life support is the port's.
    /// The voyage starts over from the new day when the ship leaves.</summary>
    public void WaitAtPort(double days)
    {
        var s = Save!;
        var g = G!;
        var from = s.TotalDays;
        var waited = 0;
        // A day at a time, so a port coming under fire sends the ship off before it falls.
        for (; waited < days; waited++)
        {
            if (g.War.HullOf(s.PortStation) < 0.6) break;
            s.TotalDays += 1;
            s.MarketIn(g);
        }
        PortReport = WarNews(from, s.TotalDays);
        VisitingPort = false;
        Message = waited < days ? $"{waited} 日で待つのをやめた: この港は砲火を浴びている（構造 {g.War.HullOf(s.PortStation):P0}）" : $"{days:F0} 日待った";
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
        else if (v.CanDock && !DockAdmitted) Message = $"{Factions.OwnerName(G!, v.LocalStation!.Id)} は入港を拒否した（敵対）";
        else if (v.RequestDock()) Dock();
        else Message = $"港まで {Tuning.DockRange:F3} AU、相対 {Tuning.DockRelativeSpeed:F3} 以内で入港できる（X 接近、Z 相対0 で寄せる）";
    }

    /// <summary>Whether the station in reach lets the ship dock: its owner must not be hostile.</summary>
    public bool DockAdmitted => V?.LocalStation is { } st && Factions.Admits(Save!, G!, st.Id);

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
        V!.Restock(s.Ship.State, s.Hold);
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
        var station = v.LocalStation!.Id;
        var here = v.Hold.Where(c => c.Mission.Target == station).ToList();
        PortReport = here.Select(c => $"配達  {c.Mission.Cargo.Name}  健全度 {c.Integrity:P0}   報酬 {c.Payout:F0}").ToList();
        PortReport.Add($"所要 {Instruments.Span(v.Time)}   残燃料 {v.Fuel:F2}   船体 {v.Hull:P0}");
        Earned = here.Sum(c => c.Payout);
        if (v.TollsDue > 0) PortReport.Add($"通行税  -{v.TollsDue:F0}");
        s.Credits = Math.Max(0, s.Credits + Earned - v.TollsDue);
        s.Deliveries += here.Count;
        s.Hold = v.Hold.Except(here).Select(c => c.Copy()).ToList();
        s.HeldFuel = v.Fuel;
        s.HeldHull = v.Hull;
        s.HeldLife = v.LifeRemaining;
        var from = s.TotalDays;
        s.TotalDays += v.Time;
        s.MarketIn(G!);   // the galaxy's stock, and the war, catch up with the days flown
        var before = s.Standing.ToArray();
        foreach (var c in here) Missions.Deliver(s, G!, c);
        if (StandingChange(before) is { } moved) PortReport.Add(moved);
        s.PortStation = station;
        PortReport.AddRange(WarNews(from, s.TotalDays));
        s.ClosestApproach = v.ClosestApproach;
        WriteSave();
        VisitingPort = false;
        Message = "";
        Screen = Screen.Port;
    }
}

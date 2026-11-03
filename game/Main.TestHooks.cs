using System;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>Autotest only.</summary>
public partial class Main
{
    /// <summary>Autotest only: shows the worlds of a system from a given place.</summary>
    (int Star, Vec3d At)? _viewForTest;

    public void ViewFromForTest(int star, Vec3d at) => _viewForTest = (star, at);

    /// <summary>Autotest only: where the sky is seen from, in ly, instead of the ship.</summary>
    public Vec3d? SkyFromForTest;

    /// <summary>Autotest only: forces the death path through the normal flow.</summary>
    public void CrashForTest() { V!.ForceDeathForTest(); Screen = Screen.Dead; }

    public void GoBrokeForTest()
    {
        Save!.Credits = 0;
        Save.HeldFuel = 0.1;
        WriteSave();
        Board();
    }

    /// <summary>Autotest only: puts the ship beside a station and docks there.</summary>
    public void DockAtForTest(int depot)
    {
        var v = V!;
        var st = G.StationAt(depot);
        v.EnterFtl();
        var host = st.Host!;
        v.PlaceForTest(G.StarAt(depot).Position + (host.StarPosition(v.AbsoluteTime) + st.Position(v.AbsoluteTime)) * Sublight.Scale, host.StarVelocity(v.AbsoluteTime) + st.Velocity(v.AbsoluteTime));
        v.Descend();
        v.SetGoal(st);   // in a system of several, this one
        v.AdvanceSublight(0.01, Vec3d.Zero);
        Dock();
    }

    /// <summary>Takes a job here not bound for the given star, with the port shut.</summary>
    public void TakeMissionForTest(int notTo)
    {
        var s = Save!;
        Missions.Take(s, G!, Offers.First(m => m.Target != notTo && Missions.Fits(s, m)));
        SteerForCourse();
        V!.Restock(s.Ship.State, s.Hold);
        WriteSave();
    }

    public void PlaceInTargetWellForTest()
    {
        var s = G!.Stars[V!.TargetStar];
        var r = Sublight.SystemRadius(s) * 0.12;
        var speed = Math.Sqrt(Sublight.Mu(s) / r) * 1.1;
        V.PlaceForTest(V.TruePositions[s.Id] + new Vec3d(r * Sublight.Scale, 0, 0), new Vec3d(0, 0, speed));
        V.RefillForTest();
    }

    public Vector2 OrbitPointForTest(Vec3d p) => _hud.OrbitPoint(p);
    public Vector2 LegPointForTest(OrbitForecast leg, Vec3d p) => _hud.LegMap(leg)(p);
    public Vector2 HandleTipForTest(int i) => _hud.HandleTip(i);
    public int HandleCountForTest => _hud.HandleCount;
    public int MarkerCountForTest => _hud.MarkerCount;
    public string ClockTextForTest => _hud.ClockTextForTest;
    public Vector2 CompassMarkerForTest(ViewHold h) => _hud.CompassMarkerForTest(h);
    public Vector2 HoldButtonForTest(ViewHold h) => _hud.HoldButtonForTest(h);

    /// <summary>Presses a console button by its command, as a tap on it would.</summary>
    public void CommandForTest(Key k) => PressButton(new Command(k, "", ""));

    public Vector2 DeleteButtonForTest => _hud.DeleteButton(Selected!)!.Value;
    public void ReloadForTest() => Continue();
    public Hud HudForTest => _hud;
    public Sky SkyForTest => _sky!;
    public Vector2 BodyPointForTest(Center c) => _hud.BodyPointForTest(c);
    public Vector2? ApsisPointForTest(bool apo) => _hud.ApsisPointForTest(apo);
    public bool ForecastBusyForTest => _forecastJob != null || (_forecastDirty && Screen is Screen.Orbit or Screen.Plan or Screen.Flight);
}

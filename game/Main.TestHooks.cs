using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

public partial class Main
{
    public void GoBrokeForTest()
    {
        Save!.Credits = 0;
        Save.HeldFuel = 0.1;
        WriteSave();
        Board();
    }

    public void DockAtDepotForTest()
    {
        var v = V!;
        var depot = G!.Depots[0];
        var st = G.StationAt(depot);
        v.EnterFtl();
        v.PlaceForTest(G.Stars[depot].Position + st.Position(v.AbsoluteTime) * Sublight.Scale, st.Velocity(v.AbsoluteTime));
        v.Descend();
        v.AdvanceSublight(0.01, Vec3d.Zero);
        Dock();
    }

    /// <summary>Takes a job here not bound for the given star, with the port shut.</summary>
    public void TakeMissionForTest(int notTo)
    {
        var s = Save!;
        Missions.Take(s, Offers.First(m => m.Target != notTo));
        SteerForCourse();
        V!.Restock(new ShipState(s.HeldFuel, s.HeldHull, s.HeldLife), s.Hold);
        WriteSave();
    }

    public void PlaceInTargetWellForTest()
    {
        var s = G!.Stars[V!.TargetStar];
        var r = G.StationAt(s.Id).Radius * 0.6;
        var speed = Math.Sqrt(Sublight.Mu(s) / r) * 1.1;
        V.PlaceForTest(V.TruePositions[s.Id] + new Vec3d(r * Sublight.Scale, 0, 0), new Vec3d(0, 0, speed));
        V.RefillForTest();
    }
    public Vector2 OrbitPointForTest(Vec3d p) => _hud.OrbitPoint(p);
    public Vector2 SystemPointForTest(int star, Vec3d p) => _hud.SystemPoint(star, p);
    public Vector2 HandleTipForTest(int i) => _hud.HandleTip(i);
    public int HandleCountForTest => _hud.HandleCount;
    public int MarkerCountForTest => _hud.MarkerCount;
    public string ClockTextForTest => _hud.ClockTextForTest;
    public Vector2 CompassMarkerForTest(ViewHold h) => _hud.CompassMarkerForTest(h);
    public Vector2 DeleteButtonForTest => _hud.DeleteButton(Selected!)!.Value;
    public void ReloadForTest() => Continue();
    public Hud HudForTest => _hud;
}

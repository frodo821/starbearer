using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>Taps and drags on the orbit chart: picking, placing nodes, and the node handles.</summary>
public partial class Hud
{
    public void OrbitTap(Vector2 at)
    {
        var v = Game.V!;
        foreach (var n in v.Nodes)
            if (NodePoint(n) is { } np && np.DistanceTo(at) < 14) { Game.SelectNode(n); return; }
        // An apsis marker puts a node right on it.
        if (ApsisAt(at) is { } apsis) { Game.AddNode(apsis); return; }
        // A tap on a station not yet the target picks it: in a system with several, that is how the pilot
        // chooses one. The target itself is left to the path it sits on.
        if (StationUnder(at) is { } station && station != v.Goal) { Game.SetTarget(station); return; }
        // A tap on a body targets it; on the target again, lets it go.
        if (OrbiterAt(at) is { } picked) { Game.SetTarget(picked == v.Goal ? null : picked); return; }
        if (Game.SubForecast is { } fc && EarliestNear(fc, at, 14) is { } time) { Game.AddNode(time); return; }
        Game.HandlesOpen = false;
    }

    /// <summary>The time on the path nearest a screen point, on the first pass that comes within reach:
    /// where the path overlaps itself, a tap means the soonest one.</summary>
    double? EarliestNear(OrbitForecast fc, Vector2 at, float reach)
    {
        double? time = null;
        var closest = reach;
        foreach (var (point, when) in PathAhead(fc))
        {
            var d = point.DistanceTo(at);
            if (d < closest) { closest = d; time = when; }
            else if (time != null && d >= reach) break;
        }
        return time;
    }

    /// <summary>Screen position of the delete button in the node's panel.</summary>
    public Vector2? DeleteButton(Maneuver n) => NodePanelBox(n) is { } box ? NodeDeleteBox(box).GetCenter() : null;

    const float HandleReach = 56f, DvPerPixel = 0.0005f;
    Maneuver? _heldNode;
    Vector2 _heldAxis;
    int _heldKind;   // 0 prograde, 1 radial, 2 normal, 3 slides along the path

    public bool OrbitHolding => _heldNode != null;

    (Vector2 Dir, Color Color, string Label, int Kind, float Sign, float Reach)[] Handles(Maneuver n)
    {
        if (StageOf(n) is not { } s) return Array.Empty<(Vector2, Color, string, int, float, float)>();
        var (t, r, nor) = Sublight.Frame(s.Position, s.Velocity);
        var map = LegMap(Game.SubForecast!.LegOf(n)!);
        var o = map(s.Position);
        var unit = 1.0 / Game.Chart.PixelsPerAu;
        var list = new List<(Vector2, Color, string, int, float, float)>();
        void Add(Vec3d axis, Color c, string label, int kind, float sign)
        {
            var screen = map(s.Position + axis * unit) - o;
            if (screen.Length() < 0.2f) return;                  // pointing at the camera: unusable
            list.Add((screen.Normalized(), c, label, kind, sign, HandleReach * Mathf.Clamp(screen.Length(), 0.45f, 1f)));
        }
        var green = new Color(0.45f, 1f, 0.6f);
        var blue = new Color(0.45f, 0.8f, 1f);
        var magenta = new Color(1f, 0.5f, 0.9f);
        Add(t, green, "順行", 0, 1); Add(-t, green, "逆行", 0, -1);
        Add(r, blue, "外", 1, 1); Add(-r, blue, "内", 1, -1);
        Add(nor, magenta, "法線", 2, 1); Add(-nor, magenta, "反法線", 2, -1);
        return list.ToArray();
    }

    public void OrbitPress(Vector2 at)
    {
        if (!Game.HandlesOpen || Game.Selected is not { } n || NodePoint(n) is not { } np) return;
        foreach (var h in Handles(n))
            if ((np + h.Dir * h.Reach).DistanceTo(at) < 14)
            {
                _heldNode = n; _heldAxis = h.Dir * h.Sign; _heldKind = h.Kind;
                return;
            }
        if (np.DistanceTo(at) < 14) { _heldNode = n; _heldKind = 3; }
    }

    public void OrbitHoldMove(InputEventMouseMotion mm)
    {
        var n = _heldNode!;
        if (_heldKind == 3)
        {
            var fc = Game.SubForecast;
            if (fc == null) return;
            n.Time = PathAhead(fc).OrderBy(t => t.At.DistanceSquaredTo(mm.Position)).First().Time;
        }
        else
        {
            var scale = DvPerPixel * (Input.IsKeyPressed(Key.Shift) ? 0.1f : 1f);
            var change = mm.Relative.Dot(_heldAxis) * scale;
            if (_heldKind == 0) n.Prograde += change;
            else if (_heldKind == 1) n.Radial += change;
            else n.Normal += change;
        }
        Game.NodeEdited();
    }

    public void OrbitRelease() => _heldNode = null;
    public Vector2 HandleTip(int i) { var h = Handles(Game.Selected!)[i]; return NodePoint(Game.Selected!)!.Value + h.Dir * h.Reach; }
    public int HandleCount => Game.Selected != null ? Handles(Game.Selected).Length : 0;
}

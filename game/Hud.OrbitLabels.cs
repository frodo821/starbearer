using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>
/// The orbit's markers as KSP shows them: the mark alone, its figures when the pointer is on it, and kept
/// on by a tap (a second tap takes them off). Markers are known by kind and order along the path (the
/// first Ap ahead, the second...), so a pinned one stays pinned while the plan is reshaped.
/// And what a double tap puts the chart's pivot on.
/// </summary>
public partial class Hud
{
    readonly HashSet<string> _pinned = new();
    readonly List<(string Key, Vector2 At)> _markers = new();
    readonly Dictionary<string, int> _markerCount = new();
    Vector2 _pointer = new(-1e4f, -1e4f);

    public void OrbitHover(Vector2 at) => _pointer = at;

    /// <summary>Starts a frame's markers afresh.</summary>
    void ResetMarkers() { _markers.Clear(); _markerCount.Clear(); }

    /// <summary>Registers a marker of a kind at a point; returns its key and whether its figures show.</summary>
    (string Key, bool Shown) Marker(string kind, Vector2 at)
    {
        var n = _markerCount.GetValueOrDefault(kind);
        _markerCount[kind] = n + 1;
        var key = kind + n;
        _markers.Add((key, at));
        return (key, _pinned.Contains(key) || at.DistanceTo(_pointer) < 14);
    }

    string? MarkerAt(Vector2 at) =>
        _markers.Where(m => m.At.DistanceTo(at) < 12).OrderBy(m => m.At.DistanceTo(at)).Select(m => m.Key).FirstOrDefault();

    void TogglePin(string key) { if (!_pinned.Remove(key)) _pinned.Add(key); }

    public bool PinnedForTest(string key) => _pinned.Contains(key);
    public bool MarkerNearForTest(Vector2 at, float reach) => _markers.Any(m => m.At.DistanceTo(at) < reach);

    /// <summary>What the last tap did, so a double tap that follows can take it back before it refocuses.</summary>
    (Vector2 At, ulong When, Action Undo)? _lastTap;

    void Tapped(Vector2 at, Action undo) => _lastTap = (at, Time.GetTicksMsec(), undo);

    public void UndoTap(Vector2 at)
    {
        if (_lastTap is { } t && t.At.DistanceTo(at) < 12 && Time.GetTicksMsec() - t.When < 800) t.Undo();
        _lastTap = null;
    }

    /// <summary>What a double tap at a point would put the pivot on: a node, the ship, a body or the star.</summary>
    public ChartFocus? FocusAt(Vector2 at)
    {
        var v = Game.V!;
        if (Game.Chart.PixelsPerAu < 35) return null;
        foreach (var n in v.Nodes)
            if (NodePoint(n) is { } np && np.DistanceTo(at) < 14) return new FocusNode(n);
        if (OrbitPoint(v.SubPosition).DistanceTo(at) < 12) return new FocusShip();
        if (OrbiterAt(at) is Center body) return new FocusBody(body);
        if (StarPoint(Vec3d.Zero).DistanceTo(at) < 14) return new FocusBody(Game.G!.System(v.SystemStar));
        return null;
    }

    /// <summary>Where a node is on the chart, in ly: on the body its leg goes round, where that body will be.</summary>
    public Vec3d? NodeOnChart(Maneuver n)
    {
        var v = Game.V!;
        if (Game.SubForecast?.LegOf(n) is not { } leg || StageOf(n) is not { } s) return null;
        if (leg.Center!.Key == v.Primary.Key)
            return v.System.Position + (v.Primary.StarPosition(v.AbsoluteTime) + s.Position) * Sublight.ChartLyPerAu;
        return v.ChartPositions[leg.Star] + (leg.Center.StarPosition(leg.Track[0].Time) + s.Position) * Sublight.ChartLyPerAu;
    }

    /// <summary>The focus, named under the chart, with the way back to the ship.</summary>
    void DrawFocus()
    {
        if (Game.FocusName is not { } name) return;
        var text = $"視点の中心: {name}    R で自機へ";
        var w = F.GetStringSize(text, HorizontalAlignment.Left, -1, 12).X;
        Text(new Vector2(Size.X / 2 - w / 2, ClusterTop - 6), text, Palette.Dim, 12);
    }
}

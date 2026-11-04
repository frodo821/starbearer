using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>What the orbit chart turns about and follows, as KSP's map focus: a body, the ship, or a node.</summary>
public abstract record ChartFocus;
public sealed record FocusBody(Center Body) : ChartFocus;
public sealed record FocusShip : ChartFocus;
public sealed record FocusNode(Maneuver Node) : ChartFocus;

/// <summary>
/// The orbit chart's focus. Unset, it follows the ship's primary, as it always has; a double tap on a
/// body, the ship or a node (or the body's menu) puts the pivot on it and keeps it there as it moves.
/// R gives the ship back the focus. A focus that goes away (a node deleted, another system) lets go.
/// </summary>
public partial class Main
{
    public ChartFocus? Focus { get; private set; }

    /// <summary>Where the focus is on the chart now, in ly; null when it is unset or gone.</summary>
    Vec3d? FocusPoint()
    {
        var v = V!;
        var t = v.AbsoluteTime;
        return Focus switch
        {
            FocusBody { Body: var b } when b.Star == v.SystemStar => v.System.Position + b.StarPosition(t) * Sublight.ChartLyPerAu,
            FocusShip => PrimaryOnChart + v.SubPosition * Sublight.ChartLyPerAu,
            FocusNode { Node: var n } when v.Nodes.Contains(n) => _hud.NodeOnChart(n),
            _ => null,
        };
    }

    public void SetFocus(ChartFocus? focus)
    {
        Focus = focus;
        var at = FocusPoint();
        if (at == null) Focus = null;
        Chart.Refocus(at ?? PrimaryOnChart);
        Message = Focus switch
        {
            FocusBody { Body: var b } => $"視点の中心: {b.Name}",
            FocusShip => "視点の中心: 自機",
            FocusNode n => $"視点の中心: ノード {V!.Nodes.OrderBy(m => m.Time).ToList().IndexOf(n.Node) + 1}",
            _ => "視点の中心: 主天体",
        };
    }

    public string? FocusName => Focus switch
    {
        FocusBody { Body: var b } => b.Name,
        FocusShip => "自機",
        FocusNode n => $"ノード {V!.Nodes.OrderBy(m => m.Time).ToList().IndexOf(n.Node) + 1}",
        _ => null,
    };

    /// <summary>Each frame in orbit: the chart follows the focus, or failing one, the ship's primary.</summary>
    void FollowFocus()
    {
        var v = V!;
        if (Focus != null)
        {
            if (FocusPoint() is { } at) { Chart.Follow(at, true); return; }
            Focus = null;   // gone: back to following the primary, from where the view stands
            Chart.Refocus(PrimaryOnChart, glide: false);
        }
        Chart.Follow(PrimaryOnChart, v.Primary.Parent != null);
    }
}

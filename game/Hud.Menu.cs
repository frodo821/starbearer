using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>One choice in a chart menu. A choice with no action is shown dark.</summary>
public readonly record struct MenuItem(string Label, Action? Act);

/// <summary>
/// The menu a right click (a two-finger click on a trackpad) opens on the chart: what can be done
/// with the body, station or star under the pointer, without going down to the console.
/// </summary>
public partial class Hud
{
    const float MenuWidth = 190, MenuRow = 26, MenuTitle = 28;

    /// <summary>The open menu, and the view it belongs to: it closes if that view goes away.</summary>
    (Vector2 At, string Title, MenuItem[] Items, Screen On, bool Map, bool Sky)? _menu;

    public bool MenuOpen => _menu is { } m && m.On == Game.Screen && m.Map == Game.MapOpen && m.Sky == Game.OrbitSky;

    void OpenMenu(Vector2 at, string title, IEnumerable<MenuItem> items)
    {
        var list = items.ToArray();
        if (list.Length == 0) { _menu = null; return; }
        // Kept on screen, clear of the console.
        var height = MenuTitle + list.Length * MenuRow + 6;
        var x = Mathf.Min(at.X, Size.X - MenuWidth - 8);
        var y = Mathf.Min(at.Y, Size.Y - ConsoleHeight - height - 8);
        _menu = (new Vector2(x, y), title, list, Game.Screen, Game.MapOpen, Game.OrbitSky);
    }

    public void CloseMenu() => _menu = null;

    Rect2 MenuItemBox(int i) => new(_menu!.Value.At + new Vector2(3, MenuTitle + i * MenuRow), MenuWidth - 6, MenuRow - 2);

    /// <summary>A click while the menu is open: on a choice it is taken; anywhere else the menu just closes.</summary>
    public void MenuClick(Vector2 at)
    {
        if (!MenuOpen) { _menu = null; return; }
        var items = _menu!.Value.Items;
        for (int i = 0; i < items.Length; i++)
            if (MenuItemBox(i).HasPoint(at))
            {
                _menu = null;
                items[i].Act?.Invoke();
                return;
            }
        _menu = null;
    }

    void DrawMenu()
    {
        if (!MenuOpen) return;
        var (at, title, items, _, _, _) = _menu!.Value;
        var box = new Rect2(at, MenuWidth, MenuTitle + items.Length * MenuRow + 6);
        DrawRect(box, new Color(0.02f, 0.04f, 0.08f, 0.95f));
        DrawRect(box, new Color(Ink, 0.6f), false, 1);
        Text(at + new Vector2(10, 20), title, Good, 15);
        var mouse = GetLocalMousePosition();
        for (int i = 0; i < items.Length; i++)
        {
            var r = MenuItemBox(i);
            var live = items[i].Act != null;
            if (live && r.HasPoint(mouse)) DrawRect(r, new Color(Ink, 0.15f));
            Text(r.Position + new Vector2(8, 18), items[i].Label, live ? Ink : new Color(Dim, 0.4f), 14);
        }
    }

    /// <summary>What can be done with a body or the station: target it, and plan a way there.</summary>
    IEnumerable<MenuItem> OrbiterMenu(IOrbiter o)
    {
        var v = Game.V!;
        yield return o == v.Goal ? new("目標を解除", () => Game.SetTarget(null)) : new("目標にする", () => Game.SetTarget(o));
        yield return new("遷移案", () => Game.PlanFor(o, Key.H));
        if (o is Station) yield return new("合流案", () => Game.PlanFor(o, Key.M));
        if (Game.Owns(Modules.Transfer)) yield return new("計算機で計画", () => Game.PlanFor(o, Key.T));
    }

    /// <summary>What can be done with a star: make it the destination, or, while planning a jump, aim at it.</summary>
    IEnumerable<MenuItem> StarMenu(int star)
    {
        var v = Game.V!;
        if (star == v.TargetStar && Game.FreeCourse) yield return new("目的地を解除", Game.ClearCourse);
        else if (star != v.TargetStar) yield return new("目的地にする", () => Game.SetCourse(star));
        if (Game.Screen == Screen.Plan) yield return new("この星へ方位を合わせる", () => Game.SetPlanDir(v.ChartPositions[star] - v.EstimatedPosition));
    }

    string StarTitle(int star)
    {
        var g = Game.G!;
        return g.Stars[star].Name + (g.Ports.Contains(star) ? " ◆" : g.Depots.Contains(star) ? " ◇" : "")
            + (star == Game.V!.TargetStar ? "  目的地" : "");
    }

    /// <summary>A right click on the galaxy map.</summary>
    public void MapMenu(Vector2 at)
    {
        if (PickStar(at) is int star && star != CurrentStar()) OpenMenu(at, StarTitle(star), StarMenu(star));
        else _menu = null;
    }

    /// <summary>A right click on the orbit chart: the station, a body, or zoomed out, a star.</summary>
    public void OrbitMenu(Vector2 at)
    {
        var v = Game.V!;
        if (v.LocalStation is { } st && StationPoint() is { } sp && sp.DistanceTo(at) < 14)
            OpenMenu(at, v.SystemStar == v.TargetStar ? "目的港" : "港", OrbiterMenu(st));
        else if (OrbiterAt(at) is Center body)
            OpenMenu(at, body.Name, OrbiterMenu(body));
        else if (Game.Chart.PixelsPerAu < 35 && PickStar(at) is int star && star != v.SystemStar)
            OpenMenu(at, StarTitle(star), StarMenu(star));
        else _menu = null;
    }

    public Vector2? MenuItemForTest(string label) =>
        MenuOpen && Array.FindIndex(_menu!.Value.Items, x => x.Label == label) is var i and >= 0 ? MenuItemBox(i).GetCenter() : null;
}

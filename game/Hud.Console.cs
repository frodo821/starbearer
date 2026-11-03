using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>One button. Pressing it is the same as pressing its key (with Shift, if Shift is set).</summary>
public readonly record struct Command(Key Key, string Cap, string Name, bool Enabled = true, bool Hold = false, bool Shift = false);

/// <summary>
/// Buttons and the panels they sit in, gathered by purpose around the edges: time at the top left,
/// navigation down the left, the ship at the top right, the helm at the bottom right, and along the
/// bottom only what moves the ship from one kind of travel to another, with the advice line above.
/// A button drawn anywhere lights up when the advice line points at its key.
/// </summary>
public partial class Hud
{
    const float ConsoleHeight = 50, ButtonHeight = 28, ButtonGap = 5, PanelPad = 12;
    readonly List<(Rect2 Box, Command Command)> _buttons = new();
    readonly List<(Rect2 Box, Action Act)> _clicks = new();
    readonly List<Rect2> _panels = new();
    Key? _lit;
    Rect2 ConsoleBand => new(0, Size.Y - ConsoleHeight, Size.X, ConsoleHeight);
    static readonly Color PanelFill = new(0.01f, 0.02f, 0.05f, 0.78f);

    /// <summary>Where the advice line is centred: between the navigation panel and the helm.</summary>
    float AdviceCentre => (NavRight + HelmLeft) / 2;

    void ClearButtons()
    {
        _buttons.Clear(); _clicks.Clear(); _panels.Clear();
        _lit = Game.Screen is Screen.Orbit or Screen.Plan or Screen.Flight && Game.V != null ? Game.Advice().Key : null;
    }

    float ButtonWidth(Command c) =>
        F.GetStringSize(c.Name, HorizontalAlignment.Left, -1, 14).X + (c.Cap == "" ? 0 : F.GetStringSize(c.Cap, HorizontalAlignment.Left, -1, 11).X + 6) + 16;

    /// <summary>Draws a button for a command and takes taps on it; returns its width.</summary>
    float Button(Vector2 at, Command c, float? width = null)
    {
        var box = new Rect2(at, width ?? ButtonWidth(c), ButtonHeight);
        _buttons.Add((box, c));
        var lit = c.Key == _lit && c.Enabled;
        var col = !c.Enabled ? new Color(Dim, 0.25f) : lit ? Warn : Ink;
        if (lit) DrawRect(box, new Color(Warn, 0.12f + 0.2f * (0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 180f))));
        if (Game.HeldButton == c.Key && c.Hold) DrawRect(box, new Color(Ink, 0.35f));
        DrawRect(box, col, false, lit ? 2 : 1);
        Text(box.Position + new Vector2(8, 19), c.Name, col, 14);
        if (c.Cap != "")
        {
            var cap = F.GetStringSize(c.Cap, HorizontalAlignment.Left, -1, 11).X;
            Text(box.Position + new Vector2(box.Size.X - cap - 6, 18), c.Cap, new Color(col, col.A * 0.6f), 11);
        }
        return box.Size.X;
    }

    /// <summary>Buttons left to right from a point; returns where the row ends.</summary>
    float ButtonRow(float x, float y, IEnumerable<Command> commands)
    {
        foreach (var c in commands) x += Button(new Vector2(x, y), c) + ButtonGap;
        return x;
    }

    /// <summary>The same, ending at a right edge.</summary>
    void ButtonRowRight(float right, float y, IReadOnlyList<Command> commands) =>
        ButtonRow(right - commands.Sum(c => ButtonWidth(c) + ButtonGap) + ButtonGap, y, commands);

    float RowWidth(IEnumerable<Command> commands) => commands.Sum(c => ButtonWidth(c) + ButtonGap) - ButtonGap;

    /// <summary>A small button that runs an action rather than a key.</summary>
    void ActionButton(Rect2 box, string label, Action act, Color col, bool enabled = true)
    {
        if (enabled) _clicks.Add((box, act));
        var ink = enabled ? col : new Color(Dim, 0.25f);
        DrawRect(box, ink, false, 1);
        var w = F.GetStringSize(label, HorizontalAlignment.Left, -1, 13).X;
        Text(new Vector2(box.GetCenter().X - w / 2, box.Position.Y + box.Size.Y / 2 + 5), label, ink, 13);
    }

    /// <summary>A panel's backing, with its title; taps on it never reach the chart beneath.</summary>
    void Panel(Rect2 box, string title, bool opaque = false)
    {
        _panels.Add(box);
        DrawRect(box, opaque ? new Color(PanelFill, 1) : PanelFill);
        DrawRect(box, new Color(Ink, 0.18f), false, 1);
        if (title != "") Text(box.Position + new Vector2(PanelPad, 17), title, new Color(Dim, 0.9f), 12);
    }

    /// <summary>A section's heading inside a panel.</summary>
    void Section(float x, ref float y, float width, string title)
    {
        y += 6;
        Text(new Vector2(x, y + 12), title, new Color(Dim, 0.9f), 12);
        DrawLine(new Vector2(x + F.GetStringSize(title, HorizontalAlignment.Left, -1, 12).X + 8, y + 7), new Vector2(x + width, y + 7), new Color(Ink, 0.15f), 1);
        y += 20;
    }

    /// <summary>The bottom band: moving between kinds of travel, and the advice line above it.</summary>
    void DrawConsole()
    {
        var band = ConsoleBand;
        DrawRect(band, new Color(0.01f, 0.02f, 0.05f, 0.85f));
        DrawLine(band.Position, band.Position + new Vector2(band.Size.X, 0), new Color(Ink, 0.4f), 1);
        float x = 12, y = band.Position.Y + (ConsoleHeight - ButtonHeight) / 2;
        foreach (var (group, commands) in Game.BarCommands())
        {
            if (group != "") { Text(new Vector2(x, y + 18), group, Dim, 11); x += F.GetStringSize(group, HorizontalAlignment.Left, -1, 11).X + 6; }
            x = ButtonRow(x, y, commands) + 14;
        }

        var (lit, advice) = Game.Advice();
        var size = 18;
        var w = F.GetStringSize(advice, HorizontalAlignment.Left, -1, size).X;
        var cx = Mathf.Clamp(AdviceCentre, w / 2 + 10, Size.X - w / 2 - 10);
        Text(new Vector2(cx - w / 2, Size.Y - ConsoleHeight - 12), advice, lit != null ? Warn : Ink, size);
        if (Game.Message != "")
        {
            var mw = F.GetStringSize(Game.Message, HorizontalAlignment.Left, -1, 15).X;
            var mx = Mathf.Clamp(AdviceCentre, mw / 2 + 10, Size.X - mw / 2 - 10);
            Text(new Vector2(mx - mw / 2, Size.Y - ConsoleHeight - 38), Game.Message, Ink, 15);
        }
    }

    /// <summary>A press on a button or a panel never reaches the chart or the sky underneath.</summary>
    public bool ConsoleTap(Vector2 at)
    {
        if (Game.Screen is not (Screen.Orbit or Screen.Plan or Screen.Flight)) return false;
        foreach (var (box, act) in _clicks)
            if (box.HasPoint(at)) { act(); return true; }
        foreach (var (box, c) in _buttons)
            if (box.HasPoint(at)) { if (c.Enabled) Game.PressButton(c); return true; }
        return ConsoleBand.HasPoint(at) || _panels.Any(p => p.HasPoint(at));
    }

    public Vector2 ButtonForTest(Key key) => _buttons.First(b => b.Command.Key == key).Box.GetCenter();
    public bool HasButtonForTest(Key key) => _buttons.Any(b => b.Command.Key == key);
}

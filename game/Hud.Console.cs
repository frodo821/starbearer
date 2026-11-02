using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>One button on the console. Pressing it is the same as pressing its key.</summary>
public readonly record struct Command(Key Key, string Cap, string Name, bool Enabled = true, bool Hold = false);

/// <summary>The console along the bottom edge: groups of buttons, and the advice line above them.</summary>
public partial class Hud
{
    const float ConsoleHeight = 92, ButtonHeight = 30, ButtonGap = 5;
    readonly List<(Rect2 Box, Command Command)> _buttons = new();
    Rect2 ConsoleBand => new(0, Size.Y - ConsoleHeight, Size.X, ConsoleHeight);

    float ButtonWidth(Command c) =>
        F.GetStringSize(c.Name, HorizontalAlignment.Left, -1, 14).X + F.GetStringSize(c.Cap, HorizontalAlignment.Left, -1, 11).X + 20;

    void DrawConsole()
    {
        var band = ConsoleBand;
        DrawRect(band, new Color(0.01f, 0.02f, 0.05f, 0.82f));
        DrawLine(band.Position, band.Position + new Vector2(band.Size.X, 0), new Color(Ink, 0.4f), 1);
        var (lit, advice) = Game.Advice();
        var pulse = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 180f);

        _buttons.Clear();
        float x = 12, y = band.Position.Y + 10;
        foreach (var (group, commands) in Game.Commands())
        {
            var width = commands.Sum(c => ButtonWidth(c) + ButtonGap);
            if (x + width > Size.X - 12) { x = 12; y += ButtonHeight + 8; }
            Text(new Vector2(x, y + 19), group, Dim, 11);
            x += F.GetStringSize(group, HorizontalAlignment.Left, -1, 11).X + 6;
            foreach (var c in commands)
            {
                var box = new Rect2(x, y, ButtonWidth(c), ButtonHeight);
                _buttons.Add((box, c));
                var col = !c.Enabled ? new Color(Dim, 0.25f) : c.Key == lit ? Warn : Ink;
                if (c.Key == lit) DrawRect(box, new Color(Warn, 0.12f + 0.2f * pulse));
                if (Game.HeldButton == c.Key) DrawRect(box, new Color(Ink, 0.35f));
                DrawRect(box, col, false, c.Key == lit ? 2 : 1);
                Text(box.Position + new Vector2(7, 20), c.Name, col, 14);
                var cap = F.GetStringSize(c.Cap, HorizontalAlignment.Left, -1, 11).X;
                Text(box.Position + new Vector2(box.Size.X - cap - 6, 19), c.Cap, new Color(col, col.A * 0.6f), 11);
                x += box.Size.X + ButtonGap;
            }
            x += 12;
        }

        Centered(Size.Y - ConsoleHeight - 12, advice, lit != null ? Warn : Ink, 18);
        if (Game.Message != "") Centered(Size.Y - ConsoleHeight - 38, Game.Message, Ink, 15);
    }

    /// <summary>A press on the band never reaches the chart underneath.</summary>
    public bool ConsoleTap(Vector2 at)
    {
        if (Game.Screen is not (Screen.Orbit or Screen.Plan or Screen.Flight) || !ConsoleBand.HasPoint(at)) return false;
        foreach (var (box, c) in _buttons)
            if (c.Enabled && box.HasPoint(at)) Game.PressButton(c);
        return true;
    }

    public Vector2 ButtonForTest(Key key) => _buttons.First(b => b.Command.Key == key).Box.GetCenter();
}

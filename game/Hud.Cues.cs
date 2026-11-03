using System;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>Cues for the ship's state: the corner frame, the streaks, and the full-screen transitions.</summary>
public partial class Hud
{

    /// <summary>What the ship is doing, in the one colour that also rims the screen.</summary>
    (string Label, Color Colour) ShipState()
    {
        var v = Game.V!;
        var planning = Game.Screen == Screen.Plan;
        var well = v.Phase != Phase.Sublight && v.NearestWell(out _) < double.MaxValue;
        var speed = (v.Phase == Phase.Sublight ? v.JumpState.Velocity : v.Velocity).Length;
        return v.Phase switch
        {
            Phase.Sublight when planning && v.Docked => ("港に停泊中  発進を計画中", Warn),
            Phase.Sublight when planning => ("亜光速  FTL発進を計画中", Warn),
            Phase.Sublight when v.Docked => ("港に停泊中", Good),
            Phase.Sublight when v.Adrift => ("亜光速  恒星間を慣性航行", Warn),
            Phase.Sublight => ("亜光速  軌道航行中", Good),
            Phase.Stalled => ("FTL 脱落  星間で停止", Bad),
            _ when planning => ("FTL 航行中  計画のため時間停止", Warn),
            _ when well => ("FTL 恒星の井戸  D で降下", Warn),
            _ when speed < Tuning.StallSpeed * 1.3 => ("FTL 巡航  失速間近", Bad),
            _ => ("FTL 巡航", Ftl),
        };
    }

    /// <summary>Corner brackets in the state colour. Danger pulses, so it reads without looking at a gauge.</summary>
    void DrawStateFrame()
    {
        var (_, col) = ShipState();
        var pulse = col == Bad ? 0.55f + 0.45f * Mathf.Sin(Time.GetTicksMsec() / 120f) : 0.8f;
        col = new Color(col, pulse);
        const float m = 5, arm = 70;
        var bottom = Size.Y - ConsoleHeight - 4;
        foreach (var (corner, dx, dy) in new[] { (new Vector2(m, m), 1f, 1f), (new Vector2(Size.X - m, m), -1f, 1f), (new Vector2(m, bottom), 1f, -1f), (new Vector2(Size.X - m, bottom), -1f, -1f) })
        {
            DrawLine(corner, corner + new Vector2(arm * dx, 0), col, 2);
            DrawLine(corner, corner + new Vector2(0, arm * dy), col, 2);
        }
        var heat = (float)Math.Clamp(Game.V!.Heat, 0, 1.5) / 1.5f;
        if (Game.V.Heat > Game.V.HeatTolerance)
            DrawRect(new Rect2(0, 0, Size.X, Size.Y - ConsoleHeight), new Color(Bad, 0.5f * heat), false, 14 + 30 * heat);
    }

    /// <summary>Stars pulled into lines, longer with speed. They fade as the drive nears its stall.</summary>
    void DrawStreaks(float boost = 0)
    {
        var v = Game.V!;
        var speed = v.Phase == Phase.Flight ? (float)v.Velocity.Length : 0;
        var power = Mathf.Clamp((speed - (float)Tuning.StallSpeed) / 0.6f, 0, 1) + boost;
        if (power <= 0.01f) return;
        var centre = (Size - new Vector2(0, ConsoleHeight)) / 2;
        var reach = centre.Length();
        var clock = Time.GetTicksMsec() / 1000f;
        for (int i = 0; i < 70; i++)
        {
            var angle = i * 2.399963f;
            var phase = (clock * (0.35f + 0.5f * power) + i * 0.137f) % 1;
            var r0 = reach * (0.12f + phase * phase);
            var r1 = r0 + reach * 0.16f * power * (0.3f + phase);
            var dir = Vector2.FromAngle(angle);
            DrawLine(centre + dir * r0, centre + dir * r1, new Color(Ftl, Mathf.Min(0.55f, 0.15f + 0.4f * power) * phase), 1.2f, true);
        }
    }

    /// <summary>A change of state gets a moment of its own, so it can't be missed.</summary>
    void DrawTransition()
    {
        if (Game.Cue is not { } cue) return;
        var p = Mathf.Clamp(Game.CueAge / cue.Seconds, 0, 1);
        var fade = Mathf.Min(1, (1 - p) * 4);
        var centre = (Size - new Vector2(0, ConsoleHeight)) / 2;
        var words = new Vector2(Size.X / 2, Mathf.Min(centre.Y, ClusterTop - 80));   // the title over the helm, not on it
        if (cue.Major)
        {
            DrawRect(new Rect2(Vector2.Zero, Size - new Vector2(0, ConsoleHeight)), new Color(cue.Colour, 0.45f * Mathf.Pow(1 - p, 3)));
            DrawStreaks(1.2f * (1 - p));
            var r = (cue.Outward ? p : 1 - p) * centre.Length();
            DrawArc(centre, r, 0, Mathf.Tau, 64, new Color(cue.Colour, 0.8f * fade), 2, true);
            DrawArc(centre, r * 0.7f, 0, Mathf.Tau, 64, new Color(cue.Colour, 0.4f * fade), 1, true);
            var w = F.GetStringSize(cue.Title, HorizontalAlignment.Left, -1, 44).X + 60;
            var box = new Rect2((Size.X - w) / 2, words.Y - 60, w, 76);
            DrawRect(box, new Color(0, 0, 0, 0.6f * fade));
            DrawRect(box, new Color(cue.Colour, fade), false, 2);
            Centered(words.Y - 6, cue.Title, new Color(cue.Colour, fade), 44);
            Centered(words.Y + 44, cue.Detail, new Color(Ink, fade), 18);
        }
        else
        {
            Centered(112, cue.Title, new Color(cue.Colour, fade), 22);
            if (cue.Detail != "") Centered(136, cue.Detail, new Color(Ink, fade), 15);
        }
    }
}

using System;
using Godot;

namespace Starbearer;

/// <summary>Building blocks the windows (port, manifest) share: labels, buttons, rows and the panel frame.</summary>
public static class Ui
{
    /// <summary>Lighter than the HUD's dim, for small text on an opaque panel.</summary>
    public static readonly Color PanelDim = new(0.6f, 0.68f, 0.8f);

    public static Label Text(string s, int size = 15, Color? c = null)
    {
        var l = new Label { Text = s };
        l.AddThemeFontSizeOverride("font_size", size);
        if (c is { } col) l.AddThemeColorOverride("font_color", col);
        return l;
    }

    /// <summary>The keyboard stays with the game, so Enter and the number keys work with a window open.</summary>
    public static Button Press(string s, Action act, bool enabled = true, string key = "", int size = 0)
    {
        var b = new Button { Text = key == "" ? s : $"{s}  [{key}]", FocusMode = Control.FocusModeEnum.None, Disabled = !enabled };
        if (size > 0) b.AddThemeFontSizeOverride("font_size", size);
        b.Pressed += act;
        return b;
    }

    /// <summary>Text that takes the free width, then the controls after it.</summary>
    public static HBoxContainer Row(Control text, params Control[] tail)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(text);
        foreach (var c in tail) { c.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(c); }
        return row;
    }

    public static StyleBoxFlat Frame(float opacity, int marginX, int marginY) => new()
    {
        BgColor = new Color(0.02f, 0.035f, 0.07f, opacity), BorderColor = new Color(Palette.Good, 0.6f),
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        ContentMarginLeft = marginX, ContentMarginRight = marginX, ContentMarginTop = marginY, ContentMarginBottom = marginY,
    };

    public static void Clear(Node box)
    {
        foreach (var child in box.GetChildren()) { box.RemoveChild(child); child.QueueFree(); }
    }
}

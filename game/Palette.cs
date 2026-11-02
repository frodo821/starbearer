using Godot;

namespace Starbearer;

/// <summary>The colours the screens share.</summary>
public static class Palette
{
    public static readonly Color Ink = new(0.75f, 0.9f, 1f, 0.95f);
    public static readonly Color Dim = new(0.55f, 0.7f, 0.8f, 0.7f);
    public static readonly Color Warn = new(1f, 0.7f, 0.25f);
    public static readonly Color Bad = new(1f, 0.3f, 0.25f);
    /// <summary>Also normal space, in the state cues.</summary>
    public static readonly Color Good = new(0.5f, 1f, 0.7f);
    /// <summary>Faster than light, in the state cues.</summary>
    public static readonly Color Ftl = new(0.45f, 0.75f, 1f);
}

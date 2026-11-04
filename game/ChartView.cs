using System;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>
/// The camera on the one chart. The galaxy map and the orbit view are the same map: the orbit view
/// is the map looked at closely, in AU around a system. Positions are in ly, zoom in pixels per ly.
/// </summary>
public sealed class ChartView
{
    public const float SystemZoom = 425f, GalaxyZoom = 11f;

    public float Yaw = 0.6f, Pitch = 1.1f, Zoom = GalaxyZoom;
    /// <summary>The point everything turns and zooms around.</summary>
    public Vec3d Center;
    /// <summary>Moves land at once instead of gliding, so scripted checks see them in the same frame.</summary>
    public bool Instant;

    /// <summary>Where the chart is gliding to after a change of layer.</summary>
    (Vec3d Center, float Zoom)? _glide;
    /// <summary>Where the followed body was last frame.</summary>
    Vec3d _followed;

    /// <summary>Pixels per AU.</summary>
    public float PixelsPerAu => Zoom * (float)Sublight.ChartLyPerAu;

    /// <summary>A zoom that frames a body's sphere of influence, or the whole system for a star.</summary>
    public static float ZoomFor(Center c) =>
        c.Parent == null ? SystemZoom : Mathf.Min(1e9f, 220f / (float)(c.Edge * Sublight.ChartLyPerAu));

    public Vector2 Project(Vec3d p, Vector2 screen)
    {
        p -= Center;
        var cy = Mathf.Cos(Yaw); var sy = Mathf.Sin(Yaw);
        var cp = Mathf.Cos(Pitch); var sp = Mathf.Sin(Pitch);
        var x = (float)p.X * cy - (float)p.Z * sy;
        var z = (float)p.X * sy + (float)p.Z * cy;
        var y = (float)p.Y * cp - z * sp;
        return screen / 2 + new Vector2(x, -y) * Zoom;
    }

    /// <summary>Puts the chart on a view at once, as when boarding.</summary>
    public void Show(Vec3d center, float zoom)
    {
        Center = center;
        Zoom = zoom;
        _glide = null;
        _followed = center;
    }

    /// <summary>Moves the chart to a view over a moment, so the two scales read as one map.</summary>
    public void Glide(Vec3d center, float zoom)
    {
        if (Instant) { Center = center; Zoom = zoom; return; }
        _glide = (center, zoom);
    }

    public void StopGlide() => _glide = null;

    /// <summary>Follows another point from now: the chart glides onto it, or stays where it is and goes with it.</summary>
    public void Refocus(Vec3d point, bool glide = true)
    {
        _followed = point;
        if (glide) Glide(point, Zoom);
    }

    public void Update(double delta)
    {
        if (_glide is not { } to) return;
        var k = 1 - Math.Exp(-5 * delta);
        Center += (to.Center - Center) * k;
        Zoom *= Mathf.Pow(to.Zoom / Zoom, (float)k);
        if (Mathf.Abs(Zoom / to.Zoom - 1) < 0.01f && (Center - to.Center).Length * Zoom < 1) { Center = to.Center; Zoom = to.Zoom; _glide = null; }
    }

    /// <summary>A planet moves under the view; the chart goes with it so the ship's orbit holds still.</summary>
    public void Follow(Vec3d body, bool moves)
    {
        if (moves)
        {
            Center += body - _followed;
            if (_glide is { } g) _glide = g with { Center = g.Center + (body - _followed) };
        }
        _followed = body;
    }

    /// <summary>Slides the pivot in the plane of the screen, so the chart follows the fingers.</summary>
    public void Pan(Vector2 d)
    {
        StopGlide();
        var cy = Mathf.Cos(Yaw); var sy = Mathf.Sin(Yaw);
        var cp = Mathf.Cos(Pitch); var sp = Mathf.Sin(Pitch);
        var right = new Vec3d(cy, 0, -sy);
        var up = new Vec3d(-sy * sp, cp, -cy * sp);
        Center += (right * d.X - up * d.Y) * (1.0 / Zoom);
    }

    public void ZoomBy(float factor)
    {
        StopGlide();
        Zoom = Mathf.Clamp(Zoom * factor, 4, 1e9f);
    }

    public void Rotate(Vector2 d)
    {
        Yaw += d.X * 0.005f;
        Pitch = Mathf.Clamp(Pitch + d.Y * 0.005f, -1.55f, 1.55f);
    }
}

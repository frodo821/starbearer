using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>
/// The system's star, planets and moons out of the window. Each is a sphere at a fixed distance, sized
/// to the angle it fills from the ship, so distance never runs into the depth range of the scene.
/// </summary>
public partial class Worlds : Node3D
{
    const float Shell = 40f;
    const double Exaggeration = 2.5, SmallestAngle = 0.0035;
    static readonly SphereMesh Ball = new() { Radius = 0.5f, Height = 1f, RadialSegments = 96, Rings = 48 };
    readonly List<(Center Body, MeshInstance3D Mesh, ShaderMaterial Look)> _shown = new();
    int _system = -1;

    /// <summary>Colours and traits for one body, from its place in the system so it looks the same every visit.</summary>
    static ShaderMaterial Look(ulong seed, Center c, Star star, Shader shader)
    {
        var r = new Rng(seed * 6700417 + (ulong)c.Key);
        var mat = new ShaderMaterial { Shader = shader };
        void Set(string name, Color col) => mat.SetShaderParameter(name, new Vector3(col.R, col.G, col.B));
        Color Hue(double h, double s, double v) => Color.FromHsv((float)h, (float)s, (float)v);
        mat.SetShaderParameter("seed", (float)r.Range(0, 60));
        switch (c.Kind)
        {
            case BodyKind.Star:
                mat.SetShaderParameter("kind", 3);
                var hot = Sky.StarColor(star.Temperature);
                Set("high", hot.Lerp(Colors.White, 0.35f));
                Set("mid", hot.Darkened(0.35f));
                break;
            case BodyKind.Giant:
                mat.SetShaderParameter("kind", 1);
                var hue = r.Range(0.02, 0.62);
                Set("low", Hue(hue, r.Range(0.35, 0.7), 0.55));
                Set("mid", Hue(hue + r.Range(-0.05, 0.08), r.Range(0.15, 0.45), 0.85));
                Set("high", Hue(hue + 0.03, 0.12, 0.97));
                Set("air", Hue(hue, 0.4, 1));
                mat.SetShaderParameter("bands", (float)r.Range(7, 16));
                break;
            case BodyKind.Moon:
                mat.SetShaderParameter("kind", 2);
                var tint = r.Range(0, 0.15);
                Set("low", Hue(tint, r.Range(0, 0.2), r.Range(0.18, 0.3)));
                Set("high", Hue(tint, r.Range(0, 0.15), r.Range(0.65, 0.85)));
                break;
            default:
                mat.SetShaderParameter("kind", 0);
                // Close to the star worlds burn dry; far out they freeze; between, some hold water.
                var warmth = 1 - c.OrbitRadius / c.Parent!.Edge;
                var (sea, cloud) = warmth > 0.7 ? (0.2, 0.1) : warmth > 0.4 ? (r.Range(0.35, 0.6), r.Range(0.4, 0.8)) : (r.Range(0.5, 0.75), 0.3);
                var water = warmth > 0.7 ? Hue(0.03, 0.8, 0.4) : warmth > 0.4 ? Hue(r.Range(0.55, 0.62), 0.75, 0.55) : Hue(0.55, 0.25, 0.8);
                var land = warmth > 0.7 ? Hue(r.Range(0.02, 0.1), 0.6, 0.55) : warmth > 0.4 ? Hue(r.Range(0.2, 0.33), 0.5, 0.45) : Hue(0.58, 0.15, 0.7);
                Set("low", water);
                Set("mid", land);
                Set("high", warmth > 0.4 ? Hue(r.Range(0.06, 0.12), 0.3, 0.8) : Hue(0.6, 0.05, 0.95));
                Set("air", warmth > 0.7 ? Hue(0.05, 0.5, 0.8) : Hue(0.58, 0.45, 1));
                mat.SetShaderParameter("sea", (float)sea);
                mat.SetShaderParameter("clouds", (float)cloud);
                break;
        }
        return mat;
    }

    void Build(Galaxy g, int star)
    {
        foreach (var (_, mesh, _) in _shown) mesh.QueueFree();
        _shown.Clear();
        _system = star;
        if (star < 0) return;
        var shader = GD.Load<Shader>("res://game/planet.gdshader");
        foreach (var body in g.System(star).Tree)
        {
            var look = Look(g.Seed, body, g.Stars[star], shader);
            var mesh = new MeshInstance3D { Mesh = Ball, MaterialOverride = look, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(mesh);
            _shown.Add((body, mesh, look));
        }
    }

    /// <param name="star">The star whose system the ship is in, or -1 in open space.</param>
    /// <param name="ship">The ship relative to that star, in AU.</param>
    public void Show(Galaxy g, int star, Vec3d ship, double t)
    {
        if (star != _system) Build(g, star);
        foreach (var (body, mesh, look) in _shown)
        {
            var to = body.StarPosition(t) - ship;
            var dist = Math.Max(to.Length, body.BodyRadius * 1.02);
            var angle = Math.Max(Math.Atan(body.BodyRadius / dist) * 2 * (body.Parent == null ? 1 : Exaggeration), SmallestAngle);
            mesh.Position = Sky.ToV(to.Normalized()) * Shell;
            var size = (float)(2 * Shell * Math.Tan(Math.Min(angle, 2.8) / 2));
            mesh.Scale = new Vector3(size, size, size);
            look.SetShaderParameter("sun", Sky.ToV((-body.StarPosition(t)).Normalized()));
            look.SetShaderParameter("spin", (float)(t * (0.4 + body.Index * 0.07) % Math.Tau));
        }
    }
}

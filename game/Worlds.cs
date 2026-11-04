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
    readonly List<(Center Body, MeshInstance3D Mesh, ShaderMaterial Look, MeshInstance3D? Air, float AirScale)> _shown = new();
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
                mat.SetShaderParameter("relief", 1f);
                var tint = r.Range(0, 0.15);
                Set("low", Hue(tint, r.Range(0, 0.2), r.Range(0.18, 0.3)));
                Set("high", Hue(tint, r.Range(0, 0.15), r.Range(0.65, 0.85)));
                break;
            default:
                mat.SetShaderParameter("kind", 0);
                mat.SetShaderParameter("relief", 1f);
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

    /// <summary>
    /// How thick a world's air is (1 an Earth's) and how high it reaches over the body, as a share of
    /// its radius (drawn many times an Earth's ~1.5%, or no one would see it). Giants are all air; rocky
    /// worlds hold it by warmth: between, as Earth; far out, thin as Mars; close in, either none, burnt
    /// off as Mercury, or a Venus's. Moons have none. From a stream of its own, so the surfaces stay as they were.
    /// </summary>
    static (double Density, double Height) Air(ulong seed, Center c)
    {
        var r = new Rng(seed * 6700417 + (ulong)c.Key ^ 0xA1A1_0000UL);
        if (c.Kind == BodyKind.Giant) return (1.0, 0.05);
        if (c.Kind != BodyKind.Rocky) return (0, 0);
        var warmth = 1 - c.OrbitRadius / c.Parent!.Edge;
        if (warmth > 0.7) return r.Unit() < 0.5 ? (0, 0) : (r.Range(1.8, 2.6), 0.08);
        return warmth > 0.4 ? (r.Range(0.7, 1.1), 0.06) : (r.Range(0.15, 0.4), 0.04);
    }

    void Build(Galaxy g, int star)
    {
        foreach (var (_, mesh, _, air, _) in _shown) { mesh.QueueFree(); air?.QueueFree(); }
        _shown.Clear();
        _system = star;
        if (star < 0) return;
        var shader = GD.Load<Shader>("res://game/planet.gdshader");
        var airShader = GD.Load<Shader>("res://game/atmosphere.gdshader");
        foreach (var body in g.System(star).Tree)
        {
            var look = Look(g.Seed, body, g.Stars[star], shader);
            var mesh = new MeshInstance3D { Mesh = Ball, MaterialOverride = look, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(mesh);
            MeshInstance3D? air = null;
            var (density, height) = Air(g.Seed, body);
            if (density > 0)
            {
                var feel = new ShaderMaterial { Shader = airShader };
                feel.SetShaderParameter("air", look.GetShaderParameter("air"));
                feel.SetShaderParameter("density", (float)density);
                feel.SetShaderParameter("inner", (float)(1 / (1 + height)));
                air = new MeshInstance3D { Mesh = Ball, MaterialOverride = feel, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(air);
            }
            _shown.Add((body, mesh, look, air, (float)(1 + height)));
        }
    }

    /// <param name="star">The star whose system the ship is in, or -1 in open space.</param>
    /// <param name="ship">The ship relative to that star, in AU.</param>
    /// <param name="along">The way the ship goes in FTL, and the effective β the bubble shows the sky at (see Optics): bodies move and shrink with the stars.</param>
    public void Show(Galaxy g, int star, Vec3d ship, double t, Vec3d along = default, double beta = 0)
    {
        if (star != _system) Build(g, star);
        foreach (var (body, mesh, look, air, airScale) in _shown)
        {
            var to = body.StarPosition(t) - ship;
            var dist = Math.Max(to.Length, body.BodyRadius * 1.02);
            var (seen, doppler) = Optics.Aberrate(to.Normalized(), along, beta);
            var angle = Math.Max(Math.Atan(body.BodyRadius / dist) * 2 * (body.Parent == null ? 1 : Exaggeration) / doppler, SmallestAngle);
            mesh.Position = Sky.ToV(seen) * Shell;
            mesh.Visible = beta <= 0 || Optics.Horizon(seen, along, Optics.SpeedFor(beta)) > 0.5;   // in the dark behind, nothing is seen
            var size = (float)(2 * Shell * Math.Tan(Math.Min(angle, 2.8) / 2));
            mesh.Scale = new Vector3(size, size, size);
            look.SetShaderParameter("sun", Sky.ToV((-body.StarPosition(t)).Normalized()));
            look.SetShaderParameter("spin", (float)(t * (0.4 + body.Index * 0.07) % Math.Tau));
            if (air == null) continue;
            air.Position = mesh.Position;
            air.Scale = mesh.Scale * airScale;
            air.Visible = mesh.Visible;
            (air.MaterialOverride as ShaderMaterial)!.SetShaderParameter("sun", Sky.ToV((-body.StarPosition(t)).Normalized()));
        }
    }
}

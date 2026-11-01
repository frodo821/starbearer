using System;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>
/// The celestial sphere, recomputed from the ship's true position: what you see out of the window is
/// always the truth, while the planner works from the chart. Everything sits on a shell around the camera.
/// </summary>
public partial class Sky : MultiMeshInstance3D
{
    public const float Radius = 100f;
    const int Background = 3500;
    MultiMesh _mm = null!;
    Galaxy? _g;

    public static Color StarColor(double t)
    {
        var a = (float)Math.Clamp((t - 3000) / 7000, 0, 1);
        return new Color(1f, 0.55f, 0.3f).Lerp(new Color(1f, 0.93f, 0.85f), Math.Min(a * 2, 1)).Lerp(new Color(0.65f, 0.78f, 1f), Math.Max(a * 2 - 1, 0));
    }

    public void Setup(Galaxy g)
    {
        _g = g;
        var shader = GD.Load<Shader>("res://game/star.gdshader");
        var mat = new ShaderMaterial { Shader = shader };
        var quad = new QuadMesh { Size = Vector2.One, Material = mat };
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true, UseCustomData = true, Mesh = quad,
            InstanceCount = Background + g.Stars.Length + g.Nebulae.Length,
        };
        Multimesh = _mm;
        CustomAabb = new Aabb(new Vector3(-1000, -1000, -1000), new Vector3(2000, 2000, 2000));

        // Far field: a seeded band of unreachable stars, fixed in direction.
        var r = new Rng(g.Seed ^ 0xABCDEF);
        var tilt = new Vec3d(r.Normal(), r.Normal(), r.Normal()).Normalized();
        for (int i = 0; i < Background; i++)
        {
            var d = r.NormalVec().Normalized();
            if (i % 2 == 0) // half of them crowd into a band
                d = (d - tilt * (d.Dot(tilt) * 0.85)).Normalized();
            var pos = ToV(d) * Radius * 1.5f;
            var mag = (float)Math.Pow(r.Unit(), 5);
            var col = StarColor(r.Range(3000, 12000));
            col.A = 0.12f + 0.75f * mag;
            _mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, pos));
            _mm.SetInstanceColor(i, col);
            _mm.SetInstanceCustomData(i, new Color(0.22f + 0.38f * mag, 7f, 0, 0));
        }
    }

    public static Vector3 ToV(Vec3d v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public void UpdateSky(Vec3d[] truth, Vec3d shipPos)
    {
        var g = _g!;
        for (int i = 0; i < g.Stars.Length; i++)
        {
            var s = g.Stars[i];
            var to = truth[i] - shipPos;
            var dist = Math.Max(to.Length, 1e-4);
            var pos = ToV(to / dist) * Radius;
            var angular = 2 * s.Radius * 7 / dist;                 // exaggerated disc so wells read at a glance
            var size = (float)Math.Clamp(Radius * angular * 2.5 + 1.6, 0.5, Radius * 3);
            var bright = (float)Math.Clamp(0.5 + 30 * s.Mass / (dist * dist), 0.5, 3);
            var c = StarColor(s.Temperature); c.A = bright;
            int idx = Background + i;
            _mm.SetInstanceTransform(idx, new Transform3D(Basis.Identity, pos));
            _mm.SetInstanceColor(idx, c);
            _mm.SetInstanceCustomData(idx, new Color(size, 9f, 0, 0));
        }
        for (int i = 0; i < g.Nebulae.Length; i++)
        {
            var n = g.Nebulae[i];
            var to = n.Center - shipPos;
            var dist = Math.Max(to.Length, n.Radius * 0.3);
            var pos = ToV(to.Normalized()) * Radius * 1.2f;
            var size = (float)Math.Clamp(Radius * 2.4 * 2.2 * n.Radius / dist, 10, Radius * 6);
            var c = new Color(0.35f, 0.5f, 0.75f, 0.11f * (float)n.Density);
            int idx = Background + g.Stars.Length + i;
            _mm.SetInstanceTransform(idx, new Transform3D(Basis.Identity, pos));
            _mm.SetInstanceColor(idx, c);
            _mm.SetInstanceCustomData(idx, new Color(size, 3f, 0, 0));
        }
    }
}

using System;
using System.Linq;
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

    /// <summary>Each nebula drawn as a few overlapping puffs, so it reads as a cloud, not a disc.</summary>
    const int PuffsPerNebula = 6;
    const float NebulaShell = 140f;
    /// <summary>Faint: a nebula is gas and dust, mostly seen as a haze against the stars.</summary>
    const float NebulaBrightness = 0.006f;   // linear: the screen's sRGB lifts dark values a lot
    (Vec3d Center, double Radius, Vector3 Tint)[] _puffs = Array.Empty<(Vec3d, double, Vector3)>();
    ShaderMaterial _nebulae = null!;

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
            InstanceCount = Background + g.Stars.Length,
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

        // Puffs scattered about each nebula's centre, by the seed, so a nebula keeps its shape.
        var pr = new Rng(g.Seed ^ 0x5EB0FA);
        _puffs = g.Nebulae.SelectMany(n => Enumerable.Range(0, PuffsPerNebula).Select(_ =>
            (n.Center + pr.NormalVec() * (n.Radius * 0.4), n.Radius * pr.Range(0.3, 0.55),
             new Vector3(0.35f, 0.5f, 0.75f) * NebulaBrightness * (float)n.Density))).ToArray();
        _nebulae = new ShaderMaterial { Shader = GD.Load<Shader>("res://game/nebula.gdshader") };
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = NebulaShell, Height = NebulaShell * 2, RadialSegments = 48, Rings = 24 },
            MaterialOverride = _nebulae,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-1000, -1000, -1000), new Vector3(2000, 2000, 2000)),
        });
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
            // True size: the glow's bright core (a third of the quad) covers the star's real disc, which
            // Worlds draws as a sphere when the star is close. Far off, the floor keeps it a point of light.
            var disc = 2 * Radius * Math.Tan(Math.Min(Math.Atan(s.Radius / Math.Max(dist, s.Radius * 1.02)), 1.4));
            var size = (float)Math.Clamp(disc * 3 + 1.6, 0.5, Radius * 3);
            var bright = (float)Math.Clamp(0.5 + 30 * s.Mass / (dist * dist), 0.5, 3);
            var c = StarColor(s.Temperature); c.A = bright;
            int idx = Background + i;
            _mm.SetInstanceTransform(idx, new Transform3D(Basis.Identity, pos));
            _mm.SetInstanceColor(idx, c);
            _mm.SetInstanceCustomData(idx, new Color(size, 9f, 0, 0));
        }
        // Each puff's reach on the sky, as a chord of the unit sphere; inside one, it is everywhere.
        var dirs = new Vector4[_puffs.Length];
        var tints = new Vector4[_puffs.Length];
        for (int i = 0; i < _puffs.Length; i++)
        {
            var (centre, radius, tint) = _puffs[i];
            var to = centre - shipPos;
            var dist = to.Length;
            var reach = 2.2 * Math.Atan(radius / Math.Max(dist, 1e-9));
            var chord = dist <= radius || reach >= Math.PI ? 3.0 : 2 * Math.Sin(reach / 2);
            var d = dist > 1e-9 ? to / dist : new Vec3d(0, 0, 1);
            dirs[i] = new Vector4((float)d.X, (float)d.Y, (float)d.Z, (float)chord);
            tints[i] = new Vector4(tint.X, tint.Y, tint.Z, 0);
        }
        _nebulae.SetShaderParameter("count", _puffs.Length);
        _nebulae.SetShaderParameter("puffs", dirs);
        _nebulae.SetShaderParameter("tints", tints);
    }
}

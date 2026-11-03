using System;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>
/// The celestial sphere, recomputed from the ship's true position: what you see out of the window is
/// always the truth, while the planner works from the chart. Everything sits on a shell around the camera.
///
/// The truth includes the light's way to the eye (see Optics): the nearest star bends the light of the
/// stars behind it into two images, and in FTL the bubble crowds the sky ahead, blue and bright, dims and
/// reddens it behind, and leaves a dark cone at the back. The bubble collapsing flashes ahead.
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

    /// <summary>Every point of light as it is at rest, before the lens and the bubble: where, how hot, how bright, how big and how sharp.</summary>
    Vec3d[] _dir = Array.Empty<Vec3d>();
    double[] _kelvin = Array.Empty<double>(), _bright = Array.Empty<double>(), _size = Array.Empty<double>(), _sharp = Array.Empty<double>();
    int Sources => Background + _g!.Stars.Length;
    /// <summary>Per source its main image, then its second image through the lens, then the flash.</summary>
    int Flash => 2 * Sources;
    float[] _buffer = Array.Empty<float>();
    const int Stride = 20;   // a transform's 12 floats, a colour's 4, the custom data's 4

    /// <summary>The FTL speed the window shows, eased so the view doesn't jump; the way the ship goes; the flash's age and strength.</summary>
    double _speed;
    Vec3d _along = new(0, 0, 1);
    bool _wasFtl;
    double _flashAge = double.MaxValue, _flashStrength;
    /// <summary>Real seconds the view takes to settle to a new speed, and the flash to fade by 1/e.</summary>
    const double Settle = 0.35, FlashFade = 0.5;

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
            InstanceCount = 2 * (Background + g.Stars.Length) + 1,
        };
        Multimesh = _mm;
        CustomAabb = new Aabb(new Vector3(-1000, -1000, -1000), new Vector3(2000, 2000, 2000));
        _buffer = new float[_mm.InstanceCount * Stride];

        var n = Sources;
        (_dir, _kelvin, _bright, _size, _sharp) = (new Vec3d[n], new double[n], new double[n], new double[n], new double[n]);
        // Far field: a seeded band of unreachable stars, fixed in direction.
        var r = new Rng(g.Seed ^ 0xABCDEF);
        var tilt = new Vec3d(r.Normal(), r.Normal(), r.Normal()).Normalized();
        for (int i = 0; i < Background; i++)
        {
            var d = r.NormalVec().Normalized();
            if (i % 2 == 0) // half of them crowd into a band
                d = (d - tilt * (d.Dot(tilt) * 0.85)).Normalized();
            var mag = Math.Pow(r.Unit(), 5);
            (_dir[i], _kelvin[i], _bright[i], _size[i], _sharp[i]) = (d, r.Range(3000, 12000), 0.12 + 0.75 * mag, 0.22 + 0.38 * mag, 7);
        }
        for (int i = 0; i < g.Stars.Length; i++) (_kelvin[Background + i], _sharp[Background + i]) = (g.Stars[i].Temperature, 9);

        // Puffs scattered about each nebula's centre, by the seed, so a nebula keeps its shape.
        var pr = new Rng(g.Seed ^ 0x5EB0FA);
        _puffs = g.Nebulae.SelectMany(nb => Enumerable.Range(0, PuffsPerNebula).Select(_ =>
            (nb.Center + pr.NormalVec() * (nb.Radius * 0.4), nb.Radius * pr.Range(0.3, 0.55),
             new Vector3(0.35f, 0.5f, 0.75f) * NebulaBrightness * (float)nb.Density))).ToArray();
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

    /// <summary>The way the ship goes and the effective β the window shows it at, for the worlds drawn near.</summary>
    public (Vec3d Along, double Beta) Bubble => (_along, Optics.Beta(_speed));
    public bool FlashedForTest { get; private set; }
    /// <summary>A direction in the band of far stars, where they crowd.</summary>
    public Vec3d BandForTest => _dir[0];

    /// <param name="ftlVelocity">The ship's velocity in FTL, in ly/day; null out of it.</param>
    /// <param name="delta">Real seconds since the last frame.</param>
    public void UpdateSky(Vec3d[] truth, Vec3d shipPos, Vec3d? ftlVelocity, double delta)
    {
        var g = _g!;
        // The bubble: the speed eases toward the ship's, or to nothing out of FTL; collapsing, it flashes ahead.
        if (ftlVelocity is { } fv && fv.Length > 1e-9) _along = fv.Normalized();
        var target = ftlVelocity?.Length ?? 0;
        if (_wasFtl && ftlVelocity == null) { (_flashAge, _flashStrength) = (0, Optics.Beta(_speed)); FlashedForTest = true; }
        _wasFtl = ftlVelocity != null;
        _speed += (target - _speed) * (1 - Math.Exp(-delta / Settle));
        if (_speed < 1e-4) _speed = 0;
        _flashAge += delta;
        var beta = Optics.Beta(_speed);

        // The stars of the galaxy, where they truly are; the nearest is the lens.
        int lens = -1; double lensDist = double.MaxValue;
        for (int i = 0; i < g.Stars.Length; i++)
        {
            var s = g.Stars[i];
            var to = truth[i] - shipPos;
            var dist = Math.Max(to.Length, 1e-4);
            if (dist < lensDist) (lens, lensDist) = (i, dist);
            var k = Background + i;
            _dir[k] = to / dist;
            // True size: the glow's bright core (a third of the quad) covers the star's real disc, which
            // Worlds draws as a sphere when the star is close. Far off, the floor keeps it a point of light.
            var disc = 2 * Radius * Math.Tan(Math.Min(Math.Atan(s.Radius / Math.Max(dist, s.Radius * 1.02)), 1.4));
            _size[k] = Math.Clamp(disc * 3 + 1.6, 0.5, Radius * 3);
            _bright[k] = Math.Clamp(0.5 + 30 * s.Mass / (dist * dist), 0.5, 3);
        }
        var lensStar = g.Stars[lens];
        var lensAu = lensDist / Sublight.Scale;
        var einstein = Optics.EinsteinAngle(lensStar.Mu, lensAu);
        var lensDir = _dir[Background + lens];
        var lensDisc = Math.Atan(lensStar.Radius / Math.Max(lensDist, lensStar.Radius * 1.02));
        var lensing = einstein > 2e-4;   // under about a hundredth of a degree it can't be seen

        void Write(int slot, Vec3d dir, double kelvin, double bright, double size, double sharp)
        {
            var (seen, doppler) = Optics.Aberrate(dir, _along, beta);
            var a = bright * Optics.DopplerGain(doppler) * Optics.Visible(kelvin * doppler) * Optics.Horizon(seen, _along, _speed);
            var c = StarColor(kelvin * doppler);
            var p = ToV(seen) * Radius * (slot % Sources < Background ? 1.5f : 1f);
            var o = slot * Stride;
            // Identity basis, the position as the origin.
            _buffer[o] = 1; _buffer[o + 1] = 0; _buffer[o + 2] = 0; _buffer[o + 3] = p.X;
            _buffer[o + 4] = 0; _buffer[o + 5] = 1; _buffer[o + 6] = 0; _buffer[o + 7] = p.Y;
            _buffer[o + 8] = 0; _buffer[o + 9] = 0; _buffer[o + 10] = 1; _buffer[o + 11] = p.Z;
            _buffer[o + 12] = c.R; _buffer[o + 13] = c.G; _buffer[o + 14] = c.B; _buffer[o + 15] = (float)a;
            // A disc shrinks as the bubble crowds it forward; a point of light stays one.
            _buffer[o + 16] = (float)Math.Max(Math.Min(size, 1.6), size / doppler); _buffer[o + 17] = (float)sharp;
            _buffer[o + 18] = 0; _buffer[o + 19] = 0;
        }
        void Hide(int slot) => _buffer[slot * Stride + 15] = 0;

        for (int i = 0; i < Sources; i++)
        {
            var d = _dir[i];
            if (!lensing || i == Background + lens) { Write(i, d, _kelvin[i], _bright[i], _size[i], _sharp[i]); Hide(Sources + i); continue; }
            // Light from behind the lens comes round it on both sides; an image on the star's disc is hidden by it.
            var off = Math.Acos(Math.Clamp(d.Dot(lensDir), -1, 1));
            var (outer, inner) = Optics.Lens(off, einstein);
            Write(i, Optics.AtAngle(lensDir, d, outer.Angle), _kelvin[i], _bright[i] * outer.Gain, _size[i], _sharp[i]);
            if (Math.Abs(inner.Angle) > lensDisc && inner.Gain > 0.01)
                Write(Sources + i, Optics.AtAngle(lensDir, d, inner.Angle), _kelvin[i], _bright[i] * inner.Gain, _size[i], _sharp[i]);
            else Hide(Sources + i);
        }

        // The flash: what the bubble swept up, let go ahead as it collapses.
        var flash = _flashStrength * Math.Exp(-_flashAge / FlashFade);
        if (flash > 0.01) Write(Flash, _along, 20000, 3 * flash, Radius * 1.2, 2.5);
        else Hide(Flash);
        _mm.Buffer = _buffer;

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
            var (d, doppler) = Optics.Aberrate(dist > 1e-9 ? to / dist : new Vec3d(0, 0, 1), _along, beta);
            var shade = (float)(Math.Clamp(doppler, 0.3, 2) * Optics.Horizon(d, _along, _speed));
            dirs[i] = new Vector4((float)d.X, (float)d.Y, (float)d.Z, (float)(chord / Math.Max(doppler, 0.3)));
            tints[i] = new Vector4(tint.X * shade, tint.Y * shade, tint.Z * shade, 0);
        }
        _nebulae.SetShaderParameter("count", _puffs.Length);
        _nebulae.SetShaderParameter("puffs", dirs);
        _nebulae.SetShaderParameter("tints", tints);
    }
}

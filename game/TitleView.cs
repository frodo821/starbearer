using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>
/// What runs behind the title: the view from a cockpit, in a galaxy made for it alone. Scenes follow one
/// another, faded through black: hanging off a world in some system, its moons going round and the star
/// in the corner of the window, or riding a bubble between stars, the sky crowded ahead until it drops
/// out with a flash. The same sky and worlds the game draws, with nobody at the helm.
/// </summary>
public partial class TitleView : Node3D
{
    const double SceneSeconds = 24, FadeSeconds = 1.8;
    /// <summary>Days of the system's time a real second shows: slow enough to read as drifting, fast enough to see the moons go.</summary>
    const double OrbitDaysPerSecond = 0.05;
    /// <summary>The bubble's speed (ly/day), the days a second shows in it, and the seconds before a scene ends that it drops out.</summary>
    const double CruiseSpeed = 1.2, CruiseDaysPerSecond = 0.1, DropBefore = 5;

    Task<Galaxy>? _making;
    Galaxy? _g;
    Sky? _sky;
    readonly Worlds _worlds = new();
    Vec3d[] _truth = Array.Empty<Vec3d>();
    Rng _r = new(1);
    int _count;
    double _age;

    abstract record Scene;
    /// <summary>Off a world: the ship holds a fixed offset from it (AU), and the view leans from it toward the star.</summary>
    sealed record Hanging(int Star, Center Body, Vec3d Offset, double T0, double Lean) : Scene;
    /// <summary>In FTL from a point (ly) along a way, the view a little off the bow.</summary>
    sealed record Cruise(Vec3d From, Vec3d Along, Vec3d Turn, double Off) : Scene;
    Scene? _scene;

    public override void _Ready() => AddChild(_worlds);

    public void Begin(ulong seed)
    {
        _r = new Rng(seed ^ 0x717E);
        _making = Task.Run(() => Galaxy.Generate(seed));
    }

    /// <summary>Black over the view: all of it until the galaxy is made, then fading in and out at each scene's ends.</summary>
    public float Shade => _g == null ? 1 : (float)Math.Clamp(Math.Max(1 - _age / FadeSeconds, 1 - (SceneSeconds - _age) / FadeSeconds), 0, 1);

    /// <summary>Advances the view; returns where the camera looks, or null while the galaxy is still being made.</summary>
    public Vec3d? Step(double delta)
    {
        if (_g == null)
        {
            if (_making is not { IsCompleted: true }) return null;
            if (!_making.IsCompletedSuccessfully) { GD.PushError(_making.Exception?.ToString() ?? "title galaxy failed"); _making = null; return null; }
            _g = _making.Result;
            _truth = _g.Stars.Select(s => s.Position).ToArray();
            _sky = new Sky();
            AddChild(_sky);
            _sky.Setup(_g);
            Next();
        }
        _age += delta;
        if (_age > SceneSeconds) Next();
        var g = _g;
        var u = _age / SceneSeconds;
        switch (_scene)
        {
            case Hanging h:
                var t = h.T0 + _age * OrbitDaysPerSecond;
                var ship = h.Body.StarPosition(t) + h.Offset;
                _sky!.UpdateSky(_truth, _truth[h.Star] + ship / Sublight.AuPerLy, null, delta);
                _worlds.Show(g, h.Star, ship, t);
                // From the world toward the star, leaning further as the scene goes.
                var toBody = -h.Offset.Normalized();
                var toStar = (-ship).Normalized();
                var apart = Math.Acos(Math.Clamp(toBody.Dot(toStar), -1, 1));
                var axis = toBody.Cross(toStar) is { Length: > 1e-6 } cross ? cross.Normalized() : toBody.AnyPerpendicular();
                return Turn(toBody, axis, apart * (h.Lean + 0.12 * (u - 0.5)));
            case Cruise c:
                var flying = _age < SceneSeconds - DropBefore;
                var travelled = Math.Min(_age, SceneSeconds - DropBefore) * CruiseDaysPerSecond * CruiseSpeed;
                _sky!.UpdateSky(_truth, c.From + c.Along * travelled, flying ? c.Along * CruiseSpeed : null, delta);
                return Turn(c.Along, c.Turn, c.Off + 0.08 * (u - 0.5));
        }
        return null;
    }

    /// <summary>A vector turned about a unit axis.</summary>
    static Vec3d Turn(Vec3d v, Vec3d axis, double angle) =>
        v * Math.Cos(angle) + axis.Cross(v) * Math.Sin(angle) + axis * (axis.Dot(v) * (1 - Math.Cos(angle)));

    /// <summary>Two scenes off a world, then one in the bubble, over and over.</summary>
    void Next()
    {
        _age = 0;
        _scene = _count++ % 3 == 2 ? MakeCruise() : MakeHanging();
        if (_scene is Cruise) _worlds.Show(_g!, -1, Vec3d.Zero, 0);
    }

    Scene MakeHanging()
    {
        var g = _g!;
        // A system with worlds, and among them one to look at: giants most, moons least.
        int star;
        do star = _r.Index(g.Stars.Length); while (g.System(star).Children.Count == 0);
        var bodies = g.System(star).Tree.Where(b => b.Parent != null).ToList();
        double Weight(Center b) => b.Kind switch { BodyKind.Giant => 4, BodyKind.Rocky => 2, _ => 1 };
        var pick = _r.Unit() * bodies.Sum(Weight);
        var body = bodies.First(b => (pick -= Weight(b)) <= 0 || b == bodies[^1]);
        var t0 = _r.Range(0, 2000);
        // Off to the side of the world from its star: half lit to a thick crescent, near enough that it fills a good part of the window.
        var sun = (-body.StarPosition(t0)).Normalized();
        var side = Turn(sun.AnyPerpendicular(), sun, _r.Range(0, Math.Tau));
        var offset = Turn(sun, side, _r.Range(65, 115) * Math.PI / 180) * (body.BodyRadius * _r.Range(5.5, 10));
        return new Hanging(star, body, offset, t0, _r.Range(0.18, 0.35));
    }

    Scene MakeCruise()
    {
        var g = _g!;
        // From a star toward one a few light years off, aimed a little past it.
        var a = g.Stars[_r.Index(g.Stars.Length)];
        var b = g.Stars.Where(s => s != a && (s.Position - a.Position).Length is > 3 and < 12).OrderBy(_ => _r.Unit()).FirstOrDefault()
                ?? g.Stars.Where(s => s != a).OrderBy(s => (s.Position - a.Position).Length).First();
        var way = (b.Position - a.Position).Normalized();
        var along = Turn(way, way.AnyPerpendicular(), _r.Range(0.03, 0.08));
        var turn = Turn(along.AnyPerpendicular(), along, _r.Range(0, Math.Tau));
        return new Cruise(a.Position + along * 0.05, along, turn, _r.Range(0.15, 0.45));
    }
}

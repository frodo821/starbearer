using System;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Ui;

namespace Starbearer;

/// <summary>
/// The ship's manifest, open in flight: the bays and how full they are, each job aboard with where it
/// goes and how it is holding up, and where the company's other ships wait. Built from the live voyage.
/// </summary>
public partial class ManifestWindow : PanelContainer
{
    readonly Main _game;
    readonly VBoxContainer _root = new();
    static readonly Color Good = Palette.Good, Warn = Palette.Warn;
    static readonly Color Dim = Ui.PanelDim;
    double _sinceBuilt;

    /// <summary>Set at each Rebuild: the buttons that steer for each job's destination, in hold order (goods of one's own have none).</summary>
    public Button[] CourseButtons { get; private set; } = Array.Empty<Button>();

    public ManifestWindow(Main game)
    {
        _game = game;
        Visible = false;
        CustomMinimumSize = new Vector2(470, 0);
        // Under the clock, growing down; a long hold scrolls rather than reach the attitude dial.
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetRight = -16; OffsetTop = 262;
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical = GrowDirection.End;
        AddThemeStyleboxOverride("panel", Ui.Frame(0.93f, 16, 12));
        _root.AddThemeConstantOverride("separation", 6);
        AddChild(_root);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        // Integrity and distances change as the ship flies.
        _sinceBuilt += delta;
        if (_sinceBuilt > 1) Rebuild();
    }

    public void Rebuild()
    {
        _sinceBuilt = 0;
        Ui.Clear(_root);
        var g = _game.G!; var v = _game.V!; var s = _game.Save!;
        var ship = s.Ship;
        var bays = new CargoBays(v.Class, ship.Pressurized);
        _root.AddChild(Row(Text("積荷と仕事", 18, Good), Press("閉じる  I", _game.ToggleManifest, size: 13)));
        _root.AddChild(Text($"{v.Class.Name}   容積 {Missions.Cubic(Missions.VolumeUsed(v.Hold))} / {Missions.Cubic(v.Class.Capacity)}   積荷質量 {v.CargoMass:F1}   資金 {s.Credits:F0}", 14));
        // Room by kind of bay, as the board's refusals count it.
        var kinds = new[] { CargoKind.Container, CargoKind.Bulk, CargoKind.Fluid, CargoKind.PressurizedFluid }
            .Where(k => bays.RoomFor(k) > 0)
            .Select(k => $"{Cargos.Name(k)} {Missions.Cubic(v.Hold.Where(c => c.Mission.Cargo.Kind == k).Sum(c => c.Mission.Cargo.Volume))}/{Missions.Cubic(bays.RoomFor(k))}");
        _root.AddChild(Text(string.Join("   ", kinds), 13, Dim));
        _root.AddChild(new HSeparator());

        if (v.Hold.Count == 0) _root.AddChild(Text("空荷", 14, Dim));
        var jobs = new VBoxContainer();
        jobs.AddThemeConstantOverride("separation", 6);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.CustomMinimumSize = new Vector2(0, Math.Min(v.Hold.Count * 64, 200));
        jobs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(jobs);
        _root.AddChild(scroll);
        var here = v.EstimatedPosition;
        foreach (var lot in v.Hold.Where(c => !c.IsJob))
        {
            var cargo = lot.Mission.Cargo;
            var hot = v.Heat > cargo.HeatTolerance;
            jobs.AddChild(Text($"{cargo.Name} {cargo.Volume:F0} 単位  自分の荷   仕入 {lot.Cost:F0}   健全度 {lot.Integrity:P0}" + (hot ? "  熱で傷んでいる" : ""), 14, hot || lot.Integrity < 0.999 ? Warn : null));
        }
        CourseButtons = v.Hold.Where(c => c.IsJob).Select(c =>
        {
            var m = c.Mission;
            var target = g.StarOf(m.Target);
            var star = g.Stars[target];
            var far = (v.ChartPositions[target] - here).Length;
            var info = new VBoxContainer();
            info.AddThemeConstantOverride("separation", 0);
            info.AddChild(Text($"{m.Cargo.Name}  →  {Missions.Label(g, m.Target)}（{g.Regions[star.Region].Name} 宙域）", 15, target == v.TargetStar ? Good : null));
            info.AddChild(Text($"{Cargos.Name(m.Cargo.Kind)}  {Missions.Cubic(m.Cargo.Volume)}  質量 {m.Cargo.Mass:F1}   あと {far:F1} ly", 13, Dim));
            var hot = v.Heat > m.Cargo.HeatTolerance;
            info.AddChild(Text($"報酬 {c.Payout:F0} / {m.Pay:F0}   健全度 {c.Integrity:P0}   耐熱 {m.Cargo.HeatTolerance:F2}" + (hot ? "  熱で傷んでいる" : ""), 13, hot || c.Integrity < 0.999 ? Warn : Dim));
            var course = Press(target == v.TargetStar ? "針路" : "針路に", () => _game.SetCourse(target), target != v.TargetStar, size: 13);
            jobs.AddChild(Row(info, course));
            return course;
        }).ToArray();

        var away = s.Fleet.Where(x => x.Id != s.Flown).ToList();
        if (away.Count > 0)
        {
            _root.AddChild(new HSeparator());
            _root.AddChild(Text("ほかの船", 15, Good));
            foreach (var x in away)
                _root.AddChild(Text($"{x.Type.Name}  {Missions.Label(g, x.Station)}   積荷 {x.Hold.Count} 件   燃料 {x.Fuel:F2}/{x.Type.Tank:F2}", 13, Dim));
        }
    }
}

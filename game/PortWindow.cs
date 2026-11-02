using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>The station's window: its mission board, the hold, and what it sells. Built from the checkpoint.</summary>
public partial class PortWindow : PanelContainer
{
    readonly Main _game;
    readonly VBoxContainer _root = new();
    static readonly Color Good = Palette.Good, Warn = Palette.Warn;
    /// <summary>Lighter than the HUD's, for small text on the opaque panel.</summary>
    static readonly Color Dim = new(0.6f, 0.68f, 0.8f);

    /// <summary>Set at each Rebuild: the buttons taking the offers, in board order.</summary>
    public Button[] TakeButtons { get; private set; } = Array.Empty<Button>();

    public PortWindow(Main game)
    {
        _game = game;
        Visible = false;
        CustomMinimumSize = new Vector2(1240, 780);
        SetAnchorsAndOffsetsPreset(LayoutPreset.Center, LayoutPresetMode.Minsize);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.035f, 0.07f, 0.97f), BorderColor = new Color(Good, 0.6f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        _root.AddThemeConstantOverride("separation", 10);
        AddChild(_root);
    }

    static Label Text(string s, int size = 15, Color? c = null)
    {
        var l = new Label { Text = s };
        l.AddThemeFontSizeOverride("font_size", size);
        if (c is { } col) l.AddThemeColorOverride("font_color", col);
        return l;
    }

    /// <summary>The keyboard stays with the game, so Enter and the number keys work with the window open.</summary>
    static Button Press(string s, Action act, bool enabled = true, string key = "")
    {
        var b = new Button { Text = key == "" ? s : $"{s}  [{key}]", FocusMode = FocusModeEnum.None, Disabled = !enabled };
        b.Pressed += act;
        return b;
    }

    static VBoxContainer Column(float width)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 7);
        return box;
    }

    /// <summary>A column that scrolls when it outgrows the window, as a long shipyard list does.</summary>
    static ScrollContainer Scroll(Control column)
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(column.CustomMinimumSize.X + 14, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(column);
        return scroll;
    }

    static HBoxContainer Row(Control text, params Control[] tail)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(text);
        foreach (var c in tail) { c.SizeFlagsVertical = SizeFlags.ShrinkCenter; row.AddChild(c); }
        return row;
    }

    public void Rebuild()
    {
        foreach (var child in _root.GetChildren()) { _root.RemoveChild(child); child.QueueFree(); }
        var s = _game.Save!;
        var g = _game.G!;
        var star = _game.V!.SystemStar;
        _root.AddChild(Text($"{g.Stars[star].Name}   {Missions.Name(Missions.KindOf(g, star))}   {g.Regions[g.Stars[star].Region].Name} 宙域", 28, Good));
        _root.AddChild(Text($"資金 {s.Credits:F0}   配達 {s.Deliveries} 件   ここがチェックポイント", 15, Dim));
        foreach (var line in _game.PortReport) _root.AddChild(Text(line, 16));
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 28);
        _root.AddChild(body);
        body.AddChild(Scroll(BoardColumn(g, s)));
        body.AddChild(Scroll(ShipColumn(g, s)));
        if (_game.Message != "") _root.AddChild(Text(_game.Message, 15, Warn));
        _root.AddChild(Row(new Control(), Press("船へ戻る", _game.LeavePort, key: "Enter")));
    }

    Control BoardColumn(Galaxy g, SaveData s)
    {
        var box = Column(680);
        box.AddChild(Text("ミッションボード", 20, Good));
        if (_game.GateExit is var exit and >= 0)
        {
            box.AddChild(Text($"ゲートの行き先: {g.Stars[exit].Name}（{g.Regions[g.Stars[exit].Region].Name} 宙域）", 17));
            box.AddChild(Text($"出港してから「ゲート通過」で予約し、ゲートまで寄ると向こうへ出る。速さはそのまま\n通行税は船・モジュール・積荷の価値の {Tuning.GateTollRate:P0}（今なら {_game.GateToll:F0}）、次の入港で精算", 14, Dim));
            box.AddChild(Text("ゲートは仕事を出さない", 15, Dim));
            TakeButtons = Array.Empty<Button>();
            box.AddChild(new HSeparator());
            HangarSection(box, g, s);
            return box;
        }
        var offers = _game.Offers;
        var bays = CargoBays.Of(s);
        if (offers.Count == 0) box.AddChild(Text("請けられる仕事は残っていない", 15, Dim));
        TakeButtons = offers.Select(m =>
        {
            var c = m.Cargo;
            var far = (g.Stars[m.Target].Position - g.Stars[m.Origin].Position).Length;
            var refusal = bays.Refusal(s.Hold, c);
            var info = new VBoxContainer();
            info.AddChild(Text($"{c.Name}  →  {g.Stars[m.Target].Name}（{Missions.Name(Missions.KindOf(g, m.Target))}）", 17));
            info.AddChild(Text($"{Cargos.Name(c.Kind)}   直線 {far:F1} ly   容積 {Missions.Cubic(c.Volume)}   質量 {c.Mass:F1}   耐熱 {c.HeatTolerance:F2}", 14, Dim));
            if (refusal != null) info.AddChild(Text($"積めない: {refusal}", 13, Warn));
            var take = Press("請ける", () => _game.TakeMission(m), refusal == null);
            box.AddChild(Row(info, Text($"報酬 {m.Pay:F0}", 17, Good), take));
            return take;
        }).ToArray();
        box.AddChild(new HSeparator());
        HangarSection(box, g, s);
        return box;
    }

    static string BayLabel(Bay b, bool fitted) => b.Pressurized || fitted ? "加圧液体ベイ" : Cargos.Name(b.Kind);

    /// <summary>A hull at a glance: its bays by kind, its empty delta-v, and how it flies.</summary>
    static string Spec(ShipClass c, IReadOnlyCollection<int>? fitted = null) =>
        string.Join(" ", c.Bays.Select((b, i) => (Label: BayLabel(b, fitted?.Contains(i) == true), b.Volume)).GroupBy(x => x.Label)
            .Select(k => $"{k.Key}{Missions.Cubic(k.Sum(x => x.Volume))}"))
        + $"   空荷Δv {Tuning.ExhaustVelocity * Math.Log((c.DryMass + c.Tank) / c.DryMass):F2}   抵抗×{c.Drag:0.0#} 推力×{c.Thrust:0.0#} {(c.Gates ? "ゲート可" : "ゲート不可")}";

    /// <summary>The company's ships docked here, and at a shipyard what it builds.</summary>
    void HangarSection(VBoxContainer box, Galaxy g, SaveData s)
    {
        var star = s.PortStar;
        box.AddChild(Text("格納庫", 20, Good));
        foreach (var ship in s.ShipsAt(star))
        {
            var info = new VBoxContainer();
            info.AddChild(Text(ship.Type.Name + (ship.Id == s.Flown ? "（乗船中）" : ""), 16, ship.Id == s.Flown ? Good : null));
            info.AddChild(Text($"燃料 {ship.Fuel:F2}/{ship.Type.Tank:F2}   船体 {ship.Hull:P0}   積荷 {ship.Hold.Count} 件   {Spec(ship.Fitted, ship.Pressurized)}", 13, Dim));
            if (ship.Id == s.Flown) { box.AddChild(Row(info)); continue; }
            var sale = Hangar.SaleRefusal(s, g, ship);
            box.AddChild(Row(info, Press("乗り換え", () => _game.BoardShip(ship.Id)),
                Press($"売却 {Ships.SaleValue(ship):F0}", () => _game.SellShip(ship.Id), sale == null)));
        }
        var away = s.Fleet.Where(x => x.Star != star).ToList();
        if (away.Count > 0)
            box.AddChild(Text("ほかの港: " + string.Join("、", away.Select(x => $"{x.Type.Name}（{g.Stars[x.Star].Name}）")), 13, Dim));
        var sold = Ships.SoldAt(g, star);
        if (sold.Length == 0) return;
        box.AddChild(Text("造船", 18, Good));
        foreach (var c in sold)
        {
            var info = new VBoxContainer();
            info.AddChild(Text(c.Name, 15));
            info.AddChild(Text(Spec(c), 13, Dim));
            box.AddChild(Row(info, Press($"購入 {c.Price:F0}", () => _game.BuyShip(c), s.Credits >= c.Price)));
        }
    }

    Control ShipColumn(Galaxy g, SaveData s)
    {
        var box = Column(490);
        var bays = CargoBays.Of(s);
        box.AddChild(Text($"カーゴ  容積 {Missions.Cubic(Missions.VolumeUsed(s.Hold))} / {Missions.Cubic(bays.Capacity)}", 20, Good));
        box.AddChild(Text($"{bays.Ship.Name}: " + string.Join("、", bays.Ship.Bays.Select((b, i) =>
            $"{BayLabel(b, s.Pressurized.Contains(i))} {Missions.Cubic(b.Volume)}")), 14, Dim));
        if (s.Hold.Count == 0) box.AddChild(Text("空荷", 15, Dim));
        foreach (var c in s.Hold)
        {
            var target = c.Mission.Target;
            var info = new VBoxContainer();
            info.AddChild(Text($"{c.Mission.Cargo.Name}  →  {g.Stars[target].Name}", 16));
            info.AddChild(Text($"{Cargos.Name(c.Mission.Cargo.Kind)}   健全度 {c.Integrity:P0}   報酬 {c.Payout:F0}   容積 {Missions.Cubic(c.Mission.Cargo.Volume)}", 13, Dim));
            var course = _game.Course == target ? Text("針路", 14, Good) as Control : Press("針路に", () => _game.SetCourse(target));
            box.AddChild(Row(info, course, Press($"破棄 -{Missions.DropFee(c):F0}", () => _game.DropCargo(c))));
        }
        box.AddChild(new HSeparator());
        box.AddChild(Text("補給", 20, Good));
        void Sale(string name, double cost, bool need, Action<SaveData> buy, string key) =>
            box.AddChild(Row(Text(name, 15), Press($"{cost:F0}", () => _game.Purchase(buy), need, key)));
        Sale($"燃料 {s.HeldFuel:F2} / {s.Ship.Type.Tank:F1}", (s.Ship.Type.Tank - s.HeldFuel) * Tuning.FuelPrice, s.HeldFuel < s.Ship.Type.Tank - 0.001, Supply.BuyFuel, "3");
        Sale($"生命維持 残 {s.HeldLife:F0} 日", (Tuning.LifeSupportDays - s.HeldLife) * Tuning.LifePricePerDay, s.HeldLife < Tuning.LifeSupportDays - 0.5, Supply.BuyLife, "4");
        Sale($"船体 {s.HeldHull:P0}", (1 - s.HeldHull) * Tuning.HullRepairPrice, s.HeldHull < 0.999, Supply.RepairHull, "5");
        box.AddChild(Text("足りなければ買える分だけ。出港に足る燃料も買えなければ破産", 13, Dim));
        box.AddChild(new HSeparator());
        box.AddChild(Text("モジュール", 20, Good));
        for (int i = 0; i < Modules.Shop.Length; i++)
        {
            var m = Modules.Shop[i];
            var owned = s.Modules.Contains(m.Id);
            Control buy = _game.AtShipyard ? Press($"{m.Price:F0}", () => _game.Buy(m.Id, m.Price, m.Name), s.Credits >= m.Price, $"{i + 1}") : Text($"{m.Price:F0}", 14, Dim) as Control;
            box.AddChild(Row(Text(m.Name, 15), owned ? Text("搭載済み", 14, Good) : buy));
        }
        {
            var free = bays.Unpressurized.Any();
            var fitted = s.Pressurized.Count;
            var label = Modules.PressurizerName + (fitted > 0 ? $"（{fitted} 基搭載）" : "");
            Control buy = !free ? Text(fitted > 0 ? "全液体ベイに搭載済み" : "液体ベイがない", 14, Dim)
                : _game.AtShipyard ? Press($"{Modules.PressurizerPrice:F0}", _game.BuyPressurizer, s.Credits >= Modules.PressurizerPrice)
                : Text($"{Modules.PressurizerPrice:F0}", 14, Dim);
            box.AddChild(Row(Text(label, 15), buy));
        }
        if (!_game.AtShipyard)
            box.AddChild(Text(_game.NearestShipyard() is var yard and >= 0 ? $"買えるのは造船所だけ。最寄りは {g.Stars[yard].Name} ▲" : "買えるのは造船所だけ", 13, Dim));
        return box;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Ui;

namespace Starbearer;

/// <summary>The station's window: its mission board, the hold, and what it sells. Built from the checkpoint.</summary>
public partial class PortWindow : PanelContainer
{
    readonly Main _game;
    readonly VBoxContainer _root = new();
    static readonly Color Good = Palette.Good, Warn = Palette.Warn;
    static readonly Color Dim = Ui.PanelDim;

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
        AddThemeStyleboxOverride("panel", Ui.Frame(0.97f, 22, 16));
        _root.AddThemeConstantOverride("separation", 10);
        AddChild(_root);
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

    /// <summary>The tab open: jobs and hold, the market, the ships. Kept across rebuilds.</summary>
    int _tab;

    public void ShowTab(int tab) { _tab = tab; Rebuild(); }

    static HBoxContainer Columns(params Control[] columns)
    {
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 28);
        foreach (var c in columns) body.AddChild(Scroll(c));
        return body;
    }

    public void Rebuild()
    {
        Ui.Clear(_root);
        var s = _game.Save!;
        var g = _game.G!;
        var star = _game.V!.SystemStar;
        _root.AddChild(Text($"{g.Stars[star].Name}   {Missions.Name(Missions.KindOf(g, star))}   {g.Regions[g.Stars[star].Region].Name} 宙域", 28, Good));
        _root.AddChild(Text($"資金 {s.Credits:F0}   配達 {s.Deliveries} 件   {_game.GalaxyDayText}   ここがチェックポイント", 15, Dim));
        foreach (var line in _game.PortReport) _root.AddChild(Text(line, 16));
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        tabs.AddThemeFontSizeOverride("font_size", 16);
        var jobs = Columns(BoardColumn(g, s), HoldColumn(g, s)); jobs.Name = "仕事と積荷";
        var market = Columns(MarketColumn(g, s)); market.Name = "市場";
        var ships = Columns(HangarColumn(g, s), ModuleColumn(g, s)); ships.Name = "船";
        tabs.AddChild(jobs); tabs.AddChild(market); tabs.AddChild(ships);
        tabs.CurrentTab = _tab;
        tabs.TabChanged += t => _tab = (int)t;
        _root.AddChild(tabs);
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
        return box;
    }

    static string BayLabel(Bay b, bool fitted) => b.Pressurized || fitted ? "加圧液体ベイ" : Cargos.Name(b.Kind);

    /// <summary>A hull at a glance: its bays by kind, its empty delta-v, and how it flies.</summary>
    static string Spec(ShipClass c, IReadOnlyCollection<int>? fitted = null) =>
        string.Join(" ", c.Bays.Select((b, i) => (Label: BayLabel(b, fitted?.Contains(i) == true), b.Volume)).GroupBy(x => x.Label)
            .Select(k => $"{k.Key}{Missions.Cubic(k.Sum(x => x.Volume))}"))
        + $"   空荷Δv {Tuning.ExhaustVelocity * Math.Log((c.DryMass + c.Tank) / c.DryMass):F2}   抵抗×{c.Drag:0.0#} 推力×{c.Thrust:0.0#} {(c.Gates ? "ゲート可" : "ゲート不可")}";

    /// <summary>The company's ships docked here, and at a shipyard what it builds.</summary>
    Control HangarColumn(Galaxy g, SaveData s)
    {
        var box = Column(680);
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
        if (sold.Length == 0) return box;
        box.AddChild(Text("造船", 18, Good));
        foreach (var c in sold)
        {
            var info = new VBoxContainer();
            info.AddChild(Text(c.Name, 15));
            info.AddChild(Text(Spec(c), 13, Dim));
            box.AddChild(Row(info, Press($"購入 {c.Price:F0}", () => _game.BuyShip(c), s.Credits >= c.Price)));
        }
        return box;
    }

    Control HoldColumn(Galaxy g, SaveData s)
    {
        var box = Column(490);
        var bays = CargoBays.Of(s);
        box.AddChild(Text($"カーゴ  容積 {Missions.Cubic(Missions.VolumeUsed(s.Hold))} / {Missions.Cubic(bays.Capacity)}", 20, Good));
        box.AddChild(Text($"{bays.Ship.Name}: " + string.Join("、", bays.Ship.Bays.Select((b, i) =>
            $"{BayLabel(b, s.Pressurized.Contains(i))} {Missions.Cubic(b.Volume)}")), 14, Dim));
        if (s.Hold.Count == 0) box.AddChild(Text("空荷", 15, Dim));
        foreach (var c in s.Hold)
        {
            var info = new VBoxContainer();
            var cargo = c.Mission.Cargo;
            if (!c.IsJob)
            {
                info.AddChild(Text($"{cargo.Name}  自分の荷", 16));
                info.AddChild(Text($"{Cargos.Name(cargo.Kind)}   健全度 {c.Integrity:P0}   仕入 {c.Cost:F0}   容積 {Missions.Cubic(cargo.Volume)}", 13, Dim));
                box.AddChild(Row(info, Press("投棄", () => _game.DropCargo(c))));
                continue;
            }
            var target = c.Mission.Target;
            info.AddChild(Text($"{cargo.Name}  →  {g.Stars[target].Name}", 16));
            info.AddChild(Text($"{Cargos.Name(cargo.Kind)}   健全度 {c.Integrity:P0}   報酬 {c.Payout:F0}   容積 {Missions.Cubic(cargo.Volume)}", 13, Dim));
            var course = _game.Course == target ? Text("針路", 14, Good) as Control : Press("針路に", () => _game.SetCourse(target));
            box.AddChild(Row(info, course, Press($"破棄 -{Missions.DropFee(c):F0}", () => _game.DropCargo(c))));
        }
        box.AddChild(new HSeparator());
        box.AddChild(Text("補給", 20, Good));
        var (fuelMass, fuelCost) = Trade.FuelQuote(s, g, s.MarketIn(g));
        var fuelStock = s.MarketIn(g).StockOf(g, s.PortStar, Wares.ShipFuel);
        var tank = s.Ship.Type.Tank;
        var fuelLabel = $"燃料 {s.HeldFuel:F2} / {tank:F1}   在庫 {fuelStock:F0} 単位" + (fuelStock < 0.05 ? "  品切れ" : fuelMass < tank - s.HeldFuel - 1e-6 ? "  在庫の分だけ" : "");
        box.AddChild(Row(Text(fuelLabel, 15, fuelStock < 0.05 ? Warn : null), Press($"{fuelCost:F0}", _game.BuyFuel, fuelMass > 1e-4, "3")));
        void Sale(string name, double cost, bool need, Action<SaveData> buy, string key) =>
            box.AddChild(Row(Text(name, 15), Press($"{cost:F0}", () => _game.Purchase(buy), need, key)));
        Sale($"生命維持 残 {s.HeldLife:F0} 日", (Tuning.LifeSupportDays - s.HeldLife) * Tuning.LifePricePerDay, s.HeldLife < Tuning.LifeSupportDays - 0.5, Supply.BuyLife, "4");
        Sale($"船体 {s.HeldHull:P0}", (1 - s.HeldHull) * Tuning.HullRepairPrice, s.HeldHull < 0.999, Supply.RepairHull, "5");
        box.AddChild(Text("足りなければ買える分だけ。燃料は港の在庫から。出港に足る燃料も買えなければ破産", 13, Dim));
        return box;
    }

    /// <summary>What the station deals in: price and stock per ware, buying what it makes and selling it what it uses.</summary>
    Control MarketColumn(Galaxy g, SaveData s)
    {
        var box = Column(1180);
        var m = s.MarketIn(g);
        var star = s.PortStar;
        var e = g.Economies[star];
        box.AddChild(Text("市場", 20, Good));
        box.AddChild(Text("ここで作る物を買い、ここで使う物を売れる。燃料はどこでも買える。価格は在庫しだいで、目標量ちょうどで基準価格。売り買いするほど値が動く", 13, Dim));
        foreach (var ware in e.Wares)
        {
            var w = Wares.Get(ware);
            var i = e.IndexOf(ware);
            var stock = m.Stock[star][i];
            var price = Economy.Price(w, stock / e.Target[i]);
            var role = e.Makes(ware) ? "製造" : ware == Wares.ShipFuel && !e.Uses(ware) ? "給油用" : "消費";
            var info = new VBoxContainer();
            info.AddChild(Text($"{w.Name}  {role}", 16));
            info.AddChild(Text($"{Cargos.Name(w.Kind)}   在庫 {stock:F0} / 目標 {e.Target[i]:F0}   質量 {w.Mass:F2}/単位   基準 {w.Price:F0}", 13, Dim));
            var cheap = price < w.Price * 0.9; var dear = price > w.Price * 1.1;
            var controls = new List<Control> { Text($"{price:F0}", 17, cheap ? Good : dear ? Warn : null) };
            if (Trade.Sells(g, star, ware))
            {
                var most = Trade.MaxBuy(s, g, m, ware);
                if (most > 0)
                {
                    controls.Add(Press($"買う 1  {Trade.Cost(g, m, star, ware, 1, true):F0}", () => _game.BuyWare(ware, 1)));
                    if (most > 1) controls.Add(Press($"買う {most}  {Trade.Cost(g, m, star, ware, most, true):F0}", () => _game.BuyWare(ware, most)));
                }
                else controls.Add(Text(CargoBays.Of(s).RoomFor(w.Kind) <= 0 ? $"{Cargos.BayName(w.Kind)}がない" : stock < 1 ? "在庫なし"
                    : !CargoBays.Of(s).Fits(s.Hold, w.Load(1)) ? "ベイが満杯" : "資金不足", 13, Dim));
            }
            foreach (var lot in s.Hold.Where(c => !c.IsJob && c.Mission.Cargo.Ware == ware))
                if (Trade.Offer(g, m, star, lot) is { } offer)
                    controls.Add(Press($"売る {lot.Mission.Cargo.Volume:F0}  {offer:F0}", () => _game.SellLot(lot)));
            box.AddChild(Row(info, controls.ToArray()));
        }
        var unsold = s.Hold.Where(c => !c.IsJob && !Trade.Buys(g, star, c.Mission.Cargo.Ware!)).ToList();
        if (unsold.Count > 0)
            box.AddChild(Text("ここでは売れない荷: " + string.Join("、", unsold.Select(c => $"{c.Mission.Cargo.Name} {c.Mission.Cargo.Volume:F0}")), 13, Dim));
        box.AddChild(new HSeparator());
        box.AddChild(Row(Text("停泊して待つ。待つ間は生命維持を使わず、在庫と価格が動く", 14, Dim), Press($"{Tuning.PortWaitDays:F0} 日待つ", () => _game.WaitAtPort(Tuning.PortWaitDays))));
        return box;
    }

    Control ModuleColumn(Galaxy g, SaveData s)
    {
        var box = Column(490);
        var bays = CargoBays.Of(s);
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

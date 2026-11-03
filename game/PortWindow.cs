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
        var station = s.PortStation;
        var owner = Factions.OwnerName(g, station);
        _root.AddChild(Text($"{g.StarAt(station).Name}   {Missions.Name(g.KindOf(station))}   {g.Regions[g.StarAt(station).Region].Name} 宙域" + (owner != "" ? $"   {owner}" : ""), 28, Good));
        _root.AddChild(Text($"資金 {s.Credits:F0}   配達 {s.Deliveries} 件   {_game.GalaxyDayText}   ここがチェックポイント", 15, Dim));
        if (g.Factions.Length > 0)
            _root.AddChild(Text($"スタンディング   {Factions.Summary(s, g)}   （{Factions.HostileAt:+0;-0} 以下で敵対: 入港とゲートを拒まれる）", 14, Dim));
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
            box.AddChild(Text($"ゲートの行き先: {g.StarAt(exit).Name}（{g.Regions[g.StarAt(exit).Region].Name} 宙域）", 17));
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
            var far = (g.StarAt(m.Target).Position - g.StarAt(m.Origin).Position).Length;
            var refusal = bays.Refusal(s.Hold, c);
            var info = new VBoxContainer();
            var owner = Factions.OwnerName(g, m.Target);
            info.AddChild(Text($"{c.Name}  →  {g.StarAt(m.Target).Name}（{Missions.Name(g.KindOf(m.Target))}）" + (owner != "" ? $"  {owner}" : ""), 17));
            if (!Factions.Admits(s, g, m.Target)) info.AddChild(Text($"届け先の {owner} は敵対している: 入港できない", 13, Warn));
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
        var star = s.PortStation;
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
        var away = s.Fleet.Where(x => x.Station != star).ToList();
        if (away.Count > 0)
            box.AddChild(Text("ほかの港: " + string.Join("、", away.Select(x => $"{x.Type.Name}（{Missions.Label(g, x.Station)}）")), 13, Dim));
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
            var target = g.StarOf(c.Mission.Target);
            info.AddChild(Text($"{cargo.Name}  →  {Missions.Label(g, c.Mission.Target)}", 16));
            if (!Factions.Admits(s, g, c.Mission.Target)) info.AddChild(Text($"届け先の {Factions.OwnerName(g, c.Mission.Target)} は敵対している: 入港できない", 13, Warn));
            info.AddChild(Text($"{Cargos.Name(cargo.Kind)}   健全度 {c.Integrity:P0}   報酬 {c.Payout:F0}   容積 {Missions.Cubic(cargo.Volume)}", 13, Dim));
            var course = _game.Course == target ? Text("針路", 14, Good) as Control : Press("針路に", () => _game.SetCourse(target));
            box.AddChild(Row(info, course, Press($"破棄 -{Missions.DropFee(c):F0}", () => _game.DropCargo(c))));
        }
        box.AddChild(new HSeparator());
        box.AddChild(Text("補給", 20, Good));
        var (fuelMass, fuelCost) = Trade.FuelQuote(s, g, s.MarketIn(g));
        var fuelStock = s.MarketIn(g).StockOf(g, s.PortStation, Wares.ShipFuel);
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
        var star = s.PortStation;
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
            var carried = CargoBays.Of(s).RoomFor(w.Kind) > 0;
            // A ware this ship has no bay for is struck through in red: it can't be bought into this hold.
            info.AddChild(carried ? Text($"{w.Name}  {role}", 16) : Struck($"{w.Name}  {role}  （{Cargos.BayName(w.Kind)}がない）", 16));
            info.AddChild(Text($"{Cargos.Name(w.Kind)}   在庫 {stock:F0} / 目標 {e.Target[i]:F0}   質量 {w.Mass:F2}/単位   基準 {w.Price:F0}", 13, Dim));
            var cheap = price < w.Price * 0.9; var dear = price > w.Price * 1.1;
            var controls = new List<Control> { Text($"{price:F0}", 17, cheap ? Good : dear ? Warn : null) };
            var deals = new VBoxContainer();
            if (Trade.Sells(g, star, ware) && carried)
            {
                var most = Trade.MaxBuy(s, g, m, ware);
                deals.AddChild(most > 0 ? Deal("買う", most, n => Trade.Cost(g, m, star, ware, n, true), n => _game.BuyWare(ware, n))
                    : Text(stock < 1 ? "在庫なし" : !CargoBays.Of(s).Fits(s.Hold, w.Load(1)) ? "ベイが満杯" : "資金不足", 13, Dim));
            }
            if (Trade.Buys(g, star, ware) && Trade.Held(s, ware) is var held and > 0)
                deals.AddChild(Deal("売る", held, n => Trade.SaleQuote(s, g, m, ware, n) ?? 0, n => _game.SellWare(ware, n)));
            controls.Add(deals);
            box.AddChild(Row(info, controls.ToArray()));
        }
        var unsold = s.Hold.Where(c => !c.IsJob && !Trade.Buys(g, star, c.Mission.Cargo.Ware!)).ToList();
        if (unsold.Count > 0)
            box.AddChild(Text("ここでは売れない荷: " + string.Join("、", unsold.Select(c => $"{c.Mission.Cargo.Name} {c.Mission.Cargo.Volume:F0}")), 13, Dim));
        box.AddChild(new HSeparator());
        Quotes(box, g, s, m);
        box.AddChild(new HSeparator());
        box.AddChild(Row(Text("停泊して待つ。待つ間は生命維持を使わず、在庫と価格が動く", 14, Dim), Press($"{Tuning.PortWaitDays:F0} 日待つ", () => _game.WaitAtPort(Tuning.PortWaitDays))));
        return box;
    }

    /// <summary>Text struck through in red: something this ship can't do here.</summary>
    static RichTextLabel Struck(string text, int size)
    {
        var l = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.Off,
            Text = $"[s][color=#ff6b5e]{text}[/color][/s]", CustomMinimumSize = new Vector2(420, 0), MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("normal_font_size", size);
        return l;
    }

    /// <summary>
    /// A trade X-style: a slider for how many units, what that comes to, and a button to confirm. The
    /// total follows the slider, unit by unit as the trade would move the price.
    /// </summary>
    static HBoxContainer Deal(string verb, int most, Func<int, double> total, Action<int> confirm)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var slider = new HSlider { MinValue = 0, MaxValue = most, Step = 1, Value = most, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(170, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var label = Text("", 14);
        label.CustomMinimumSize = new Vector2(150, 0);
        var go = Press($"{verb}", () => { }, true);
        void Show()
        {
            var n = (int)slider.Value;
            label.Text = $"{n} 単位  {(n > 0 ? total(n) : 0):F0}";
            go.Disabled = n == 0;
        }
        slider.ValueChanged += _ => Show();
        go.Pressed += () => confirm((int)slider.Value);
        Show();
        row.AddChild(slider);
        row.AddChild(label);
        row.AddChild(go);
        return row;
    }

    /// <summary>The ware looked up in other ports' quotes. Kept across rebuilds.</summary>
    string? _lookup;

    /// <summary>
    /// Other stations' prices for one ware, as news has brought them here: where it can be bought,
    /// cheapest first, and where it sells, dearest first. Each says how old its news is.
    /// </summary>
    void Quotes(VBoxContainer box, Galaxy g, SaveData s, Market m)
    {
        var here = s.PortStation;
        var held = s.Hold.Where(c => c.Mission.Cargo.Ware != null).Select(c => c.Mission.Cargo.Ware!);
        var ware = _lookup ?? held.FirstOrDefault() ?? g.Economies[here].Wares[0];
        var pick = new OptionButton { FocusMode = FocusModeEnum.None };
        for (int i = 0; i < Wares.All.Length; i++) pick.AddItem(Wares.All[i].Name, i);
        pick.Selected = Wares.IndexOf(ware);
        pick.ItemSelected += i => { _lookup = Wares.All[(int)i].Id; Rebuild(); };
        box.AddChild(Row(Text("ほかの港の相場（届いている知らせの分だけ古い）", 16, Good), pick));
        var w = Wares.Get(ware);
        (int Star, double Price, double Fill, double Age)? Quote(int star) =>
            star != here && m.Known(g, here, star, ware) is { } k ? (star, Economy.Price(w, k.Fill), k.Fill, m.Day - k.Day) : null;
        string Line((int Star, double Price, double Fill, double Age) q)
        {
            var st = g.StarAt(q.Star);
            var far = (st.Position - g.StarAt(here).Position).Length;
            var stock = q.Fill < 0.1 ? "品切れ近い" : q.Fill < 0.6 ? "少ない" : q.Fill < 1.3 ? "普通" : "多い";
            return $"{Missions.Label(g, q.Star)}  {g.Regions[st.Region].Name}  {far:F0} ly   {q.Price:F0}   在庫{stock}   {q.Age:F0} 日前";
        }
        var sellers = g.EconomyOrder.Where(x => Trade.Sells(g, x, ware)).Select(Quote).OfType<(int, double, double, double)>().OrderBy(q => q.Item2).Take(6).ToList();
        var buyers = g.EconomyOrder.Where(x => Trade.Buys(g, x, ware)).Select(Quote).OfType<(int, double, double, double)>().OrderByDescending(q => q.Item2).Take(6).ToList();
        var lists = new HBoxContainer();
        lists.AddThemeConstantOverride("separation", 30);
        VBoxContainer List(string title, List<(int, double, double, double)> quotes)
        {
            var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            col.AddChild(Text(title, 14, Good));
            if (quotes.Count == 0) col.AddChild(Text("知らせがない", 13, Dim));
            foreach (var q in quotes) col.AddChild(Text(Line(q), 13, Dim));
            return col;
        }
        lists.AddChild(List($"{w.Name}を買える所（安い順）", sellers));
        lists.AddChild(List($"{w.Name}を売れる所（高い順）", buyers));
        box.AddChild(lists);
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
            box.AddChild(Text(_game.NearestShipyard() is var yard and >= 0 ? $"買えるのは造船所だけ。最寄りは {g.StarAt(yard).Name} ▲" : "買えるのは造船所だけ", 13, Dim));
        return box;
    }
}

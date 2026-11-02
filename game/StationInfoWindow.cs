using System;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Ui;

namespace Starbearer;

/// <summary>
/// Another station as the news that reached the last port tells it: its market and the jobs it had
/// posted, with how old that news is. Opened from the chart's menu on a star.
/// </summary>
public partial class StationInfoWindow : PanelContainer
{
    readonly Main _game;
    readonly VBoxContainer _root = new();
    static readonly Color Good = Palette.Good, Warn = Palette.Warn, Dim = Ui.PanelDim;

    public int Star { get; private set; } = -1;

    public StationInfoWindow(Main game)
    {
        _game = game;
        Visible = false;
        CustomMinimumSize = new Vector2(640, 0);
        SetAnchorsPreset(LayoutPreset.CenterTop);
        OffsetTop = 70;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.End;
        AddThemeStyleboxOverride("panel", Ui.Frame(0.95f, 16, 12));
        _root.AddThemeConstantOverride("separation", 4);
        AddChild(_root);
    }

    public void Open(int star) { Star = star; Rebuild(); }
    public void Close() { Star = -1; }

    public void Rebuild()
    {
        Ui.Clear(_root);
        var g = _game.G!; var s = _game.Save!;
        if (Star < 0 || !g.Economies.TryGetValue(Star, out var e)) return;
        var m = s.MarketIn(g);
        var st = g.Stars[Star];
        var here = s.PortStar;
        var known = m.KnownAs(g, here, Star);
        var age = known == null ? (double?)null : m.Day - known.Day;
        _root.AddChild(Row(Text($"{st.Name}{Missions.Mark(g, Star)}  {Missions.Name(Missions.KindOf(g, Star))}  {g.Regions[st.Region].Name} 宙域", 18, Good),
            Press("閉じる  Esc", _game.CloseStationInfo, size: 13)));
        var far = (st.Position - g.Stars[here].Position).Length;
        _root.AddChild(Text(known == null ? "この港の知らせはまだ届いていない"
            : (age < 0.5 ? "いまの様子" : $"{age:F0} 日前の知らせ（{g.Stars[here].Name} から {far:F0} ly）") + "。着いたときには変わっている", 13, Dim));
        if (known == null) return;
        _root.AddChild(new HSeparator());
        _root.AddChild(Text("市場", 15, Good));
        foreach (var ware in e.Wares)
        {
            var w = Wares.Get(ware);
            var fill = known.Fill(g, Star, ware)!.Value;
            var price = Economy.Price(w, fill);
            var role = e.Makes(ware) ? "製造" : ware == Wares.ShipFuel && !e.Uses(ware) ? "給油用" : "消費";
            var stock = fill < 0.1 ? "品切れ近い" : fill < 0.6 ? "少ない" : fill < 1.3 ? "普通" : "多い";
            var cheap = price < w.Price * 0.9; var dear = price > w.Price * 1.1;
            _root.AddChild(Row(Text($"{w.Name}  {role}   在庫{stock}", 14), Text($"{price:F0}", 14, cheap ? Good : dear ? Warn : null)));
        }
        if (Missions.KindOf(g, Star) == StationKind.Gate) return;
        _root.AddChild(new HSeparator());
        _root.AddChild(Text("貼り出されていた仕事", 15, Good));
        var board = Missions.Board(g, known, Star, s.Deliveries, s.Hold);
        if (board.Count == 0) _root.AddChild(Text("なし", 13, Dim));
        foreach (var job in board)
        {
            var to = g.Stars[job.Target];
            _root.AddChild(Row(Text($"{job.Cargo.Name} {job.Cargo.Volume:F0} 単位（{Cargos.Name(job.Cargo.Kind)}）  →  {to.Name}{Missions.Mark(g, job.Target)}", 14),
                Text($"報酬 {job.Pay:F0}", 14, Good)));
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>The console buttons, the advice line, and the cues for changes of state.</summary>
public partial class Main
{
    int _lastWell = -1, _lastPrimary = -1;
    Phase _lastPhase = Phase.Sublight;
    public StateCue? Cue { get; private set; }
    public float CueAge { get; private set; }
    bool IsDown(Key key) => Input.IsKeyPressed(key) || HeldButton == key;

    public void PressButton(Command c)
    {
        if (c.Hold) { HeldButton = c.Key; return; }
        _fromButton = true;
        try { _UnhandledInput(new InputEventKey { Keycode = c.Key, Pressed = true }); }
        finally { _fromButton = false; }
    }

    static List<(string, Command[])> RateGroup(double[] rates, int current) => new()
    {
        ("時間", rates.Select((r, i) => new Command(Key.Key1 + i, $"{i + 1}", i == current ? $"[{Instruments.Rate(r)}]" : Instruments.Rate(r))).ToArray()),
    };

    static Command[] ShipCommands(bool engineOn, Main g) => new[]
    {
        new Command(Key.E, "E", engineOn ? "停止" : "点火"),
        new Command(Key.W, "W", "推力+", Hold: true),
        new Command(Key.S, "S", "推力-", Hold: true),
        new Command(Key.O, "O", "全開"),
        new Command(Key.L, "L", "ゼロ"),
        new Command(Key.B, "B", "機首視点"),
    };

    static readonly Command[] NoseCommands =
    {
        new(Key.Left, "←", "左", Hold: true), new(Key.Right, "→", "右", Hold: true),
        new(Key.Up, "↑", "上", Hold: true), new(Key.Down, "↓", "下", Hold: true),
    };

    /// <summary>The console for the current screen, in groups. A dark button says why it can't be used yet by being dark.</summary>
    public List<(string Group, Command[] Items)> Commands()
    {
        var v = V!;
        var list = new List<(string, Command[])>();
        if (Screen == Screen.Plan)
        {
            list.Add(("噴射量", new Command[] { new(Key.W, "W", "増やす", Hold: true), new(Key.S, "S", "減らす", Hold: true) }));
            list.Add(("方位", NoseCommands));
            list.Add(("確定", new Command[] { new(Key.Enter, "Enter", "実行", !v.Docked), new(Key.Escape, "Esc", "取消"), new(Key.Tab, "Tab", MapOpen ? "空" : "星図") }));
            return list;
        }
        if (Screen == Screen.Orbit)
        {
            var st = v.LocalStation;
            var way = v.Waypoint;
            var nodes = v.Nodes.Count > 0;
            list.Add(("計画", new Command[]
            {
                new(Key.C, "", "目標:" + (v.Goal == null ? "なし" : NameOf(v.Goal))),
                new(Key.H, "", way == null && v.Primary.Parent != null ? "脱出案" : "遷移案", way != null || v.Primary.Parent != null),
                new(Key.M, "", "合流案", way is Station),
                new(Key.T, "", "計算機", way != null && Owns(Modules.Transfer)),
                new(Key.G, "G", v.AutoBurn ? "自動噴射 ON" : "自動噴射", Owns(Modules.AutoBurn) && !v.Docked),
                new(Key.Y, "", "再計画", nodes),
                new(Key.Backspace, "BS", "ノード削除", nodes),
            }));
            list.AddRange(RateGroup(Rates, Aligning ? 0 : SubRateIndex));
            list.Add(("待機", new Command[] { new(Key.Space, "Space", Paused ? "再開" : "停止"), new(Key.N, "N", ToNode ? "加速中" : "ノードへ", nodes) }));
            if (v.Docked)
            {
                list.Add(("港", new Command[] { new(Key.U, "U", "出港"), new(Key.Enter, "Enter", "港の施設") }));
                list.Add(("航行", new Command[] { new(Key.J, "J", "発進計画"), new(Key.Tab, "Tab", OrbitSky ? "軌道図" : "空"), new(Key.I, "I", "積荷") }));
                return list;
            }
            list.Add(("操船", ShipCommands(Helm.EngineOn, this)));
            list.Add(("機首", NoseCommands));
            var port = new List<Command> { new(Key.X, "X", "接近", st != null, Hold: true), new(Key.Z, "Z", "相対0", st != null, Hold: true), new(Key.U, "U", "入港", v.CanDock) };
            if (v.AtGate) port.Add(new(Key.K, "", !v.Class.Gates ? "ゲート不可" : v.GateBooked != null ? "通過を取消" : $"ゲート通過 {GateToll:F0}", v.Class.Gates));
            list.Add(("港", port.ToArray()));
            list.Add(("航行", new Command[] { new(Key.J, "J", "FTL発進"), new(Key.Tab, "Tab", OrbitSky ? "軌道図" : "空"), new(Key.I, "I", "積荷") }));
            return list;
        }
        list.Add(("操船", ShipCommands(Helm.EngineOn, this)));
        list.Add(("機首", NoseCommands));
        list.AddRange(RateGroup(Rates, RateIndex));
        list.Add(("待機", new Command[] { new(Key.Space, "Space", Paused ? "再開" : "停止") }));
        list.Add(("航行", new Command[]
        {
            new(Key.D, "D", "降下", v.Phase == Phase.Flight && v.NearestWell(out _) < double.MaxValue),
            new(Key.C, "C", "修正計画"),
            new(Key.F, "F", "天測"),
            new(Key.Tab, "Tab", MapOpen ? "空" : "星図"),
            new(Key.I, "I", "積荷"),
        }));
        return list;
    }

    /// <summary>The one thing worth doing now, and the button that does it.</summary>
    public (Key? Key, string Text) Advice()
    {
        var v = V!;
        if (Screen == Screen.Plan && v.Docked) return (Key.Escape, "停泊中は計画の確認まで。発進は出港してから");
        if (Screen == Screen.Plan) return (Key.Enter, $"方位と噴射量を決めて実行  目的地は {G!.Stars[v.TargetStar].Name}");
        if (v.Phase == Phase.Stalled) return (Key.C, "失速した。修正計画で再加速する");
        if (Screen == Screen.Flight)
        {
            if (v.NearestWell(out var star) < double.MaxValue && star != null)
            {
                if (star.Id == v.TargetStar) return (Key.D, "目的の星系。近日点の手前で降下する");
                if (v.Hold.Any(c => c.Mission.Target == star.Id)) return (Key.D, "積荷の届け先の星系。寄るなら降下する");
                if (G!.HasStation(star.Id) && v.Fuel < Tuning.DepartureFuelFloor) return (Key.D, "港のある星系。寄港するなら降下する");
                return (null, $"{star.Name} の井戸を通過中。降下しなければ軌道が曲がるだけ");
            }
            return (null, "巡航中。次の恒星まで機首と燃料を整える");
        }
        if (v.Docked)
            return IgnitionIn is { } hold && hold > 0.05
                ? (ToNode ? null : Key.N, $"次の点火まで {hold:F1} 日。その前に出港する")
                : (Key.U, "港に停泊中。時機を選んで出港する");
        var st = v.LocalStation;
        if (v.Goal == null)
            return v.LocalStation == null ? (Key.J, "この星系に港はない。発進して次へ") : (Key.C, "目標なし。「目標」か天体のタップで選ぶ");
        if (v.GateBooked is { } toll && GateExit is var exit and >= 0)
            return (null, $"ゲート通過を予約済み（{toll:F0}）。ゲートまで寄れば {G!.Stars[exit].Name} へ出る。速さはそのまま");
        if (Helm.EngineOn && NextNode != null)
            return v.AutoBurn ? (null, "自動噴射中。終わると自動で止まる")
                : NodeOvershot ? (Key.E, "ノードを過ぎた。E で止める")
                : (Key.E, "ノード噴射中。残り Δv が 0 に近づいたら E で止める");
        if (st != null)
        {
            var (stationAt, stationSpeed) = v.StationNow();
            var distance = (stationAt - v.SubPosition).Length;
            var relative = (stationSpeed - v.SubVelocity).Length;
            if (v.CanDock) return (Key.J, "港のすぐそば。発進するか、U で入港し直す");
            if (distance < 0.05)
                return relative > 0.1 ? (Key.Z, $"港の近く。相対速度 {relative:F2} を押し続けて落とす") : (Key.X, $"港まで {distance:F3} AU。押し続けて寄せる");
        }
        if (IgnitionIn is { } wait)
            return wait > 0.05 ? (ToNode ? null : Key.N, $"次の点火まで {wait:F1} 日" + (v.AutoBurn ? "。自動噴射が点火する" : "。ノードまで時間を進める")) : (Key.E, "点火の時機。機首をノードに向けて点火");
        if (SubForecast is { Watched: Station, Closest.Distance: < 0.03 }) return (Key.M, "港に接近する軌道。最接近で速度を合わせる");
        var goal = NameOf(v.Goal);
        return v.Waypoint switch
        {
            Station => (Key.H, "港へ向かう軌道を作る"),
            Center body when body == v.Goal => (Key.H, $"{body.Name} へ向かう軌道を作る"),
            Center body => (Key.H, $"{goal}は {body.Name} の周りにある。{body.Name} へ向かう軌道を作る"),
            _ when v.Goal is Center c && c.Key == v.Primary.Key => (Key.C, $"{c.Name} を周回中。「目標」で次の目標を選ぶ"),
            _ => (Key.H, $"{goal}はこの天体の外にある。ここを離れる軌道を作る"),
        };
    }

    /// <summary>Banners for changes nothing in the flight code announces by itself.</summary>
    void WatchState()
    {
        if (V is not { } v || Screen is not (Screen.Flight or Screen.Orbit)) { _lastWell = -1; _lastPrimary = -1; return; }
        if (Screen == Screen.Orbit)
        {
            var primary = v.Adrift ? -2 : v.Primary.Key;
            if (_lastPrimary != -1 && primary != _lastPrimary)
            {
                if (primary >= 0) Chart.Glide(PrimaryOnChart, ChartView.ZoomFor(v.Primary));
                Replan();
            }
            _lastPrimary = primary;
        }
        else _lastPrimary = -1;
        if (v.Phase == Phase.Stalled && _lastPhase != Phase.Stalled)
            ShowCue(new StateCue("FTL 脱落", "速度が失速線を割った", new Color(1f, 0.3f, 0.25f), Major: true, Outward: false));
        _lastPhase = v.Phase;
        var well = -1;
        if (v.Phase == Phase.Flight && v.NearestWell(out var star) < double.MaxValue && star != null) well = star.Id;
        if (well != _lastWell && well >= 0)
            ShowCue(new StateCue($"{G!.Stars[well].Name} の井戸に進入", well == v.TargetStar ? "目的の星系" : "", new Color(1f, 0.7f, 0.25f), Major: false, Outward: true));
        _lastWell = well;
    }

    void ShowCue(StateCue cue)
    {
        if (Cue is { Major: true } && !cue.Major) return;
        Cue = cue;
        CueAge = 0;
    }
}

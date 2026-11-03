using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Ui;

namespace Starbearer;

/// <summary>The port's war tab.</summary>
public partial class PortWindow
{
    /// <summary>
    /// The war: each faction's weapon and what it burns, the systems fought over with each side's
    /// fortresses, their structure and supply, and the latest news.
    /// </summary>
    Control WarColumn(Galaxy g, SaveData s)
    {
        var box = Column(1180);
        var m = s.MarketIn(g);
        box.AddChild(Text("戦況", 20, Good));
        box.AddChild(Wrapped("要塞は兵器の弾薬と燃料が足りているほど強い。補給の差で勝敗が決まる。要塞に弾薬・燃料・食品・鋼鉄（次の要塞の建材）を売ると、その勢力のスタンディングが大きく上がる。首都が無傷なら要塞は全力で撃ち、傷つくと火力が落ちる。陥落すると遷都まで大きく落ちる", 13, Dim));
        foreach (var f in g.Factions)
        {
            var forts = War.Fortresses(g).Where(x => g.OwnerOf(x) == f.Id).ToList();
            var held = g.Stars.Count(x => g.Claim(x.Id).Holder == f.Id);
            var capital = War.Capital(g, f.Id);
            var seat = capital >= 0 ? $"首都 {g.StarAt(capital).Name}（構造 {m.War.HullOf(capital):P0}）"
                : m.War.CapitalFell.TryGetValue(f.Id, out var fell) ? $"首都陥落（遷都まで {Math.Max(0, fell + War.RelocateDays - m.Day):F0} 日）" : "首都なし";
            box.AddChild(Wrapped($"{f.Name}   主力兵器 {War.WeaponName(f.Weapon)}（{Wares.Get(f.Weapon).Name}を消費）   {seat}   火力 ×{War.Command(g, m, f.Id):F2}   要塞 {forts.Count}   領有 {held} 星系   次の要塞の建材 {Math.Min(1, m.War.Progress.ElementAtOrDefault(f.Id)):P0}", 16, Palette.Faction(f.Id)));
        }
        box.AddChild(new HSeparator());
        box.AddChild(Text("係争中の星系", 17, Good));
        var fronts = War.Fronts(g).OrderBy(x => (g.Stars[x].Position - g.StarAt(s.PortStation).Position).Length).ToList();
        if (fronts.Count == 0) box.AddChild(Text("なし", 14, Dim));
        foreach (var star in fronts)
        {
            var (holder, _) = g.Claim(star);
            var far = (g.Stars[star].Position - g.StarAt(s.PortStation).Position).Length;
            var sides = War.Sides(g, star).Select(f =>
            {
                var forts = g.StationsIn(star).Where(x => g.OwnerOf(x) == f && g.KindOf(x) == StationKind.Fortress).ToList();
                var others = g.StationsIn(star).Count(x => g.OwnerOf(x) == f) - forts.Count;
                var detail = forts.Count == 0 ? "要塞なし" : $"要塞 {forts.Count}  構造 {forts.Average(m.War.HullOf):P0}  補給 {forts.Average(x => War.Strength(g, m, x)):P0}";
                return $"{g.Factions[f].Name}: {detail}" + (others > 0 ? $"  ほか {others} 基" : "");
            });
            box.AddChild(Text($"{g.Stars[star].Name}（{g.Factions[holder].Name} 領、{far:F0} ly）   " + string.Join("   対   ", sides), 14));
        }
        box.AddChild(new HSeparator());
        box.AddChild(Text("最近の出来事", 17, Good));
        var log = m.War.Log.AsEnumerable().Reverse().Take(12).ToList();
        if (log.Count == 0) box.AddChild(Text("なし", 14, Dim));
        foreach (var e in log) box.AddChild(Text($"{Instruments.Day(e.Day)}  {War.Describe(g, e)}", 14, Dim));
        return box;
    }
}

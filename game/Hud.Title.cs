using System;
using System.Linq;
using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>The title: the view from a cockpit behind, and the companies on disk to carry on with, or a new galaxy.</summary>
public partial class Hud
{
    const float TitleLeft = 90, TitleWidth = 620, TitleRowH = 50, BinWidth = 120;

    float ListTop => Size.Y * 0.44f;
    int VisibleRows => Math.Max(2, (int)((Size.Y - 70 - ListTop) / TitleRowH));
    /// <summary>The first row shown: the list scrolls to keep the picked row in sight.</summary>
    int FirstRow => Math.Clamp(Game.TitleChoice - VisibleRows + 1, 0, Math.Max(0, Game.Saves.Count + 1 - VisibleRows));

    Rect2 TitleRow(int row) => new(TitleLeft - 12, ListTop + (row - FirstRow) * TitleRowH, TitleWidth + 24, TitleRowH - 4);
    Rect2 TitleBin(int row) { var r = TitleRow(row); return new Rect2(r.End.X - BinWidth, r.Position.Y, BinWidth, r.Size.Y); }

    bool RowShown(int row) => row >= FirstRow && row < FirstRow + VisibleRows && row <= Game.Saves.Count;

    public int? TitleRowAt(Vector2 at)
    {
        for (int row = FirstRow; RowShown(row); row++)
            if (TitleRow(row).HasPoint(at)) return row;
        return null;
    }

    /// <summary>The delete button at the right end of a save's row (drawn on the picked one).</summary>
    public int? TitleDeleteAt(Vector2 at)
    {
        for (int row = FirstRow; RowShown(row) && row < Game.Saves.Count; row++)
            if (TitleBin(row).HasPoint(at)) return row;
        return null;
    }

    public Vector2 TitleRowForTest(int row) => TitleRow(row).GetCenter();
    public Vector2 TitleBinForTest(int row) => TitleBin(row).GetCenter();

    /// <summary>Black between the title's scenes, and a shadow down the left for the words to stand on.</summary>
    void DrawShade()
    {
        if (Game.TitleShade > 0) DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, Game.TitleShade));
        var w = TitleLeft + TitleWidth + 260;
        var dark = new Color(0, 0, 0, 0.72f);
        var clear = new Color(0, 0, 0, 0);
        DrawPolygon(new[] { Vector2.Zero, new Vector2(w, 0), new Vector2(w, Size.Y), new Vector2(0, Size.Y) }, new[] { dark, clear, clear, dark });
    }

    void DrawTitle()
    {
        DrawShade();
        var top = Size.Y * 0.2f;
        Text(new Vector2(TitleLeft, top + 56), "STARBEARER", Ink, 64);
        Text(new Vector2(TitleLeft + 4, top + 96), "星間運送", Dim, 22);

        for (int row = FirstRow; RowShown(row); row++) DrawTitleRow(row);
        if (FirstRow > 0) Text(new Vector2(TitleLeft, ListTop - 8), "▲", Dim, 12);
        if (FirstRow + VisibleRows <= Game.Saves.Count) Text(new Vector2(TitleLeft, ListTop + VisibleRows * TitleRowH + 4), "▼", Dim, 12);

        var hint = Game.HasSave ? "↑↓ 選ぶ    Enter 開く    N 新しい銀河    Delete 削除" : "Enter 新しい銀河";
        Text(new Vector2(TitleLeft, Size.Y - 36), hint, Dim, 15);
        var y = Size.Y - 64;
        if (Game.SaveRefused is { } why) { Text(new Vector2(TitleLeft, y), why, Warn, 15); y -= 24; }
        if (Game.LoadFailed) Text(new Vector2(TitleLeft, y), "読み込みに失敗した", Warn, 15);
    }

    void DrawTitleRow(int row)
    {
        var box = TitleRow(row);
        var picked = row == Game.TitleChoice;
        if (picked)
        {
            DrawRect(box, new Color(Ink, 0.1f));
            DrawRect(new Rect2(box.Position, 3, box.Size.Y), Ink);
        }
        var x = TitleLeft;
        var y = box.Position.Y;
        if (row == Game.Saves.Count)
        {
            Text(new Vector2(x, y + 30), "＋  新しい銀河を始める", picked ? Ink : Dim, 19);
            return;
        }
        var e = Game.Saves[row];
        var when = e.When.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        var whenW = F.GetStringSize(when, HorizontalAlignment.Left, -1, 13).X;
        var right = box.End.X - 12 - (picked ? BinWidth : 0);
        Text(new Vector2(right - whenW, y + 21), when, Dim, 13);
        if (e.Data is not { } s)
        {
            Text(new Vector2(x, y + 21), "読めないセーブ", Warn, 18);
            Text(new Vector2(x, y + 40), System.IO.Path.GetFileName(e.Path), Dim, 13);
        }
        else
        {
            var where = s.Where != "" ? s.Where : $"銀河 {s.Seed % 100000:D5}";
            Text(new Vector2(x, y + 21), where, e.Playable ? (picked ? Ink : new Color(Ink, 0.8f)) : Dim, 18);
            var detail = e.Playable
                ? $"銀河暦 {s.TotalDays:F0} 日    配達 {s.Deliveries} 件    資金 {s.Credits:F0}    船 {s.Fleet.Count} 隻"
                : "古い版のセーブ。この版では続けられない";
            Text(new Vector2(x, y + 40), detail, e.Playable ? Dim : Warn, 13);
        }
        if (!picked) return;
        var bin = TitleBin(row);
        var label = Game.DeleteArmed ? "もう一度で削除" : "削除";
        var lw = F.GetStringSize(label, HorizontalAlignment.Left, -1, 14).X;
        if (Game.DeleteArmed) DrawRect(bin, new Color(Bad, 0.18f));
        Text(new Vector2(bin.GetCenter().X - lw / 2, bin.GetCenter().Y + 5), label, Game.DeleteArmed ? Bad : Dim, 14);
    }
}

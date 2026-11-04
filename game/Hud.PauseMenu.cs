using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>The pause menu, over whatever screen it was opened on.</summary>
public partial class Hud
{
    const float PauseWidth = 520, PauseRowH = 58;

    float PauseTop => Size.Y * 0.5f - (Main.PauseItems.Length * PauseRowH + 110) / 2;
    Rect2 PauseRow(int row) => new((Size.X - PauseWidth) / 2, PauseTop + 92 + row * PauseRowH, PauseWidth, PauseRowH - 6);

    public int? PauseRowAt(Vector2 at)
    {
        for (int row = 0; row < Main.PauseItems.Length; row++)
            if (PauseRow(row).HasPoint(at)) return row;
        return null;
    }

    public Vector2 PauseRowForTest(int row) => PauseRow(row).GetCenter();

    void DrawPauseMenu()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.6f));
        var s = Game.Save!;
        var box = new Rect2((Size.X - PauseWidth) / 2 - 24, PauseTop, PauseWidth + 48, Main.PauseItems.Length * PauseRowH + 102);
        DrawRect(box, new Color(0.02f, 0.03f, 0.06f, 0.95f));
        DrawRect(box, new Color(Ink, 0.4f), false, 1);
        var x = box.Position.X + 24;
        Text(new Vector2(x, PauseTop + 40), "一時停止", Ink, 26);
        var where = s.Where != "" ? s.Where : $"銀河 {s.Seed % 100000:D5}";
        Text(new Vector2(x, PauseTop + 68), $"最後の入港 {where}   {Instruments.Day(s.TotalDays)}", Dim, 14);
        var loss = Game.PauseLoss;
        for (int row = 0; row < Main.PauseItems.Length; row++)
        {
            var item = Main.PauseItems[row];
            var r = PauseRow(row);
            var picked = row == Game.PauseChoice;
            var armed = Game.PauseArmed == item;
            var on = Game.PauseEnabled(item);
            if (picked)
            {
                DrawRect(r, new Color(armed ? Bad : Ink, armed ? 0.18f : 0.1f));
                DrawRect(new Rect2(r.Position, 3, r.Size.Y), armed ? Bad : Ink);
            }
            var (label, detail) = item switch
            {
                Main.PauseItem.Resume => ("続ける", "Esc"),
                Main.PauseItem.Restart => ("最後の入港からやり直す", Game.Bankrupt ? "破産したのでやり直せない" : "再開の回数に数える"),
                Main.PauseItem.Title => ("タイトルへ", "セーブの切り替え・新しい銀河"),
                _ => ("ゲームを終了", ""),
            };
            if (armed) detail = $"もう一度押すと実行。最後の入港から {Instruments.Span(loss)} の航海は失われる";
            var col = !on ? new Color(Dim, 0.5f) : armed ? Bad : picked ? Ink : new Color(Ink, 0.8f);
            Text(new Vector2(r.Position.X + 16, r.Position.Y + 23), label, col, 18);
            if (detail != "") Text(new Vector2(r.Position.X + 16, r.Position.Y + 43), detail, armed ? Bad : Dim, 13);
        }
    }
}

using Godot;
using Starbearer.Sim;
using static Starbearer.Palette;

namespace Starbearer;

/// <summary>Everything drawn over the sky: instruments, the trajectory projected on the sphere, and the chart.</summary>
public partial class Hud : Control
{
    public Main Game = null!;
    Font F => ThemeDB.FallbackFont;
    bool _mapDragging;
    public Hud() { MouseFilter = MouseFilterEnum.Ignore; }
    public PortWindow Port { get; private set; } = null!;
    public ManifestWindow Manifest { get; private set; } = null!;
    public StationInfoWindow StationInfo { get; private set; } = null!;

    public override void _Ready()
    {
        Port = new PortWindow(Game);
        AddChild(Port);
        Manifest = new ManifestWindow(Game);
        AddChild(Manifest);
        StationInfo = new StationInfoWindow(Game);
        AddChild(StationInfo);
    }

    public override void _Process(double delta)
    {
        var manifest = Game.ManifestOpen && Game.Screen is Screen.Orbit or Screen.Plan or Screen.Flight && Game.V != null;
        if (manifest != Manifest.Visible)
        {
            if (manifest) Manifest.Rebuild();
            Manifest.Visible = manifest;
        }
        StationInfo.Visible = StationInfo.StationId >= 0 && Game.Screen is Screen.Orbit or Screen.Plan or Screen.Flight && Game.V != null;
        var open = Game.Screen == Screen.Port;
        if (open == Port.Visible) return;
        if (open) Port.Rebuild();
        Port.Visible = open;
    }

    void Text(Vector2 p, string s, Color c, int size = 16) =>
        DrawString(F, p, s, HorizontalAlignment.Left, -1, size, c);

    void Centered(float y, string s, Color c, int size)
    {
        var w = F.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;
        Text(new Vector2((Size.X - w) / 2, y), s, c, size);
    }

    public override void _Draw()
    {
        switch (Game.Screen)
        {
            case Screen.Title: DrawTitle(); return;
            case Screen.Loading: DrawLoading(); return;
            case Screen.Orbit:
                if (Game.OrbitSky) DrawCockpit(); else DrawOrbitMap();
                DrawHudLayers();
                break;
            case Screen.Plan or Screen.Flight:
                if (Game.MapOpen) DrawMap();
                else { DrawStreaks(); DrawCockpit(); }
                DrawHudLayers();
                break;
            case Screen.Port: DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.01f, 0.015f, 0.035f)); break;
            case Screen.Dead: DrawDead(); break;
        }
        DrawMenu();
    }

    void DrawHudLayers()
    {
        DrawInstruments();
        DrawStateFrame();
        DrawConsole();
        DrawTransition();
    }

    void DrawLoading()
    {
        Centered(340, "STARBEARER", Ink, 64);
        var dots = new string('.', 1 + (int)(Time.GetTicksMsec() / 400 % 3));
        Centered(420, Game.LoadingLabel + dots, Dim, 22);
    }

    void DrawTitle()
    {
        Centered(340, "STARBEARER", Ink, 64);
        Centered(400, "星間運送", Dim, 22);
        if (Game.HasSave) Centered(500, "Enter  続きから      N  新しい銀河", Ink, 22);
        else Centered(500, "Enter  新しい銀河", Ink, 22);
        if (Game.LoadFailed) Centered(550, "読み込みに失敗した", Warn, 16);
        if (Game.SaveRefused is { } why) Centered(580, why, Warn, 16);
    }

    void DrawDead()
    {
        var v = Game.V!; var s = Game.Save!;
        var why = v.Death switch
        {
            DeathCause.Collision => "恒星に突入した",
            DeathCause.HullFailure => "船体が熱で崩壊した",
            DeathCause.Stranded => "燃料が尽き、超空間に取り残された",
            DeathCause.LifeSupport => "生命維持が尽きた",
            _ => "",
        };
        if (Game.Bankrupt) why = "破産した。出港に足る燃料が買えない";
        Centered(300, why, Bad, 40);
        Centered(370, $"飛行 {v.Time:F1} 日   配達 {s.Deliveries} 件   再開 {s.Retries} 回", Ink, 20);
        if (!Game.Bankrupt) Centered(480, "R  最後の入港から再開", Ink, 22);
        Centered(520, "X  ここで終える（この銀河は失われる）", Warn, 22);
    }
}

using System.Linq;
using System;
using System.Threading.Tasks;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>Starting, loading, boarding and ending a voyage, and the checkpoint on disk.</summary>
public partial class Main
{
    Task? _loading;
    Action? _loaded;
    public string LoadingLabel { get; private set; } = "";
    public bool LoadFailed { get; private set; }
    int _attempt;

    /// <summary>The company can't pay for the fuel to leave. Unlike a death, this has no retry.</summary>
    public bool Bankrupt { get; private set; }

    public void StartNewGalaxy() => Begin(System.Random.Shared.NextInt64() is var n ? (ulong)n : 1);
    public void StartSeed(ulong seed) => Begin(seed);

    void Begin(ulong seed) => Load("銀河を生成しています", () => SaveData.NewGalaxy(seed), made =>
    {
        SaveRefused = null;
        Save = made.Save;
        WriteSave();
        ShowGalaxy(made.Galaxy);
        Board();
    });

    /// <summary>Runs slow work off the main thread, with the loading screen up until it is done.</summary>
    void Load<T>(string label, Func<T> work, Action<T> done)
    {
        LoadingLabel = label;
        LoadFailed = false;
        Screen = Screen.Loading;
        var task = Task.Run(work);
        _loading = task;
        _loaded = () =>
        {
            if (task.IsCompletedSuccessfully) { done(task.Result); return; }
            GD.PushError(task.Exception?.ToString() ?? "loading failed");
            LoadFailed = true;
            Screen = Screen.Title;
        };
    }

    void PollLoading()
    {
        if (_loading is not { IsCompleted: true }) return;
        var finish = _loaded!;
        _loading = null;
        _loaded = null;
        finish();
    }

    void ShowGalaxy(Galaxy g)
    {
        G = g;
        _sky?.QueueFree();
        _sky = new Sky();
        AddChild(_sky);
        _sky.Setup(g);
    }

    /// <summary>Why the save on disk can't be carried on from, or null if it can (or there is none).</summary>
    public string? SaveRefused { get; private set; }

    public void Continue()
    {
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        SaveData? save;
        try { save = SaveData.FromJson(f.GetAsText()); }
        catch (System.Text.Json.JsonException) { save = null; }
        // An older build's save describes another galaxy than its seed makes now: say so, rather than load it wrong.
        if (save is not { Current: true })
        {
            SaveRefused = save == null ? "セーブを読めない" : "古い版のセーブで、この版では続けられない（銀河の作りが変わった）。N で新しい銀河を始める";
            return;
        }
        SaveRefused = null;
        Save = save;
        Board();
    }

    /// <summary>Set up the docked state from the checkpoint. Also the retry entry point.</summary>
    void Board()
    {
        var s = Save!;
        Bankrupt = false;
        if (G == null || G.Seed != s.Seed) ShowGalaxy(Galaxy.Generate(s.Seed));
        s.MarketIn(G!);   // the stations built in the war come back with it, the port this ship lies at among them
        V = new Voyage(G!, s.PortStation, G!.StarOf(s.PortStation), s.Hold, (double[])s.ClosestApproach.Clone(),
            s.Seed ^ (ulong)(s.Deliveries * 31 + ++_attempt * 1009), s.TotalDays, s.Ship.State, s.Ship.Fitted);
        if (Supply.Bankrupt(s, G!)) { Bankrupt = true; Screen = Screen.Dead; return; }
        SteerForCourse();
        PlanDir = (V.ChartPositions[V.TargetStar] - V.EstimatedPosition).Normalized();
        PlanDv = 0.35;
        Forecast = null; SubForecast = null; SubCurrent = null; PlanFrozen = false; Selected = null;
        OrbitSky = false; Paused = true;
        Chart.Show(PrimaryOnChart, ChartView.ZoomFor(V.Primary));
        LookAt(PlanDir);
        Screen = Screen.Orbit;
        MapOpen = false;
        Helm.Reset(PlanDir); CameraFollow = false;
        var neighbours = G!.StationsIn(G.StarOf(s.PortStation)).Count() - 1;
        Message = s.Hold.Count == 0 && s.Deliveries == 0 && s.TotalDays == 0 && neighbours > 0
            ? $"この星系には港がほかに {neighbours} つある。Enter で港を開き、最初の仕事（同じ星系の港へ）を請ける。FTL なしで、軌道を合わせて届けられる"
            : s.Hold.Count == 0 ? "空荷で停泊中。Enter で港を開いてミッションを請ける" : "港に停泊中。星図と計画で発進の時機を選び、U で出港する";
        _forecastDirty = true;
    }

    void WriteSave()
    {
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        f.StoreString(Save!.ToJson());
    }

    public void Retry()
    {
        Save!.Retries++;
        WriteSave();
        Board();
    }

    public void EndVoyageForGood()
    {
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
        Save = null; V = null; G = null;
        _sky?.QueueFree(); _sky = null;
        Screen = Screen.Title;
    }
}

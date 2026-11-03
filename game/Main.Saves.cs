using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>A save on disk as the title lists it: Data is null if the file can't be read.</summary>
public sealed record SaveEntry(string Path, SaveData? Data, DateTime When)
{
    public bool Playable => Data is { Current: true };
}

/// <summary>The saves on disk: one file per company, listed on the title, newest first.</summary>
public partial class Main
{
    // The scripted smoke run must never touch the player's saves.
    static bool Scripted => OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--autotest="));
    static string SaveDir => Scripted ? "user://autotest_saves" : "user://saves";
    /// <summary>Where the one save of earlier builds lay; it is moved into the list.</summary>
    const string LegacySave = "user://save.json";

    /// <summary>The file the company being played checkpoints to.</summary>
    string? _slot;
    public List<SaveEntry> Saves { get; private set; } = new();
    public bool HasSave => Saves.Count > 0;
    /// <summary>The row picked on the title: a save, or Saves.Count for a new galaxy.</summary>
    public int TitleChoice { get; private set; }
    /// <summary>Set by a first Delete on a save's row; a second deletes it.</summary>
    public bool DeleteArmed { get; private set; }

    public void RefreshSaves()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SaveDir));
        if (!Scripted && FileAccess.FileExists(LegacySave))
            DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(LegacySave), ProjectSettings.GlobalizePath(NewSlot()));
        Saves = DirAccess.GetFilesAt(SaveDir).Where(f => f.EndsWith(".json")).Select(f => Read($"{SaveDir}/{f}"))
            .OrderByDescending(e => e.When).ThenByDescending(e => e.Path, StringComparer.Ordinal).ToList();
        TitleChoice = Math.Clamp(TitleChoice, 0, Saves.Count);
        DeleteArmed = false;
    }

    static SaveEntry Read(string path)
    {
        SaveData? data = null;
        using (var f = FileAccess.Open(path, FileAccess.ModeFlags.Read))
            if (f != null)
                try { data = SaveData.FromJson(f.GetAsText()); }
                catch (System.Text.Json.JsonException) { }
        // A save from before the time was kept goes by when its file was written.
        var when = data is { SavedAt: var at } && at != default ? at : DateTimeOffset.FromUnixTimeSeconds((long)FileAccess.GetModifiedTime(path)).UtcDateTime;
        return new SaveEntry(path, data, when);
    }

    static string NewSlot()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var path = $"{SaveDir}/{stamp}.json";
        for (int i = 2; FileAccess.FileExists(path); i++) path = $"{SaveDir}/{stamp}-{i}.json";
        return path;
    }

    void WriteSave()
    {
        var s = Save!;
        s.SavedAt = DateTime.UtcNow;
        if (G != null && G.IsStation(s.PortStation)) s.Where = Missions.Label(G, s.PortStation);
        _slot ??= NewSlot();
        using var f = FileAccess.Open(_slot, FileAccess.ModeFlags.Write);
        f.StoreString(s.ToJson());
    }

    void PickTitle(int row)
    {
        row = Math.Clamp(row, 0, Saves.Count);
        if (row != TitleChoice) DeleteArmed = false;
        TitleChoice = row;
    }

    void OpenTitleChoice()
    {
        if (TitleChoice >= Saves.Count) StartNewGalaxy();
        else Continue(Saves[TitleChoice]);
    }

    void DeleteTitleChoice()
    {
        if (TitleChoice >= Saves.Count) return;
        if (!DeleteArmed) { DeleteArmed = true; return; }
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Saves[TitleChoice].Path));
        SaveRefused = null;
        RefreshSaves();
    }

    void TitleInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && _hud.TitleRowAt(mm.Position) is int hover) PickTitle(hover);
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) PickTitle(TitleChoice - 1);
            if (mb.ButtonIndex == MouseButton.WheelDown) PickTitle(TitleChoice + 1);
            if (mb.ButtonIndex == MouseButton.Left && _hud.TitleDeleteAt(mb.Position) is int bin) { PickTitle(bin); DeleteTitleChoice(); }
            else if (mb.ButtonIndex == MouseButton.Left && _hud.TitleRowAt(mb.Position) is int row) { PickTitle(row); OpenTitleChoice(); }
        }
        if (e is not InputEventKey { Pressed: true } k) return;
        switch (k.Keycode)
        {
            case Key.Up: PickTitle(TitleChoice - 1); break;
            case Key.Down: PickTitle(TitleChoice + 1); break;
            case Key.Enter or Key.KpEnter when !k.Echo: OpenTitleChoice(); break;
            case Key.N when !k.Echo: StartNewGalaxy(); break;
            case Key.Delete or Key.Backspace when !k.Echo: DeleteTitleChoice(); break;
            case Key.Escape: DeleteArmed = false; break;
        }
    }
}

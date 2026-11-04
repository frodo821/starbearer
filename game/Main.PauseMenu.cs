using System;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>
/// The pause menu, on Esc: carry on, go back to the last port call, go to the title to change company or
/// start a galaxy, or quit. The clock stops while it is open. What would throw away the voyage since
/// the last port call asks twice.
/// </summary>
public partial class Main
{
    public enum PauseItem { Resume, Restart, Title, Quit }
    public static readonly PauseItem[] PauseItems = (PauseItem[])Enum.GetValues(typeof(PauseItem));

    public bool PauseOpen { get; private set; }
    public int PauseChoice { get; private set; }
    /// <summary>The item a first press armed; a second press on it carries it out.</summary>
    public PauseItem? PauseArmed { get; private set; }

    /// <summary>Days of the voyage since the last port call that leaving now throws away (0 at the port, where every change is saved).</summary>
    public double PauseLoss => Screen is Screen.Port || V == null ? 0 : V.Time;

    public bool PauseEnabled(PauseItem item) => item != PauseItem.Restart || (Save != null && CanRetry);

    void OpenPause()
    {
        if (Save == null) return;
        PauseOpen = true;
        PauseChoice = 0;
        PauseArmed = null;
        _hud.CloseMenu();   // the clock stops, so a lit engine just waits
    }

    void ClosePause() { PauseOpen = false; PauseArmed = null; }

    void PickPause(int row)
    {
        row = Math.Clamp(row, 0, PauseItems.Length - 1);
        if (row != PauseChoice) PauseArmed = null;
        PauseChoice = row;
    }

    void ChoosePause()
    {
        var item = PauseItems[PauseChoice];
        if (!PauseEnabled(item)) return;
        var asks = item is PauseItem.Restart or PauseItem.Title or PauseItem.Quit && PauseLoss > 0 && Screen != Screen.Dead;
        if (asks && PauseArmed != item) { PauseArmed = item; return; }
        ClosePause();
        switch (item)
        {
            case PauseItem.Restart: PutAwayWindows(); Retry(); Message = "最後の入港からやり直した"; break;
            case PauseItem.Title: ToTitle(); break;
            case PauseItem.Quit: GetTree().Quit(); break;
        }
    }

    /// <summary>The windows over the ship, closed before the ship they show is replaced.</summary>
    void PutAwayWindows()
    {
        if (ManifestOpen) ToggleManifest();
        if (StationInfoOpen) CloseStationInfo();
        VisitingPort = false;
        MapOpen = false;
    }

    /// <summary>Back to the title with the company left as its last port call saved it, to carry on with it or another.</summary>
    void ToTitle()
    {
        PutAwayWindows();
        Helm.CutEngine();
        var slot = _slot;
        _slot = null;
        Save = null; V = null;
        Bankrupt = false;
        Message = "";
        RefreshSaves();
        TitleChoice = Math.Max(0, Saves.FindIndex(e => e.Path == slot));
        Screen = Screen.Title;
    }

    /// <summary>Set by a press the menu took, so its release doesn't reach what is under the menu once it closes.</summary>
    bool _pausePress;

    /// <summary>The release of a press the menu took. Returns true if it swallowed the event.</summary>
    bool PauseRelease(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: false } || !_pausePress) return false;
        _pausePress = false;
        return true;
    }

    void PauseInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: false }) _consolePress = false;
        if (e is InputEventMouseMotion mm && _hud.PauseRowAt(mm.Position) is int hover) PickPause(hover);
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb && _hud.PauseRowAt(mb.Position) is int row)
        {
            _pausePress = true;
            PickPause(row);
            ChoosePause();
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.Escape: ClosePause(); break;
            case Key.Up: PickPause(PauseChoice - 1); break;
            case Key.Down: PickPause(PauseChoice + 1); break;
            case Key.Enter or Key.KpEnter: ChoosePause(); break;
        }
    }
}

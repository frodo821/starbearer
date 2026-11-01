using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

public partial class Main
{
    void OrbitInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left && mb.Pressed && _hud.CompassTap(mb.Position)) return;
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _hud.ZoomOrbit(1.1f);
            if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _hud.ZoomOrbit(1 / 1.1f);
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _looking = mb.Pressed;
                if (mb.Pressed) { _pressAt = mb.Position; _dragged = false; _hud.OrbitPress(mb.Position); }
                else { if (!_dragged && !_hud.OrbitHolding) _hud.OrbitTap(mb.Position); _hud.OrbitRelease(); }
            }
        }
        if (e is InputEventMouseMotion mm && _looking)
        {
            if (mm.Position.DistanceTo(_pressAt) > 5) _dragged = true;
            if (_hud.OrbitHolding) _hud.OrbitHoldMove(mm);
            else _hud.RotateOrbit(mm.Relative);
        }
        if (e is InputEventPanGesture pan) _hud.PanOrbit(pan.Delta * 6f);
        if (e is InputEventMagnifyGesture pinch) _hud.ZoomOrbit(pinch.Factor);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Screen == Screen.Loading) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            if (click.Pressed && _hud.ConsoleTap(click.Position)) { _consolePress = true; return; }
            HeldButton = null;
            if (!click.Pressed && _consolePress) { _consolePress = false; return; }
        }
        if (Screen == Screen.Orbit && !OrbitSky) OrbitInput(e);
        else if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left && mb.Pressed && Screen is Screen.Flight or Screen.Orbit && _hud.CompassTap(mb.Position)) return;
            if (MapOpen) { _hud.MapMouse(mb); return; }
            if (mb.ButtonIndex == MouseButton.Right) _looking = mb.Pressed;
            if (mb.ButtonIndex == MouseButton.Left)
            {
                // Press-and-drag looks around; a press without movement is a tap that sets the aim.
                _looking = mb.Pressed;
                if (mb.Pressed) { _pressAt = mb.Position; _dragged = false; }
                else if (!_dragged && Screen == Screen.Plan) SetAimScreen(mb.Position);
            }
            if (Screen == Screen.Plan && mb.Pressed && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var step = Input.IsKeyPressed(Key.Shift) ? 0.002 : 0.02;
                SetPlanDv(PlanDv + (mb.ButtonIndex == MouseButton.WheelUp ? step : -step));
            }
        }
        if (e is InputEventMouseMotion mm)
        {
            if (MapOpen) { _hud.MapDrag(mm); return; }
            if (_looking)
            {
                if (mm.Position.DistanceTo(_pressAt) > 5) _dragged = true;
                Look(mm.Relative * 0.003f);
            }
        }
        // macOS trackpads report two-finger scrolling and pinching as gestures, not as a wheel.
        if (e is InputEventPanGesture pan && V != null && Screen != Screen.Orbit)
        {
            if (MapOpen) _hud.PanMap(pan.Delta * 6f);
            else Look(pan.Delta * 0.03f);
        }
        if (e is InputEventMagnifyGesture pinch && MapOpen) _hud.ZoomMap(pinch.Factor);
        if (Screen == Screen.Orbit && OrbitSky && e is InputEventPanGesture skyPan) Look(skyPan.Delta * 0.03f);
        if (e is not InputEventKey { Pressed: true, Echo: false } k)
        {
            if (e is InputEventKey { Pressed: true, Echo: true } ek && Screen == Screen.Plan) Nudge(ek.Keycode, ek.ShiftPressed);
            return;
        }
        if (MapOpen && k.Keycode == Key.R) MapCenter = V!.EstimatedPosition;
        if (MapOpen && k.Keycode == Key.T) MapCenter = V!.ChartPositions[V.TargetStar];
        switch (Screen)
        {
            case Screen.Title:
                if (k.Keycode == Key.Enter) { if (HasSave) Continue(); else StartNewGalaxy(); }
                if (k.Keycode == Key.N) StartNewGalaxy();
                break;
            case Screen.Orbit:
                if (V!.Docked && ShipControl(k.Keycode)) { Message = DockedNotice; break; }
                if (k.Keycode == Key.U) ToggleDock();
                if (k.Keycode == Key.Enter) OpenPort();
                if (k.Keycode == Key.J) BeginJump();
                if (k.Keycode == Key.Tab) OrbitSky = !OrbitSky;
                if (k.Keycode == Key.N) WarpToNode();
                if (k.Keycode == Key.P) { Paused = !Paused; ToNode = false; Aligning = false; }
                if (k.Keycode == Key.Y) { Replan(); Message = "現在の軌道から計画を作り直した"; }
                ThrottleKeys(k.Keycode);
                if (k.Keycode == Key.V) CycleHold();
                if (k.Keycode == Key.B) ToggleCameraFollow();
                if (k.Keycode == Key.H) SuggestTransfer();
                if (k.Keycode == Key.Bracketright) CycleNode(1);
                if (k.Keycode == Key.Bracketleft) CycleNode(-1);
                if (k.Keycode == Key.M) AddMatchBurn();
                if (k.Keycode is Key.Backspace or Key.Delete) { if (k.ShiftPressed) ClearNodes(); else DeleteNode(); }
                if (k.Keycode == Key.T) RunTransferComputer();
                if (k.Keycode == Key.G) ToggleAutoBurn();
                if (k.Keycode == Key.R) GlideChart(V!.System.Position, SystemZoom);
                if (k.Keycode >= Key.Key1 && k.Keycode < Key.Key1 + Rates.Length) { SubRateIndex = (int)(k.Keycode - Key.Key1); Paused = false; ToNode = false; Aligning = false; }
                break;
            case Screen.Plan:
                if (k.Keycode == Key.Enter) ExecutePlan();
                if (k.Keycode == Key.Escape) CancelPlan();
                if (k.Keycode == Key.Tab) ToggleMap();
                Nudge(k.Keycode, k.ShiftPressed);
                break;
            case Screen.Flight:
                if (k.Keycode == Key.C) BeginCorrection();
                if (k.Keycode == Key.D) TryDescend();
                ThrottleKeys(k.Keycode);
                if (k.Keycode == Key.V) CycleHold();
                if (k.Keycode == Key.B) ToggleCameraFollow();
                if (k.Keycode == Key.F) { V!.TakeSight(); Message = $"天測完了。位置誤差 ±{V.NavSigma:F3} ly"; _forecastDirty = true; }
                if (k.Keycode == Key.Tab) ToggleMap();
                if (k.Keycode >= Key.Key1 && k.Keycode < Key.Key1 + Rates.Length) RateIndex = (int)(k.Keycode - Key.Key1);
                break;
            case Screen.Port:
                if (k.Keycode == Key.Enter) LeavePort();
                for (int i = 0; i < Modules.Shop.Length; i++)
                    if (k.Keycode == Key.Key1 + i) Buy(Modules.Shop[i].Id, Modules.Shop[i].Price, Modules.Shop[i].Name);
                if (k.Keycode == Key.Key3) Purchase(Supply.BuyFuel);
                if (k.Keycode == Key.Key4) Purchase(Supply.BuyLife);
                if (k.Keycode == Key.Key5) Purchase(Supply.RepairHull);
                break;
            case Screen.Dead:
                if (k.Keycode == Key.R && !Bankrupt) Retry();
                if (k.Keycode == Key.X) EndVoyageForGood();
                break;
        }
    }

    void Look(Vector2 d)
    {
        CameraFollow = false;
        _yaw -= d.X;
        _pitch = Mathf.Clamp(_pitch - d.Y, -1.5f, 1.5f);
    }

    void Nudge(Key key, bool fine)
    {
        var deg = Mathf.DegToRad(fine ? 0.02 : 0.2);
        var up = new Vec3d(0, 1, 0);
        var right = PlanDir.Cross(up).Normalized();
        var pitch = right.Cross(PlanDir).Normalized();
        Vec3d? d = key switch
        {
            Key.Left => PlanDir - right * deg,
            Key.Right => PlanDir + right * deg,
            Key.Up => PlanDir + pitch * deg,
            Key.Down => PlanDir - pitch * deg,
            _ => null,
        };
        if (d is { } dir) SetPlanDir(dir);
    }
}

using System;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>
/// Rental shuttles, and changing ship in orbit. A shuttle is hired at a port, to ride aboard the ship
/// flown or to be boarded there; in orbit the pilot leaves the ship on its orbit and flies off in the
/// shuttle, and comes back alongside to board it again. An overdue shuttle is taken back at the next
/// port it calls at; with no ship of the company there, that is the end (see Dock).
/// </summary>
public partial class Main
{
    /// <summary>The galaxy day now: the voyage's clock, or the checkpoint's between voyages.</summary>
    public double GalaxyDay => V?.AbsoluteTime ?? Save!.TotalDays;

    /// <summary>The shuttle riding aboard the ship flown, if any.</summary>
    public OwnedShip? CarriedShuttle => Save is { } s ? Rentals.CarriedBy(s, s.Flown) : null;

    public void RentShuttle(bool aboard)
    {
        var s = Save!;
        if (Rentals.Refusal(s, aboard) is { } why) { Refuse(why); return; }
        var shuttle = Rentals.Hire(s, GalaxyDay, aboard)!;
        if (aboard)
        {
            V!.CarriedMass = Rentals.CarriedMass(s, s.Flown);
            Message = $"レンタルシャトルを借りて {s.Ship.Type.Name} に載せた（保証金 {Tuning.ShuttleDeposit:F0}）";
        }
        else
        {
            BoardShip(shuttle.Id);
            Message = $"レンタルシャトルを借りて乗り換えた（保証金 {Tuning.ShuttleDeposit:F0}）";
        }
        PortChanged();
    }

    /// <summary>Gives a shuttle back here: one riding aboard, or one waiting at this port.</summary>
    public void ReturnShuttle(OwnedShip shuttle)
    {
        var s = Save!;
        if (shuttle.Rental == null || shuttle.Id == s.Flown) return;
        var refund = Rentals.Return(s, shuttle, GalaxyDay);
        if (V != null) V.CarriedMass = Rentals.CarriedMass(s, s.Flown);
        Message = $"シャトルを返した。保証金から {refund:F0} が戻った";
        PortChanged();
    }

    public void PayRentDebt()
    {
        var paid = Rentals.PayDebt(Save!);
        Message = Save!.RentDebt > 0 ? $"延滞金を {paid:F0} 払った。残り {Save.RentDebt:F0}" : $"延滞金 {paid:F0} を払い終えた";
        PortChanged();
    }

    /// <summary>Why the pilot can't leave the ship flown in orbit for its shuttle now, or null if they can.</summary>
    public string? LaunchRefusal()
    {
        var v = V!;
        if (CarriedShuttle == null) return "シャトルを載せていない";
        if (v.Docked) return "出港してから";
        if (v.Phase != Phase.Sublight || v.Adrift) return "星系の中の軌道でないと船を置いていけない";
        return Parking.Refusal(v.Primary, v.SubPosition, v.SubVelocity);
    }

    /// <summary>The pilot leaves the ship flown on its orbit and flies on in the shuttle it carried.</summary>
    public void LaunchShuttle()
    {
        if (Screen != Screen.Orbit) return;
        if (LaunchRefusal() is { } why) { Message = why; return; }
        var s = Save!; var v = V!;
        CutEngine("");
        var ship = s.Ship;
        var shuttle = CarriedShuttle!;
        ship.Fuel = v.Fuel; ship.ReactionMass = v.ReactionMass; ship.Hull = v.Hull; ship.Life = v.LifeRemaining;
        ship.Hold = v.Hold.Select(c => c.Copy()).ToList();
        ship.Parked = ParkedState.Of(v.Primary, v.SubPosition, v.SubVelocity, v.AbsoluteTime);
        ship.Station = -1;
        shuttle.Carrier = 0;
        s.Flown = shuttle.Id;
        v.Reship(shuttle.Fitted, shuttle.State, shuttle.Hold, 0);
        Selected = null;
        Replan();
        ShowCue(new StateCue("シャトルで発つ", $"{ship.Type.Name} を {v.Primary.Name} の軌道に残した", Palette.Good, Major: false, Outward: true));
        Message = $"{ship.Type.Name} は軌道に残った。目標にして横に付け、U で乗り換えられる";
    }

    /// <summary>The ship of the fleet left on the orbit picked as the target, if that is what it is.</summary>
    public OwnedShip? GoalShip => V?.Goal is ParkedOrbit po ? Save!.Fleet.FirstOrDefault(x => x.Id == po.ShipId && x.Parked != null) : null;

    /// <summary>Close and slow enough beside the ship left in orbit to board it.</summary>
    public bool AlongsideGoalShip => V is { } v && v.Goal is ParkedOrbit po && GoalShip != null
        && v.RelativeNow(po) is var (p, vel) && (p - v.SubPosition).Length < Tuning.DockRange && (vel - v.SubVelocity).Length < Tuning.DockRelativeSpeed;

    /// <summary>Why the ship left in orbit can't be boarded from here, or null if it can.</summary>
    public string? BoardRefusal()
    {
        var s = Save!;
        if (GoalShip is not { } ship) return "目標が軌道に残した自船でない";
        if (s.Ship.Rental == null) return "乗り換えて離れられるのはシャトルから";
        if (Rentals.CarriedBy(s, ship.Id) != null) return $"{ship.Type.Name} はもうシャトルを載せている";
        if (!AlongsideGoalShip) return $"{Instruments.Distance(Tuning.DockRange)}、相対 {Instruments.Speed(Tuning.DockRelativeSpeed)} 以内まで寄せる（X 接近、Z 相対0）";
        return null;
    }

    /// <summary>Boards the ship left in orbit from the shuttle alongside, which goes aboard it.</summary>
    public void BoardGoalShip()
    {
        if (BoardRefusal() is { } why) { Message = why; return; }
        var s = Save!; var v = V!;
        CutEngine("");
        var ship = GoalShip!;
        var orbit = (ParkedOrbit)v.Goal!;
        var shuttle = s.Ship;
        shuttle.Fuel = v.Fuel; shuttle.ReactionMass = v.ReactionMass; shuttle.Hull = v.Hull; shuttle.Life = v.LifeRemaining;
        shuttle.Carrier = ship.Id;
        shuttle.Station = -1;
        var (p, vel) = orbit.StateAt(v.AbsoluteTime);
        ship.Parked = null;
        s.Flown = ship.Id;
        v.MatchState(orbit.Host, p, vel);
        v.Reship(ship.Fitted, ship.State, ship.Hold, Rentals.CarriedMass(s, ship.Id));
        v.SetGoal(v.LocalStation);
        Selected = null;
        Replan();
        ShowCue(new StateCue("乗り換え", $"{ship.Type.Name} に戻った。シャトルは艦内に", Palette.Good, Major: false, Outward: false));
        Message = $"{ship.Type.Name} に乗り換えた";
    }

    /// <summary>At a port call: overdue shuttles are taken back (see Rentals.TakeBack). Returns the lines for the port's report.</summary>
    System.Collections.Generic.List<string> TakeBackOverdue(int station) => Rentals.TakeBack(Save!, station, GalaxyDay);
}

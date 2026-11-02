using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starbearer.Sim;

namespace Starbearer;

/// <summary>The shipyard: modules, pressurizers and ships, bought, boarded and sold where the ship is docked.</summary>
public partial class Main
{
    /// <summary>The port the ship is at builds modules: only a shipyard does.</summary>
    public bool AtShipyard => V?.LocalStation is { } st && G!.KindOf(st.Id) == StationKind.Shipyard;

    /// <summary>The nearest shipyard in the region, for pointing the way to one; -1 if none.</summary>
    public int NearestShipyard()
    {
        var here = G!.Stars[V!.SystemStar];
        return G.Ports.Where(p => G.KindOf(p) == StationKind.Shipyard && G.StarAt(p).Region == here.Region)
            .OrderBy(p => (G.StarAt(p).Position - here.Position).Length).DefaultIfEmpty(-1).First();
    }

    public void Buy(string id, double price, string name)
    {
        var s = Save!;
        if (s.Modules.Contains(id)) return;
        if (!AtShipyard) { Refuse("モジュールは造船所で買える"); return; }
        if (s.Credits < price) { Refuse($"{name}: 資金が足りない"); return; }
        s.Credits -= price;
        s.Modules.Add(id);
        Message = $"{name} を搭載";
        PortChanged();
    }

    /// <summary>Fits a pressurizer to the next fluid bay without one.</summary>
    public void BuyPressurizer()
    {
        if (!AtShipyard) { Refuse("モジュールは造船所で買える"); return; }
        if (!Modules.FitPressurizer(Save!)) { Refuse($"{Modules.PressurizerName}: 付けられる液体ベイがないか、資金が足りない"); return; }
        Message = $"{Modules.PressurizerName} を液体ベイに搭載";
        PortChanged();
    }

    public void BuyShip(ShipClass c)
    {
        if (!Hangar.Buy(Save!, G!, c)) { Refuse($"{c.Name}: " + (Save!.Credits < c.Price ? "資金が足りない" : "ここでは造っていない")); return; }
        Message = $"{c.Name} を購入した。この港に置いてある";
        PortChanged();
    }

    /// <summary>Changes ships here. The one left keeps its bays' cargo, fuel and modules; the voyage starts over in the new one.</summary>
    public void BoardShip(int id)
    {
        var s = Save!;
        if (id == s.Flown || !Hangar.Board(s, id)) return;
        VisitingPort = false;   // leaving the port boards the new ship from the checkpoint
        SteerForCourse();
        Message = $"{s.Ship.Type.Name} に乗り換えた";
        PortChanged();
    }

    public void SellShip(int id)
    {
        var s = Save!;
        var ship = s.Fleet.FirstOrDefault(x => x.Id == id);
        if (ship == null) return;
        var value = Ships.SaleValue(ship);
        if (!Hangar.Sell(s, G!, id)) { Refuse(Hangar.SaleRefusal(s, G!, ship) ?? "売れない"); return; }
        Message = $"{ship.Type.Name} を {value:F0} で売った";
        PortChanged();
    }
}

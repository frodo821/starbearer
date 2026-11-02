namespace Starbearer.Sim;

/// <summary>
/// What can be fitted to a ship at a shipyard: quality-of-life automation, none of it needed to finish a
/// mission, and pressurizers for fluid bays. Modules stay with the ship they were fitted to.
/// </summary>
public static class Modules
{
    public const string AutoBurn = "autoburn";
    public const string Transfer = "transfer";
    public static readonly (string Id, string Name, double Price)[] Shop =
    {
        (AutoBurn, "自動噴射装置", 150),
        (Transfer, "転送計算機", 250),
    };

    /// <summary>Fitted to one fluid bay, it lets that bay carry pressurized fluid. Sold per bay, so it is not in the shop list.</summary>
    public const string PressurizerName = "加圧モジュール";
    public const double PressurizerPrice = 200;

    /// <summary>What the modules aboard are worth, pressurizers included.</summary>
    public static double Value(OwnedShip s) =>
        s.Modules.Sum(id => Shop.FirstOrDefault(m => m.Id == id).Price) + s.Pressurized.Count * PressurizerPrice;

    /// <summary>Fits a pressurizer to the first fluid bay of the ship flown without one. False if there is none, or not the money.</summary>
    public static bool FitPressurizer(SaveData s)
    {
        var bay = CargoBays.Of(s).Unpressurized.Cast<int?>().FirstOrDefault();
        if (bay is not int i || s.Credits < PressurizerPrice) return false;
        s.Credits -= PressurizerPrice;
        s.Pressurized.Add(i);
        return true;
    }
}

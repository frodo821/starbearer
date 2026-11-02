namespace Starbearer.Sim;

/// <summary>
/// A traded good. Amounts are in hold units (20 m³ each); mass and price are per unit.
/// </summary>
/// <param name="Price">Base price: what a unit fetches where stock sits at its target.</param>
/// <param name="Mass">Per unit: ores and metals are heavy, gases light.</param>
/// <param name="HeatTolerance">How much heat it stands before it starts to spoil.</param>
public sealed record Ware(string Id, string Name, CargoKind Kind, double Price, double Mass, double HeatTolerance)
{
    /// <summary>A lot of it as a load in the hold.</summary>
    public Cargo Load(double units, double reward = 0) => new(Name, Mass * units, HeatTolerance, reward, units, Kind) { Ware = Id };
}

public static class Wares
{
    const CargoKind F = CargoKind.Fluid, P = CargoKind.PressurizedFluid, B = CargoKind.Bulk, C = CargoKind.Container;

    public static readonly Ware[] All =
    {
        // Raw: mined, harvested, scooped.
        new("water", "水", F, 8, 0.12, 0.85),
        new("organics", "有機化合物", F, 14, 0.12, 0.6),
        new("hydrogen", "水素", P, 6, 0.06, 0.8),
        new("deuterium", "重水素", P, 30, 0.06, 0.8),
        new("methane", "メタン", P, 10, 0.08, 0.7),
        new("ammonia", "アンモニア", P, 12, 0.08, 0.7),
        new("iron_ore", "鉄鉱", B, 10, 0.25, 0.95),
        new("titanium_ore", "チタン鉱", B, 18, 0.25, 0.95),
        new("silica", "珪砂", B, 8, 0.22, 0.95),
        new("uranium_ore", "ウラン鉱", B, 40, 0.3, 0.95),
        // Refined.
        new("oxygen", "酸素", P, 16, 0.08, 0.8),
        new("hydrazine", "ヒドラジン", F, 40, 0.12, 0.5),
        new("steel", "鋼鉄", B, 35, 0.3, 0.95),
        new("titanium", "チタン合金", B, 70, 0.25, 0.95),
        new("silicon_carbide", "炭化ケイ素", B, 55, 0.2, 0.95),
        new("explosives", "爆薬", C, 60, 0.12, 0.35),
        // Goods.
        new("fusion_fuel", "核融合燃料", C, 15, 0.5, 0.7),   // what ships burn: dense, and counted in the tank by mass
        new("fission_fuel", "核分裂燃料", C, 120, 0.3, 0.8),
        new("food", "食品", C, 30, 0.1, 0.55),
        new("medical", "医療品", C, 110, 0.06, 0.45),
        new("electronics", "先端電子機器", C, 160, 0.08, 0.5),
        new("missiles", "ミサイル", C, 220, 0.15, 0.45),
        new("shells", "砲弾", C, 70, 0.25, 0.5),
        new("slugs", "質量弾", B, 45, 0.3, 0.95),
        new("laser_optics", "レーザー集光素子", C, 180, 0.05, 0.5),
    };

    static readonly Dictionary<string, Ware> ById = All.ToDictionary(w => w.Id);
    public static Ware Get(string id) => ById[id];
    public static int IndexOf(string id) => Array.FindIndex(All, w => w.Id == id);

    /// <summary>What ships burn, in FTL and in normal space alike.</summary>
    public const string ShipFuel = "fusion_fuel";
}

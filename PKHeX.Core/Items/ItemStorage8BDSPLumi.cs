using System;

namespace PKHeX.Core;

/// <summary>Inventory rules for Pokémon Luminescent Platinum's BDSP-based bag.</summary>
public sealed class ItemStorage8BDSPLumi : IItemStorage
{
    public static readonly ItemStorage8BDSPLumi Instance = new();

    private static readonly ushort[] GeneralExtras = [1825, 1834, 1836];
    private static readonly ushort[] KeyExtras = [1823, 1824, 1826, 1827, 1828, 1829, 1830, 1831, 1832, 1833, 1835];
    private static readonly ushort[] General = Join(ItemStorage8BDSP.GetLegal(InventoryType.Items), GeneralExtras);
    private static readonly ushort[] Key = Join(ItemStorage8BDSP.GetLegal(InventoryType.KeyItems), KeyExtras);

    public ReadOnlySpan<ushort> GetItems(InventoryType type) => type switch
    {
        InventoryType.Items => General,
        InventoryType.KeyItems => Key,
        _ => ItemStorage8BDSP.GetLegal(type),
    };

    public int GetMax(InventoryType type) => ItemStorage8BDSP.Instance.GetMax(type);
    public bool IsLegal(InventoryType type, int itemIndex, int itemCount) =>
        type is InventoryType.KeyItems || ItemStorage8BDSP.Instance.IsLegal(type, itemIndex, itemCount);

    private static ushort[] Join(ReadOnlySpan<ushort> first, ReadOnlySpan<ushort> second)
    {
        var result = new ushort[first.Length + second.Length];
        first.CopyTo(result);
        second.CopyTo(result.AsSpan(first.Length));
        return result;
    }
}

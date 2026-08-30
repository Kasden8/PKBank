using System;
using System.Collections.Generic;
using static PKHeX.Core.InventoryType;

namespace PKHeX.Core;

public class PlayerBag8b : PlayerBag
{
    public override IReadOnlyList<InventoryPouch8b> Pouches { get; }
    public override IItemStorage Info { get; }

    private static InventoryPouch8b[] GetPouches(IItemStorage info) =>
    [
        MakePouch(Items, info), MakePouch(KeyItems, info), MakePouch(TMHMs, info), MakePouch(Medicine, info),
        MakePouch(Berries, info), MakePouch(Balls, info), MakePouch(BattleItems, info), MakePouch(Treasure, info),
    ];

    public PlayerBag8b(SAV8BS sav) : this(sav.Items.Data, sav is SAV8BSLuminescent ? ItemStorage8BDSPLumi.Instance : ItemStorage8BDSP.Instance) { }
    public PlayerBag8b(MyItem8b block) : this(block.Data, ItemStorage8BDSP.Instance) { }
    public PlayerBag8b(ReadOnlySpan<byte> data) : this(data, ItemStorage8BDSP.Instance) { }
    private PlayerBag8b(ReadOnlySpan<byte> data, IItemStorage info)
    {
        Info = info;
        Pouches = GetPouches(info);
        Pouches.LoadAll(data);
    }

    public override void CopyTo(SaveFile sav) => CopyTo((SAV8BS)sav);

    public void CopyTo(SAV8BS sav)
    {
        CopyTo(sav.Items.Data);
        if (sav is not SAV8BSLuminescent)
            sav.Items.CleanIllegalSlots();
    }
    public void CopyTo(MyItem8b items)
    {
        CopyTo(items.Data);
        items.CleanIllegalSlots();
    }

    public void CopyTo(Span<byte> data) => Pouches.SaveAll(data);

    private static InventoryPouch8b MakePouch(InventoryType type, IItemStorage info)
    {
        var max = ItemStorage8BDSP.Instance.GetMax(type);
        return new InventoryPouch8b(type, info, max);
    }
}

using System;
using System.Buffers.Binary;

namespace PKHeX.Core;

/// <summary>Save data for Pokémon Luminescent Platinum.</summary>
public sealed class SAV8BSLuminescent : SAV8BS
{
    public SAV8BSLuminescent() : this(new byte[SaveUtil.SIZE_G8BDSP_3], false)
        => SaveRevision = unchecked((int)0xFFFF0134);

    public SAV8BSLuminescent(Memory<byte> data, bool exportable = true) : base(data, exportable)
        => LumiZukan = new(this, data.Slice(0x7A328, 0x30B8));

    public Zukan8bLumi LumiZukan { get; } = null!;

    public override PB8LUMI BlankPKM => new();
    public override Type PKMType => typeof(PB8LUMI);
    public override EntityContext Context => EntityContext.Gen8bLumi;
    public override PersonalTable8BDSP Personal => PersonalTable.BDSPLUMI;
    public override ushort MaxSpeciesID => Legal.MaxSpeciesID_9;
    public override int MaxItemID => 1836;

    public bool IsLuminescentRevision => (BinaryPrimitives.ReadUInt32LittleEndian(Data) & 0xFFFF0000) == 0xFFFF0000;

    protected override SAV8BSLuminescent CloneInternal() => new(Data.ToArray());
    protected override PB8LUMI GetPKM(Memory<byte> data) => new(data);

    // Luminescent's stored hash is not retail BDSP's MD5. Recomputing the retail
    // hash changes an otherwise untouched save, so leave the mod-owned checksum
    // bytes intact until its algorithm has been independently verified.
    protected override void SetChecksums() { }

    protected override void SetDex(PKM pk) => LumiZukan.SetDex(pk);
    public override bool GetSeen(ushort species) => LumiZukan.GetSeen(species);
    public override bool GetCaught(ushort species) => LumiZukan.GetCaught(species);
}

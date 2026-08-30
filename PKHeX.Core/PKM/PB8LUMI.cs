using System;

namespace PKHeX.Core;

/// <summary>Generation 8 Pokémon data used by Pokémon Luminescent Platinum.</summary>
public sealed class PB8LUMI : PB8
{
    public override PersonalInfo8BDSP PersonalInfo => PersonalTable.BDSPLUMI.GetFormEntry(Species, Form);
    public override EntityContext Context => EntityContext.Gen8bLumi;

    public PB8LUMI()
    {
        EggLocation = MetLocation = Locations.Default8bNone;
        AffixedRibbon = Core.AffixedRibbon.None;
    }

    public PB8LUMI(Memory<byte> data) : base(data) { }
    public override PB8LUMI Clone() => new(Data.ToArray());

    public override ushort MaxSpeciesID => Legal.MaxSpeciesID_9;
    public override int MaxItemID => 1836;
}

using System;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Core;

/// <summary>Compact 1,025-species Pokédex used by Luminescent Platinum.</summary>
public sealed class Zukan8bLumi
{
    private const int MaxSpecies = Legal.MaxSpeciesID_9;
    private const int MaleShiny = 0x7B4;
    private const int FemaleShiny = 0xF68;
    private const int Male = 0x171C;
    private const int Female = 0x1ED0;
    private const int Language = 0x28FC;
    private readonly SAV8BSLuminescent _save;
    private readonly Memory<byte> _data;

    public Zukan8bLumi(SAV8BSLuminescent save, Memory<byte> data) => (_save, _data) = (save, data);

    public ZukanState8b GetState(ushort species)
    {
        Validate(species);
        var index = species - 1;
        return (ZukanState8b)((_data.Span[index / 2] >> ((index & 1) * 4)) & 0xF);
    }

    public void SetState(ushort species, ZukanState8b state)
    {
        Validate(species);
        var index = species - 1;
        ref var value = ref _data.Span[index / 2];
        var shift = (index & 1) * 4;
        value = (byte)((value & ~(0xF << shift)) | ((byte)state << shift));
    }

    public bool GetSeen(ushort species) => GetState(species) >= ZukanState8b.Seen;
    public bool GetCaught(ushort species) => GetState(species) >= ZukanState8b.Caught;

    public void SetDex(PKM pk)
    {
        if (pk.IsEgg || pk.Species is 0 or > MaxSpecies) return;
        SetState(pk.Species, ZukanState8b.Caught);
        SetGender(pk.Species, pk.Gender, pk.IsShiny);
        if (pk.Species <= Legal.MaxSpeciesID_8b)
            SetLanguage(pk.Species, pk.Language, true);
    }

    public void SetEntry(ushort species, bool seen, bool caught)
    {
        SetState(species, caught ? ZukanState8b.Caught : seen ? ZukanState8b.Seen : ZukanState8b.None);
    }

    private void SetGender(ushort species, byte gender, bool shiny)
    {
        var personal = PersonalTable.BDSPLUMI[species];
        SetBit(species, Male, !personal.OnlyFemale && gender == 0);
        SetBit(species, Female, !personal.OnlyMale && gender == 1);
        if (shiny)
        {
            SetBit(species, MaleShiny, !personal.OnlyFemale && gender == 0);
            SetBit(species, FemaleShiny, !personal.OnlyMale && gender == 1);
        }
    }

    private void SetLanguage(ushort species, int language, bool value)
    {
        if (language is 0 or 6 or > 10) return;
        var bit = language >= 7 ? language - 2 : language - 1;
        var offset = Language + sizeof(int) * (species - 1);
        var current = ReadInt32LittleEndian(_data.Span[offset..]);
        WriteInt32LittleEndian(_data.Span[offset..], value ? current | (1 << bit) : current & ~(1 << bit));
    }

    private void SetBit(ushort species, int offset, bool value)
    {
        var index = species - 1;
        ref var target = ref _data.Span[offset + index / 8];
        var mask = 1 << (index & 7);
        target = (byte)(value ? target | mask : target & ~mask);
    }

    private static void Validate(ushort species)
    {
        if (species is 0 or > MaxSpecies)
            throw new ArgumentOutOfRangeException(nameof(species));
    }
}

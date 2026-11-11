using PKBank.App.ViewModels;
using PKForge.Domain;
using PKForge.Engine;
using PKHeX.Core;

namespace PKBank.App.Services.Pokepark;

/// <summary>
/// The park's daycare: residents left there grow with EXP, as in the games' own daycares
/// (Gen 1–7 grow, Gen 8 and 9 only give eggs, Let's Go and Legends give neither), and the
/// growth is written back to the place the Pokémon really lives: its Bank entry or its slot
/// in the open save.
/// </summary>
public sealed class ParkDaycare(PokeparkService service, IBankService bank)
{
    /// <summary>The EXP a guest earns per hour in the park's daycare.</summary>
    public const uint ExpPerHour = 500;

    /// <summary>The save editor, when one is open (the daycare writes back into it).</summary>
    public Func<SaveEditorViewModel?>? SaveEditor { get; set; }

    /// <summary>The residents left at the daycare.</summary>
    public IReadOnlyList<ParkStay> Guests
    {
        get
        {
            lock (Gate) return [.. PokeparkStore.Load().Daycare];
        }
    }

    private static readonly Lock Gate = new();

    /// <summary>Leaves a resident at the daycare.</summary>
    public string Leave(ParkPokemon resident)
    {
        lock (Gate)
        {
            var data = PokeparkStore.Load();
            if (data.Daycare.Any(s => s.Id == resident.Id)) return $"{resident.Name} is already at the daycare.";
            var source = service.SourceOf(resident);
            if (source is null) return $"{resident.Name}'s home is not open right now, so it cannot grow there.";
            if (ParkDaycareService.RulesFor(source.Data, source.Format) is not { Grows: true })
                return $"{resident.Name}'s game has no daycare that gives EXP (only eggs, or none at all).";
            data.Daycare.Add(new ParkStay(resident.Id, DateTimeOffset.Now, 0));
            PokeparkStore.Save(data);
            service.AppendJournal(ParkJournalEventKind.Activity, $"{resident.Name} moved into the daycare.", [resident.Id]);
            return $"{resident.Name} is at the daycare. It grows a little every hour.";
        }
    }

    /// <summary>Takes a resident back from the daycare.</summary>
    public string TakeHome(ParkPokemon resident)
    {
        lock (Gate)
        {
            var data = PokeparkStore.Load();
            if (data.Daycare.RemoveAll(s => s.Id == resident.Id) == 0) return $"{resident.Name} is not at the daycare.";
            PokeparkStore.Save(data);
            return $"{resident.Name} came back from the daycare.";
        }
    }

    /// <summary>The EXP a guest has earned since it arrived.</summary>
    public static uint ExpOf(ParkStay stay, DateTimeOffset now)
    {
        var hours = Math.Max(0, (now - stay.Since).TotalHours);
        return (uint)Math.Floor(hours * ExpPerHour);
    }

    /// <summary>The growth a guest would have if it were collected now.</summary>
    public (int Before, int After, uint Exp)? Preview(ParkPokemon resident)
    {
        var stay = Guests.FirstOrDefault(s => s.Id == resident.Id);
        if (stay is null) return null;
        var source = service.SourceOf(resident);
        if (source is null) return null;
        var exp = ExpOf(stay, DateTimeOffset.Now);
        var preview = ParkDaycareService.Preview(source.Data, source.Format, exp);
        return (preview.LevelBefore, preview.LevelAfter, preview.ExpGained);
    }

    /// <summary>
    /// Takes a resident home and writes the growth back where it belongs: the Bank entry's
    /// bytes, or the slot of the open save (written with the editor's restore point).
    /// </summary>
    public async Task<string> CollectAsync(ParkPokemon resident)
    {
        var stay = Guests.FirstOrDefault(s => s.Id == resident.Id);
        if (stay is null) return $"{resident.Name} is not at the daycare.";
        var source = service.SourceOf(resident);
        if (source is null) return $"{resident.Name}'s home is not open right now; the growth stays in the park.";
        var exp = ExpOf(stay, DateTimeOffset.Now);
        if (exp <= 0) return $"{resident.Name} has not grown yet (it grows {ExpPerHour} EXP an hour).";

        if (source.BankId is { } bankId)
        {
            var grown = ParkDaycareService.GrowBytes(source.Data, source.Format, exp);
            if (grown is not { } result) return $"{resident.Name} could not be read; nothing was written.";
            if (result.Growth.ExpGained == 0) return $"{resident.Name} is already at the highest level its game allows.";
            var entry = bank.GetAll().FirstOrDefault(e => e.Id == bankId);
            if (entry is null) return "The Bank entry is gone; nothing was written.";
            var info = entry.Info with { Level = result.Growth.LevelAfter };
            bank.Replace(bankId, result.Data, info);
            lock (Gate)
            {
                var data = PokeparkStore.Load();
                data.Daycare.RemoveAll(s => s.Id == resident.Id);
                PokeparkStore.Save(data);
            }
            service.AppendJournal(ParkJournalEventKind.Activity,
                $"{resident.Name} grew at the daycare (level {result.Growth.LevelBefore} → {result.Growth.LevelAfter}).", [resident.Id]);
            return $"{resident.Name} grew to level {result.Growth.LevelAfter} (+{result.Growth.ExpGained} EXP).";
        }

        // A resident that lives in the open save: the slot itself grows, and the save is
        // written through the editor (restore point kept).
        var editor = SaveEditor?.Invoke();
        if (editor?.Save is not { } save || editor.FilePath is null)
            return "Open the save that holds this Pokémon to bring it back grown.";
        if (!TrySlotOf(resident, out var box, out var slot))
            return "That Pokémon is not in the open save any more.";
        var pk = box < 0 ? save.GetPartySlotAtIndex(slot) : save.GetBoxSlotAtIndex(box, slot);
        if (pk.Species <= 0) return "That slot is empty now.";
        var growth = ParkDaycareService.Preview(pk, exp);
        if (growth.ExpGained == 0) return $"{resident.Name} is already at the highest level its game allows.";
        pk.EXP += growth.ExpGained;
        pk.Stat_Level = (byte)growth.LevelAfter;
        pk.ResetPartyStats();
        pk.RefreshChecksum();
        if (box < 0) save.SetPartySlotAtIndex(pk, slot, EntityImportSettings.None);
        else save.SetBoxSlotAtIndex(pk, box, slot, EntityImportSettings.None);
        editor.Dirty = true;
        var (ok, message) = await editor.SaveAsync().ConfigureAwait(true);
        if (!ok) return "The growth could not be written: " + message;
        lock (Gate)
        {
            var data = PokeparkStore.Load();
            data.Daycare.RemoveAll(s => s.Id == resident.Id);
            PokeparkStore.Save(data);
        }
        service.AppendJournal(ParkJournalEventKind.Activity,
            $"{resident.Name} grew at the daycare (level {growth.LevelBefore} → {growth.LevelAfter}).", [resident.Id]);
        return $"{resident.Name} grew to level {growth.LevelAfter} (+{growth.ExpGained} EXP) and the save was written.";
    }

    /// <summary>
    /// The egg two residents would leave at the daycare, put into the Bank (their real data
    /// is never touched).
    /// </summary>
    public string Breed(ParkPokemon first, ParkPokemon second)
    {
        var a = service.SourceOf(first);
        var b = service.SourceOf(second);
        if (a is null || b is null) return "Both Pokémon need their save or Bank open to leave an egg.";
        var egg = ParkDaycareService.MakeEgg(a.Data, a.Format, b.Data, b.Format, out var why);
        if (egg is null) return why;
        var info = new BankEntryInfo(egg.Species, egg.Form, false, "", 1, 0, "Poképark daycare", egg.Format);
        var entry = bank.Add(egg.Data, info);
        service.AppendJournal(ParkJournalEventKind.Discovery,
            $"{first.Name} and {second.Name} left an egg at the daycare — it is in the Bank.", [first.Id, second.Id]);
        return $"{first.Name} and {second.Name} left an egg at the daycare. It is in the Bank (box {entry.Box + 1}, slot {entry.Slot + 1}).";
    }

    /// <summary>Whether two residents could leave an egg together right now.</summary>
    public bool CanBreed(ParkPokemon first, ParkPokemon second)
    {
        var a = service.SourceOf(first);
        var b = service.SourceOf(second);
        return a is not null && b is not null && ParkDaycareService.CanBreed(a.Data, a.Format, b.Data, b.Format);
    }

    /// <summary>The slot of a resident in the open save (the party is box -1).</summary>
    private bool TrySlotOf(ParkPokemon resident, out int box, out int slot)
    {
        box = 0; slot = 0;
        var parts = resident.Id.Split(':');
        if (parts is not ["save", _, var boxText, var slotText, ..]) return false;
        return int.TryParse(boxText, out box) && int.TryParse(slotText, out slot);
    }
}

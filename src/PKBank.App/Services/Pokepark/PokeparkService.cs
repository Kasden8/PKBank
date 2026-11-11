using System.Security.Cryptography;
using System.Text;
using PKForge.Domain;
using PKForge.Engine;
using PKHeX.Core;

namespace PKBank.App.Services.Pokepark;

/// <summary>
/// The park's roster and its bookkeeping: who lives there, where they came from, how they are
/// doing and what happened while the player was away. The residents themselves live in the
/// games; the park only remembers their faces.
/// </summary>
public sealed class PokeparkService(IBankService bank, IGameDataService data)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int?> _natures = new();
    private readonly Dictionary<string, DateOnly?> _metDates = new();

    public const int MaximumResidents = ParkLife.MostResidents;

    /// <summary>The save the player has open, when there is one (the park can take residents from it).</summary>
    public Func<SaveFile?>? CurrentSave { get; set; }

    public IReadOnlyList<ParkPokemon> Roster
    {
        get
        {
            lock (_gate) return [.. PokeparkStore.Load().Roster];
        }
    }

    public PokeparkSettings Settings
    {
        get
        {
            lock (_gate) return PokeparkStore.Load().Settings;
        }
    }

    public IReadOnlyList<ParkJournalEvent> Journal
    {
        get
        {
            lock (_gate) return [.. PokeparkStore.Load().Journal.TakeLast(80)];
        }
    }

    /// <summary>The species name, the same table every picker shows.</summary>
    public string SpeciesName(int species) =>
        (uint)species < (uint)data.SpeciesNames.Count ? data.SpeciesNames[species] : $"#{species}";

    /// <summary>A nature's name, in the game data's language.</summary>
    public string NatureName(int nature) => (uint)nature < (uint)data.NatureNames.Count ? data.NatureNames[nature] : $"#{nature}";

    /// <summary>Everything that could visit the park: the Bank's entries and the open save's.</summary>
    public IReadOnlyList<ParkPokemon> Candidates()
    {
        var result = bank.GetAll().Where(e => e.Info.Species > 0).Select(e =>
            new ParkPokemon($"bank:{e.Id:N}", e.Info.Species, e.Info.Form, e.Info.Shiny,
                Name(e.Info.Nickname, e.Info.Species), $"Bank · Box {e.Box + 1} · Slot {e.Slot + 1}",
                GameName: $"Gen {e.Info.Generation}", TrainerName: "", Origin: e.Info.SourceName)).ToList();
        if (CurrentSave?.Invoke() is not { } save) return result;
        var trainer = save.OT;
        var gameName = $"Gen {save.Generation}";
        var documentId = DocumentId(save);
        var fileName = SaveName(save);
        // The party as well as the boxes: party entries are valid visitors too.
        for (var slot = 0; slot < save.PartyCount; slot++)
        {
            var mon = save.GetPartySlotAtIndex(slot);
            if (mon.Species <= 0 || mon.IsEgg) continue;
            result.Add(new ParkPokemon($"save:{documentId}:-1:{slot}", mon.Species, mon.Form, mon.IsShiny,
                string.IsNullOrWhiteSpace(mon.Nickname) ? SpeciesName(mon.Species) : mon.Nickname,
                $"{fileName} · Party · Slot {slot + 1}", GameName: gameName, TrainerName: trainer, Origin: fileName));
        }
        for (var box = 0; box < save.BoxCount; box++)
        for (var slot = 0; slot < save.BoxSlotCount; slot++)
        {
            var mon = save.GetBoxSlotAtIndex(box, slot);
            if (mon.Species <= 0 || mon.IsEgg) continue;
            result.Add(new ParkPokemon($"save:{documentId}:{box}:{slot}", mon.Species, mon.Form, mon.IsShiny,
                string.IsNullOrWhiteSpace(mon.Nickname) ? SpeciesName(mon.Species) : mon.Nickname,
                $"{fileName} · Box {box + 1} · Slot {slot + 1}", GameName: gameName, TrainerName: trainer, Origin: fileName));
        }
        return result;
    }

    /// <summary>The save's name, as the player knows it.</summary>
    public static string SaveName(SaveFile save) => save.Metadata.FileName ?? save.OT ?? "Save";

    /// <summary>A stable id for the open save, so its residents keep their place between visits.</summary>
    public static string DocumentId(SaveFile save)
    {
        var bytes = Encoding.UTF8.GetBytes(SaveName(save));
        return Convert.ToHexString(SHA256.HashData(bytes))[..16];
    }

    /// <summary>The roster as it is stored, healed and de-duplicated.</summary>
    public IReadOnlyList<ParkPokemon> LoadRoster()
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            var roster = data.Roster
                .Select(p => IsLegacyFallbackName(p.Name, p.Species) ? p with { Name = SpeciesName(p.Species) } : p)
                .Where(p => p.Species > 0)
                .DistinctBy(p => p.Id)
                .Take(MaximumResidents)
                .ToArray();
            if (roster.Length != data.Roster.Count)
            {
                data.Roster = [.. roster];
                PokeparkStore.Save(data);
            }
            return roster;
        }
    }

    public string AddVisitor(ParkPokemon pokemon)
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            if (data.Roster.Any(p => p.Id == pokemon.Id)) return $"{pokemon.Name} already lives in Poképark.";
            if (data.Roster.Count >= MaximumResidents)
                return $"Poképark has {MaximumResidents} residents. Open the park and remove a resident before adding another.";
            data.Roster.Add(pokemon);
            data.AutoFillSuppressed = false;
            PokeparkStore.Save(data);
            AppendJournal(ParkJournalEventKind.Discovery, $"{pokemon.Name} moved into the park!", new[] { pokemon.Id });
            return $"{pokemon.Name} now lives in Poképark! Your original Pokémon stays in its save or bank.";
        }
    }

    /// <summary>Sends a Bank entry to the park (the entry stays in the Bank).</summary>
    public string AddBankVisitor(Guid id)
    {
        var entry = bank.GetAll().FirstOrDefault(e => e.Id == id);
        if (entry is null || entry.Info.Species <= 0) return "This Pokémon is no longer in the bank.";
        return AddVisitor(new ParkPokemon($"bank:{entry.Id:N}", entry.Info.Species, entry.Info.Form,
            entry.Info.Shiny, Name(entry.Info.Nickname, entry.Info.Species),
            $"Bank · Box {entry.Box + 1} · Slot {entry.Slot + 1}",
            GameName: $"Gen {entry.Info.Generation}", TrainerName: "", Origin: entry.Info.SourceName));
    }

    /// <summary>Sends a slot of the open save to the park (the Pokémon stays in the save).</summary>
    public string AddSaveVisitor(int box, int slot)
    {
        if (CurrentSave?.Invoke() is not { } save) return "Open a save to send a Pokémon to Poképark.";
        PKM? mon = box < 0 ? save.GetPartySlotAtIndex(slot) : save.GetBoxSlotAtIndex(box, slot);
        if (mon is null || mon.Species <= 0) return "Select a Pokémon first.";
        if (mon.IsEgg) return "Eggs can visit Poképark after they hatch.";
        var documentId = DocumentId(save);
        var fileName = SaveName(save);
        return AddVisitor(new ParkPokemon($"save:{documentId}:{box}:{slot}", mon.Species, mon.Form, mon.IsShiny,
            string.IsNullOrWhiteSpace(mon.Nickname) ? SpeciesName(mon.Species) : mon.Nickname,
            $"{fileName} · {(box < 0 ? "Party" : $"Box {box + 1}")} · Slot {slot + 1}",
            GameName: $"Gen {save.Generation}", TrainerName: save.OT, Origin: fileName));
    }

    public bool RemoveVisitor(string id)
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            if (!data.Roster.Any(p => p.Id == id)) return false;
            data.Roster.RemoveAll(p => p.Id == id);
            data.Care.RemoveAll(c => c.Id == id);
            data.AutoFillSuppressed = true;
            PokeparkStore.Save(data);
            return true;
        }
    }

    public int RemoveAllVisitors()
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            var count = data.Roster.Count;
            if (count > 0)
            {
                data.Roster.Clear();
                data.Care.Clear();
            }
            data.AutoFillSuppressed = true;
            PokeparkStore.Save(data);
            return count;
        }
    }

    /// <summary>Fills the park with residents when it is empty (the player never chose any).</summary>
    public int AutoFillIfEmpty()
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            if (data.Roster.Count > 0) return 0;
            return FillAutomatically(data, []);
        }
    }

    private int FillAutomatically(ParkData data, IReadOnlyList<ParkPokemon> previous)
    {
        if (data.AutoFillSuppressed) return 0;
        var settings = data.Settings;
        if (settings.Source == PokeparkSource.Disabled) return 0;
        var candidates = Candidates().Where(p => settings.Source == PokeparkSource.Both ||
            p.Id.StartsWith(settings.Source == PokeparkSource.Bank ? "bank:" : "save:", StringComparison.Ordinal)).ToArray();
        if (candidates.Length == 0) return 0;
        var fresh = candidates.Where(p => !previous.Any(old => old.Id == p.Id)).ToArray();
        if (fresh.Length >= Math.Min(settings.Count, MaximumResidents)) candidates = fresh;
        var roster = settings.Source == PokeparkSource.Both
            ? ParkSelection.SelectBalanced(candidates, p => p.Id,
                p => p.Id.StartsWith("save:", StringComparison.Ordinal) ? "save" : "bank", settings.Count)
            : ParkSelection.Select(candidates, p => p.Id, true, settings.Count, []);
        if (roster.Count == 0) return 0;
        data.Roster = [.. roster];
        data.Initialized = true;
        PokeparkStore.Save(data);
        return roster.Count;
    }

    public void SaveSettings(PokeparkSettings settings)
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            data.Settings = settings with
            {
                Source = Enum.IsDefined(settings.Source) ? settings.Source : PokeparkSource.Both,
                Count = Math.Clamp(settings.Count, 1, MaximumResidents),
                SelectedIds = (settings.SelectedIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(MaximumResidents).ToArray(),
            };
            if (data.Settings.Source != PokeparkSource.Disabled) data.AutoFillSuppressed = false;
            if (data.Roster.Count == 0) data.Initialized = false;
            PokeparkStore.Save(data);
        }
    }

    /// <summary>Writes an entry in the park's journal.</summary>
    public void AppendJournal(ParkJournalEventKind kind, string text, IReadOnlyList<string> residentIds, string habitat = "")
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            data.Journal.Add(new ParkJournalEvent(DateTimeOffset.Now, kind, text, habitat, residentIds));
            if (data.Journal.Count > 80) data.Journal.RemoveRange(0, data.Journal.Count - 80);
            PokeparkStore.Save(data);
        }
    }

    /// <summary>The day the player met the resident, when the game knows it (Gen 4 and later).</summary>
    public DateOnly? MetDateOf(ParkPokemon mon)
    {
        lock (_gate) if (_metDates.TryGetValue(mon.Id, out var known)) return known;
        DateOnly? met = null;
        try
        {
            var parts = mon.Id.Split(':');
            if (parts is ["bank", var guid, ..] && Guid.TryParse(guid, out var id) &&
                bank.GetAll().FirstOrDefault(e => e.Id == id) is { } entry &&
                PKForge.Engine.EntityBytes.Parse(entry, bank.GetData(id)) is { } bankPk)
                met = bankPk.MetDate;
            else if (parts is ["save", var document, var boxText, var slotText, ..] && CurrentSave?.Invoke() is { } save &&
                     DocumentId(save) == document && int.TryParse(boxText, out var box) && int.TryParse(slotText, out var slot))
            {
                PKM? savePk = box < 0 ? save.GetPartySlotAtIndex(slot) : save.GetBoxSlotAtIndex(box, slot);
                if (savePk is not null) met = savePk.MetDate;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or IOException or FormatException)
        {
            // No date to celebrate.
        }
        if (met is { } found) lock (_gate) _metDates[mon.Id] = found;
        return met;
    }

    /// <summary>The resident's nature, read from the Bank or from the open save; null when its
    /// save is not open.</summary>
    public int? NatureOf(ParkPokemon mon)
    {
        lock (_gate) if (_natures.TryGetValue(mon.Id, out var known)) return known;
        int? nature = null;
        try
        {
            var parts = mon.Id.Split(':');
            if (parts is ["bank", var guid, ..] && Guid.TryParse(guid, out var id) &&
                bank.GetAll().FirstOrDefault(e => e.Id == id) is { } entry &&
                PKForge.Engine.EntityBytes.Parse(entry, bank.GetData(id)) is { } bankPk)
                nature = (int)bankPk.Nature;
            else if (parts is ["save", var document, var boxText, var slotText, ..] && CurrentSave?.Invoke() is { } save &&
                     DocumentId(save) == document && int.TryParse(boxText, out var box) && int.TryParse(slotText, out var slot))
            {
                PKM? savePk = box < 0 ? save.GetPartySlotAtIndex(slot) : save.GetBoxSlotAtIndex(box, slot);
                if (savePk is not null && savePk.Species == mon.Species) nature = (int)savePk.Nature;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or IOException or FormatException)
        {
            // An unreadable entry keeps the taste the park gives it from its id.
        }
        if (nature is { } found) lock (_gate) _natures[mon.Id] = found;
        return nature;
    }

    /// <summary>Where a resident's real data lives: a Bank entry, or a slot of the open save.</summary>
    public sealed record ParkSource(byte[] Data, string? Format, Guid? BankId, int Box, int Slot, string SourceName);

    public ParkSource? SourceOf(ParkPokemon mon)
    {
        try
        {
            var parts = mon.Id.Split(':');
            if (parts is ["bank", var guid, ..] && Guid.TryParse(guid, out var id) &&
                bank.GetAll().FirstOrDefault(e => e.Id == id) is { } entry)
                return new(bank.GetData(id), entry.Info.Format, id, entry.Box, entry.Slot, entry.Info.SourceName);
            if (parts is ["save", var document, var boxText, var slotText, ..] && CurrentSave?.Invoke() is { } save &&
                DocumentId(save) == document && int.TryParse(boxText, out var box) && int.TryParse(slotText, out var slot))
            {
                PKM? pk = box < 0 ? save.GetPartySlotAtIndex(slot) : save.GetBoxSlotAtIndex(box, slot);
                if (pk is null || pk.Species != mon.Species) return null;
                var bytes = new byte[pk.Data.Length];
                pk.WriteDecryptedDataStored(bytes);
                return new(bytes, pk.GetType().Name, null, box, slot, SaveName(save));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or IOException or FormatException)
        {
            // Unreadable: the park keeps it as it is.
        }
        return null;
    }

    /// <summary>The care states as they are stored.</summary>
    public IReadOnlyList<ParkCareState> CareStates
    {
        get
        {
            lock (_gate) return [.. PokeparkStore.Load().Care];
        }
    }

    /// <summary>Saves the residents' care (hunger, energy, fun, bond) between visits.</summary>
    public void SaveCare(IEnumerable<ParkCareState> care)
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            data.Care = [.. care];
            PokeparkStore.Save(data);
        }
    }

    /// <summary>Today's goals and how far along they are (the park's own saved line).</summary>
    public string? Goals
    {
        get
        {
            lock (_gate) return PokeparkStore.Load().Goals;
        }
    }

    public void SaveGoals(string? goals)
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            data.Goals = goals;
            PokeparkStore.Save(data);
        }
    }

    /// <summary>The treasures found in the park.</summary>
    public IReadOnlyList<ParkTreasure> Treasures
    {
        get
        {
            lock (_gate) return [.. PokeparkStore.Load().Treasures];
        }
    }

    public void SaveTreasures(IEnumerable<ParkTreasure> treasures)
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            data.Treasures = [.. treasures];
            PokeparkStore.Save(data);
        }
    }

    /// <summary>The wild friends met in the park.</summary>
    public IReadOnlyList<ParkWildFriend> Wilds
    {
        get
        {
            lock (_gate) return [.. PokeparkStore.Load().Wilds];
        }
    }

    public void SaveWilds(IEnumerable<ParkWildFriend> wilds)
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            data.Wilds = [.. wilds];
            PokeparkStore.Save(data);
        }
    }

    /// <summary>The relationships between the residents, as the park remembers them.</summary>
    public ParkSocialState? Social
    {
        get
        {
            lock (_gate) return PokeparkStore.Load().Social;
        }
    }

    public void SaveSocial(ParkSocialState social)
    {
        lock (_gate)
        {
            var data = PokeparkStore.Load();
            data.Social = social;
            PokeparkStore.Save(data);
        }
    }

    private string Name(string name, int species) => string.IsNullOrWhiteSpace(name) ? SpeciesName(species) : name;

    /// <summary>Matches exactly the old fallback pattern "Pokémon #N" (and its ASCII spelling),
    /// never a real nickname the player could have typed.</summary>
    private static bool IsLegacyFallbackName(string name, int species) =>
        name.Equals($"Pokémon #{species}", StringComparison.OrdinalIgnoreCase) ||
        name.Equals($"Pokemon #{species}", StringComparison.OrdinalIgnoreCase);
}

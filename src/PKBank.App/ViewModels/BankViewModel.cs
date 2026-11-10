using CommunityToolkit.Mvvm.ComponentModel;
using PKForge.Domain;
using PKForge.Engine;
using PKForge.Infrastructure;
using PKHeX.Core;

namespace PKBank.App.ViewModels;

/// <summary>
/// The Bank: the player's whole collection (HOME-like). Read entries, deposit from saves,
/// withdraw into the open save (with cross-gen conversion), export/import folders, basic
/// drag reorder inside the bank. Stored entries keep their pristine bytes — the bank is a
/// vault, never a transcoder.
/// </summary>
public sealed partial class BankViewModel : ObservableObject
{
    private readonly IBankService _bank;
    private readonly SaveEngine _engine;

    [ObservableProperty] private List<BankEntry> _entries = [];
    [ObservableProperty] private int _boxIndex;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _status = "The bank is empty — deposit something from a save";
    [ObservableProperty] private int _boxTotal;

    /// <summary>Kind filter of the search strip.</summary>
    [ObservableProperty] private string _kindFilter = "all";

    public BankViewModel(IBankService bank, SaveEngine engine)
    {
        _bank = bank;
        _engine = engine;
        Refresh();
    }

    public IReadOnlyList<BankEntry> CurrentEntries =>
        Entries.Where(e => e.Box == BoxIndex).ToList();

    public bool HasSaveToWithdrawFrom => CurrentSaveProvider is not null;
    /// <summary>Set from the shell: returns the open save editor (or null when no save is open).</summary>
    public Func<SaveEditorViewModel?>? CurrentSaveProvider { get; set; }

    public void Refresh()
    {
        Entries = [.. _bank.GetAll()];
        BoxTotal = _bank.BoxCount;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var total = Entries.Count;
        Status = total == 0
            ? "The bank is empty — deposit something from a save"
            : $"{total} stored · {BoxTotal} box(es) · box {BoxIndex + 1}";
    }

    public BankEntry? Entry(int box, int slot) =>
        Entries.FirstOrDefault(e => e.Box == box && e.Slot == slot);

    private static bool Matches(BankEntry entry, string search, string kindFilter)
    {
        if (kindFilter == "shiny" && !entry.Info.Shiny) return false;
        if (kindFilter == "normal" && entry.Info.Shiny) return false;
        if (kindFilter == "legendary" && !IsSpecial(entry.Info.Species)) return false;
        if (kindFilter == "egg" && entry.Info.Species == 0) return false;
        if (string.IsNullOrWhiteSpace(search)) return true;
        var speciesName = SpeciesName.GetSpeciesName((ushort)entry.Info.Species, (int)LanguageID.English);
        return speciesName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Info.Nickname.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpecial(int species)
    {
        // Best-effort marker: mythical/legendary ranges; names are displayed, not gated.
        var s = species;
        return s is 144 or 145 or 146 or 150 or 151 or 243 or 244 or 245 or 249 or 250 or 251
            or 377 or 378 or 379 or 380 or 381 or 382 or 383 or 384 or 385
            or 480 or 481 or 482 or 483 or 484 or 485 or 486 or 487 or 488 or 489 or 490 or 491 or 492 or 493 or 494
            or 638 or 639 or 640 or 641 or 642 or 643 or 644 or 645 or 646 or 647 or 648 or 649
            or 716 or 717 or 718 or 719 or 720 or 721
            or 778 or 785 or 786 or 787 or 788 or 789 or 790 or 791 or 792 or 800 or 801 or 802 or 803 or 804 or 805 or 806 or 807 or 808 or 809
            or 888 or 889 or 890 or 891 or 892 or 893 or 894
            or 1001 or 1002 or 1003 or 1004 or 1008 or 1009 or 1010 or 1011 or 1012 or 1013 or 1014 or 1015 or 1016 or 1017 or 1018 or 1019 or 1020 or 1022 or 1023 or 1024 or 1025 or 1026 or 1027 or 1028 or 1029 or 1030;
    }

    /// <summary>Filtered view of the bank for the current box (search is applied before paging).</summary>
    public IReadOnlyList<BankEntry> VisibleEntries()
    {
        var all = Entries.Where(e => Matches(e, SearchText ?? "", KindFilter));
        var sorted = KindFilter switch
        {
            "shiny" => all.OrderByDescending(e => e.Info.Shiny).ThenBy(e => e.Info.Species),
            "legendary" => all.Where(e => IsSpecial(e.Info.Species)).OrderBy(e => e.Info.Species),
            "egg" => all.Where(e => e.Info.Species == 0),
            _ => all.OrderBy(e => e.Info.Species).ThenBy(e => e.Info.Shiny).ThenBy(e => e.Info.Level),
        };
        return sorted.Where(e => e.Box == BoxIndex).ToList();
    }

    /// <summary>Reordering inside the bank: drag is a move when the target is free, otherwise a swap.</summary>
    public bool MoveBank(Guid id, int toBox, int toSlot)
    {
        var entry = _bank.GetAll().FirstOrDefault(e => e.Id == id);
        if (entry is null) return false;
        if (entry.Box == toBox && entry.Slot == toSlot) return true;
        _bank.Move(id, toBox, toSlot);
        Refresh();
        return true;
    }

    /// <summary>Removes an entry from the bank (destructive — callers ask first).</summary>
    public void Remove(Guid id)
    {
        _bank.Remove(id);
        Refresh();
    }

    /// <summary>Adds one more empty box at the end of the vault.</summary>
    public void BankAddBox()
    {
        _bank.AddBox();
        BoxTotal = _bank.BoxCount;
        Refresh();
    }

    /// <summary>Deposits bytes acquired from a save (move=false copies, move=true releases the save's slot).</summary>
    public async ValueTask<string> DepositAsync(byte[] data, BankEntryInfo info, Func<Task<string?>>? releaseFromSave = null)
    {
        var deposit = new BankDeposit(data, info);
        var outcome = await BankTransfers.DepositAsync(_bank, [deposit], releaseFromSave).ConfigureAwait(true);
        Refresh();
        return outcome.Message;
    }

    /// <summary>What a withdrawal would do before anything is written.</summary>
    public sealed record WithdrawPreviewItem(
        Guid Id,
        string SpeciesName,
        string FromFormat,
        string ToFormat,
        EntityConverterResult ConversionResult,
        bool IsIdentical,
        bool IsLegalAfter);

    /// <summary>Converts (clone-only) and analyzes (legality) without touching disk or the bank.</summary>
    public IReadOnlyList<WithdrawPreviewItem> PreviewWithdraw(IReadOnlyList<Guid> ids)
    {
        var saveVm = CurrentSaveProvider?.Invoke();
        if (saveVm?.Save is null || ids.Count == 0) return [];
        var sav = saveVm.Save;

        var items = new List<WithdrawPreviewItem>();
        foreach (var id in ids)
        {
            var entry = _bank.GetAll().FirstOrDefault(e => e.Id == id);
            if (entry is null) continue;
            var entity = EntityBytes.Parse(_bank.GetData(id), entry.Info.Format);
            if (entity is null) continue;
            var identical = entity.GetType() == sav.PKMType;
            PKM? converted = entity.Clone();
            var result = EntityConverterResult.Success;
            if (!identical)
            {
                converted = EntityConverter.ConvertToType(entity, sav.PKMType, out result);
            }
            var legalAfter = converted is not null && new LegalityAnalysis(converted).Valid;
            items.Add(new WithdrawPreviewItem(
                id,
                entry.Info.Nickname is { Length: > 0 } nick && entry.Info.Species > 0 ? nick : SpeciesName.GetSpeciesName((ushort)entry.Info.Species, (int)LanguageID.English),
                EntityBytes.FormatOf(entity),
                converted is null ? "—" : EntityBytes.FormatOf(converted),
                result,
                identical,
                legalAfter));
        }
        return items;
    }

    /// <summary>Withdraw to the currently open save. Cross-gen converted with warnings when possible.</summary>
    public async ValueTask<string> WithdrawAsync(IReadOnlyList<Guid> ids, bool copy = false)
    {
        var saveVm = CurrentSaveProvider?.Invoke();
        if (saveVm?.Save is null || ids.Count == 0) return "No save open — open a save before withdrawing.";
        var sav = saveVm.Save;

        var parsed = new List<(BankEntry entry, byte[] bytes, PKM entity)>();
        var refused = new List<string>();
        foreach (var id in ids)
        {
            var entry = _bank.GetAll().FirstOrDefault(e => e.Id == id);
            if (entry is null) { refused.Add("unknown entry"); continue; }
            var bytes = _bank.GetData(id);
            var entity = EntityBytes.Parse(bytes, entry.Info.Format);
            if (entity is null) { refused.Add($"{entry.Info.Nickname}: unparsable bytes"); continue; }
            parsed.Add((entry, bytes, entity));
        }
        if (parsed.Count == 0) return "Nothing to withdraw." + (refused.Count > 0 ? " Problems: " + string.Join("; ", refused) : "");

        var saveBox = FirstBoxWithRoom(sav, saveVm.BoxIndex, parsed.Count);
        if (saveBox < 0)
            return "Not enough empty slots in any box (" + parsed.Count + " needed).";

        var free = FreeSlots(sav, saveBox);

        var placements = new List<PKM>();
        var conversions = new List<string>();
        foreach (var (entry, bytes, entity) in parsed)
        {
            var targetType = sav.PKMType;
            if (entity.GetType() == targetType)
            {
                placements.Add(entity.Clone());
            }
            else
            {
                var converted = EntityConverter.ConvertToType(entity, targetType, out var result);
                if (converted is null)
                {
                    refused.Add($"{entry.Info.Nickname}: cannot convert ({result})");
                    continue;
                }
                placements.Add(converted);
                conversions.Add($"{entry.Info.Nickname}: converted {EntityBytes.FormatOf(entity)} -> {EntityBytes.FormatOf(converted)} ({result})");
            }
        }
        if (placements.Count == 0) return "Nothing could be converted." + (refused.Count > 0 ? " " + string.Join("; ", refused) : "");
        if (placements.Count > free.Count) return "Not enough empty slots in the current box (" + placements.Count + " needed).";

        var targetSlots = free.Take(placements.Count).ToArray();
        var undoBytes = new List<PKM>();
        for (var i = 0; i < placements.Count; i++)
            undoBytes.Add(sav.GetBoxSlotAtIndex(saveBox, targetSlots[i]).Clone());

        if (copy)
        {
            for (var i = 0; i < placements.Count; i++)
                sav.SetBoxSlotAtIndex(placements[i].Clone(), saveBox, targetSlots[i], EntityImportSettings.None);
            saveVm.Dirty = true;
            return $"Copied {placements.Count} to the save." + (conversions.Count > 0 ? " Conversions: " + string.Join("; ", conversions) : "");
        }

        var placed = new List<PKM>(placements);
        var outcome = await BankTransfers.WithdrawAsync(_bank, [.. parsed.Take(placed.Count).Select(p => p.entry.Id)],
            $"the save's box {saveBox + 1}",
            () =>
            {
                for (var i = 0; i < placed.Count; i++)
                    sav.SetBoxSlotAtIndex(placed[i].Clone(), saveBox, targetSlots[i], EntityImportSettings.None);
                saveVm.Dirty = true;
                return Task.FromResult<string?>(null);
            },
            () =>
            {
                for (var i = 0; i < placed.Count; i++)
                    sav.SetBoxSlotAtIndex(undoBytes[i].Clone(), saveBox, targetSlots[i], EntityImportSettings.None);
                return Task.FromResult<string?>(null);
            }).ConfigureAwait(true);
        Refresh();
        return outcome.Message + (conversions.Count > 0 ? " Conversions: " + string.Join("; ", conversions) : "");
    }

    /// <summary>First box (from the current one onward) with at least <paramref name="needed"/> free slots; -1 if none.</summary>
    private static int FirstBoxWithRoom(SaveFile sav, int startBox, int needed)
    {
        for (var box = startBox; box < sav.BoxCount; box++)
        {
            var free = 0;
            for (var s = 0; s < 30 && free < needed; s++)
                if (sav.GetBoxSlotAtIndex(box, s).Species == 0) free++;
            if (free >= needed) return box;
        }
        return -1;
    }

    private static List<int> FreeSlots(SaveFile sav, int box)
    {
        var result = new List<int>();
        for (var s = 0; s < 30 && result.Count < 30; s++)
        {
            var pk = sav.GetBoxSlotAtIndex(box, s);
            if (pk.Species == 0) result.Add(s);
        }
        return result;
    }

    /// <summary>Exports the whole bank (or one selection) into a folder of .pk files (named by species).</summary>
    public string ExportFolder(string folder, IReadOnlyList<Guid>? onlyIds = null)
    {
        Directory.CreateDirectory(folder);
        var entries = Entries.Where(e => onlyIds is null || onlyIds.Contains(e.Id));
        var count = 0;
        foreach (var entry in entries)
        {
            var ext = (entry.Info.Format ?? "pkx").ToLowerInvariant();
            var safeName = entry.Info.Nickname is { Length: > 0 } nick && nick.Length > 0 ? Sanitize(nick) : $"Pokemon_{entry.Info.Species}";
            var file = Path.Combine(folder, $"{safeName}.{ext}");
            File.WriteAllBytes(file, _bank.GetData(entry.Id));
            count++;
        }
        return $"Exported {count} file(s) to {folder}.";
    }

    /// <summary>Imports every .pk file of the given format from a folder, preserving the recorded format.</summary>
    public string ImportFolder(string folder)
    {
        if (!Directory.Exists(folder)) return "Folder not found: " + folder;
        var deposits = new List<BankDeposit>();
        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var bytes = File.ReadAllBytes(file);
            var format = EntityBytes.Normalize(Path.GetExtension(file).TrimStart('.'));
            var info = _engine.TryDescribeEntity(bytes, Path.GetFileNameWithoutExtension(file), format);
            if (info is null) continue; // not a readable entity; skip silently
            deposits.Add(new BankDeposit(bytes, info));
        }
        if (deposits.Count == 0) return "No readable .pk files found in that folder.";
        var added = _bank.AddMany(deposits);
        Refresh();
        return $"{added.Count} imported into the bank.";
    }

    private static string Sanitize(string value)
    {
        var bad = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value);
        for (var i = 0; i < builder.Length; i++)
            if (bad.Contains(builder[i])) builder[i] = '_';
        return builder.ToString();
    }
}

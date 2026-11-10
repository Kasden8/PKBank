using CommunityToolkit.Mvvm.ComponentModel;
using PKForge.Domain;
using PKForge.Engine;
using PKForge.Infrastructure;
using PKHeX.Core;

namespace PKBank.App.ViewModels;

/// <summary>
/// The save editor's working state: the opened save (mutable), the current box, the selection,
/// and the dirty flag. The bank and the summary builders read through this same model.
/// </summary>
public partial class SaveEditorViewModel : ObservableObject
{
    private readonly IBackupService _backups;
    private readonly ISafeSaveWriter _writer;
    private readonly IBankService _bank;
    private readonly SaveEngine _engine;

    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private SaveFile? _save;
    [ObservableProperty] private bool _dirty;
    [ObservableProperty] private int _boxIndex;
    [ObservableProperty] private string _status = "Open a save file to start";

    private SaveSnapshot? _snapshot;
    private byte[] _originalBytes = [];

    public SaveEditorViewModel(IBackupService backups, ISafeSaveWriter writer, IBankService bank, SaveEngine engine)
    {
        _backups = backups;
        _writer = writer;
        _bank = bank;
        _engine = engine;
    }

    public bool HasSave => Save is not null;
    public int BoxTotal => Save?.BoxCount ?? 0;
    public string BoxTitle
    {
        get
        {
            if (Save is null) return "";
            var name = (Save as IBoxDetailNameRead)?.GetBoxName(BoxIndex) ?? BoxDetailNameExtensions.GetDefaultBoxName(BoxIndex);
            return name is { Length: > 0 } ? name : $"BOX {BoxIndex + 1}";
        }
    }

    public PKM Slot(int box, int slot) => Save!.GetBoxSlotAtIndex(box, slot);
    public PKM Slot(int slot) => Slot(BoxIndex, slot);
    public PKM PartySlot(int slot) => Save!.GetPartySlotAtIndex(slot);
    public int PartyCount => Save?.PartyCount ?? 0;

    /// <summary>Reads the file bytes, takes a restore point before any edit touches the save, then parses.</summary>
    public async ValueTask<string?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var save = SaveUtil.GetSaveFile(bytes, path);
        if (save is null) return "Not a save file this build understands (or it has a bogus format).";

        FilePath = path;
        FileName = Path.GetFileName(path);
        Save = save;
        _originalBytes = bytes;
        _snapshot = BuildSnapshot(save, bytes, FileName);
        await _backups.CreateAsync(_snapshot, "Opened save", path, cancellationToken).ConfigureAwait(false);
        BoxIndex = Math.Min(BoxIndex, Math.Max(0, save.BoxCount - 1));
        Dirty = false;
        Status = $"{FileName} · {save.Context} · Gen {save.Generation} opened · restore point kept";
        return null;
    }

    private static SaveSnapshot BuildSnapshot(SaveFile save, byte[] bytes, string name)
    {
        var slots = new List<SlotSummary>();
        for (var b = 0; b < save.BoxCount; b++)
        {
            for (var s = 0; s < save.BoxSlotCount; s++)
            {
                var pk = save.GetBoxSlotAtIndex(b, s);
                if (pk.Species == 0) continue;
                slots.Add(new SlotSummary(b, s, pk.Species, pk.IsNicknamed ? pk.Nickname : null, pk.IsShiny, pk.Valid, pk.Form, pk.IsEgg, pk.HeldItem, EntitySprite.Traits(pk)));
            }
        }
        for (var i = 0; i < Math.Min(6, save.PartyCount); i++)
        {
            var pk = save.GetPartySlotAtIndex(i);
            if (pk.Species == 0) continue;
            slots.Add(new SlotSummary(-1, i, pk.Species, pk.Nickname, pk.IsShiny, true, pk.Form, pk.IsEgg, pk.HeldItem, EntitySprite.Traits(pk)));
        }
        return new SaveSnapshot(save.Context.ToString(), save.Generation, bytes, slots, name);
    }

    private bool IsEmpty(PKM pk) => pk.Species == 0;

    /// <summary>
    /// Re-parses <paramref name="newBytes"/> into the working save while keeping the original
    /// snapshot/restore-point semantics (used by Mystery Gift injection); marks the save dirty.
    /// </summary>
    public void ReplaceLoadedBytes(byte[] newBytes, string status)
    {
        var save = SaveUtil.GetSaveFile(newBytes);
        if (save is null)
        {
            Status = "The rewritten save could not be parsed.";
            return;
        }
        Save = save;
        Dirty = true;
        Status = status;
    }

    /// <summary>Moves/swaps a box slot to another box slot, box↔party (capacity-checked), then marks dirty.</summary>
    public bool Move(int fromBox, int fromSlot, bool fromParty, int toBox, int toSlot, bool toParty)
    {
        if (Save is null) return false;
        if (fromParty == toParty && fromBox == toBox && fromSlot == toSlot) return false; // no-op
        var sav = Save!;
        var from = ReadSlot(fromParty, fromBox, fromSlot);
        var to = ReadSlot(toParty, toBox, toSlot);
        if (from.Species == 0 && to.Species == 0) return false; // nothing to swap
        WriteSlot(fromParty, fromBox, fromSlot, to.Clone());
        WriteSlot(toParty, toBox, toSlot, from.Clone());
        Dirty = true;
        return true;
    }

    private PKM ReadSlot(bool isParty, int box, int slot) => isParty ? PartySlot(slot) : Slot(box, slot);

    private void WriteSlot(bool isParty, int box, int slot, PKM value)
    {
        if (Save is null) return;
        if (isParty) Save.SetPartySlotAtIndex(value, slot, EntityImportSettings.None);
        else Save.SetBoxSlotAtIndex(value, box, slot, EntityImportSettings.None);
    }

    /// <summary>Legality verdict for an entity, computed lazily and cached per (box, slot) by the view.</summary>
    public static bool AnalyseLegal(PKM pk)
    {
        try { return new LegalityAnalysis(pk).Valid; }
        catch (Exception) { return false; }
    }

    /// <summary>Writes the current state back with a restore point first.</summary>
    public async ValueTask<(bool ok, string message)> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (Save is null || FilePath is null || _snapshot is null) return (false, "Nothing to save yet.");
        if (!Dirty) return (true, "No changes to save.");

        var candidate = Save.Write(BinaryExportSetting.None).ToArray();
        SaveWriteReceipt receipt;
        try
        {
            receipt = await _writer.WriteAsync(FilePath, _snapshot, candidate, "Box edits", cancellationToken).ConfigureAwait(false);
        }
        catch (UnsafeSaveWriteException ex)
        {
            return (false, $"The write was refused for safety: {ex.Message} The original was not touched.");
        }
        _originalBytes = candidate;
        _snapshot = BuildSnapshot(Save, candidate, FileName);
        Dirty = false;
        Status = $"{FileName} saved · restore point {receipt.BackupId} · {DateTimeOffset.Now.ToLocalTime():HH:mm:ss}";
        return (true, Status);
    }

    /// <summary>Deposits the Pokémon at the given slot to the Bank. Sends (move=true) or copies it.</summary>
    public BankEntryInfo? DescribeEntityForBank(byte[] data, string source) => _engine.TryDescribeEntity(data, source);

    public async ValueTask<string> DepositSelectedAsync(bool isParty, int box, int slot, bool move)
    {
        if (Save is null) return "No save open.";
        var pk = isParty ? PartySlot(slot) : Slot(box, slot);
        if (pk.Species == 0) return "That slot is empty.";
        var bytes = pk.Data.ToArray();
        var info = _engine.TryDescribeEntity(bytes, isParty ? $"Party slot {slot + 1}" : $"Box {box + 1} slot {slot + 1}", EntityBytes.FormatOf(pk));
        if (info is null) return "Unreadable data — could not deposit.";
        var deposit = new BankDeposit(bytes, info);
        Func<Task<string?>>? releaseFromSave = move
            ? () =>
            {
                if (isParty) Save.SetPartySlotAtIndex(Save.BlankPKM, slot, EntityImportSettings.None);
                else Save.SetBoxSlotAtIndex(Save.BlankPKM, box, slot, EntityImportSettings.None);
                Dirty = true;
                return Task.FromResult<string?>(null);
            }
            : null;
        var outcome = await BankTransfers.DepositAsync(_bank, [deposit], releaseFromSave).ConfigureAwait(true);
        Status = outcome.Message;
        return outcome.Message;
    }
}

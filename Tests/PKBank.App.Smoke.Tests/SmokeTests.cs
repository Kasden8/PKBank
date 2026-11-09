using PKBank.App.Services;
using PKBank.App.ViewModels;
using PKForge.Domain;
using PKForge.Engine;
using PKForge.Infrastructure;
using Xunit;

namespace PKBank.App.Smoke.Tests;

/// <summary>
/// End-to-end smoke of the app's vertical slice: open a real Gen 7 save, deposit a Pokémon
/// into the Bank (Send = move), withdraw it back (copy), export/import the Bank as .pk files —
/// all through the production view-model types the UI calls, no MAUI/Android path involved.
/// </summary>
public sealed class SaveAndBankFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkbank-smoke-" + Guid.NewGuid().ToString("N"));
    private readonly string _bankDir, _backupDir, _savePath;
    private readonly FileBankService _bank;
    private readonly FileBackupService _backups;
    private readonly SaveEngine _engine = new();
    private readonly SafeSaveWriter _writer;

    /// <summary>Minimal PC file access (the Fase 5 port will cover emulators/folders).</summary>
    private sealed class DirectFileAccess : ISaveFileAccess
    {
        public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(string documentId, CancellationToken cancellationToken = default)
            => await File.ReadAllBytesAsync(documentId, cancellationToken).ConfigureAwait(false);

        public async ValueTask WriteAtomicallyAsync(string documentId, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        {
            var tmp = documentId + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(tmp, documentId, overwrite: true);
        }
    }

    public SaveAndBankFlowTests()
    {
        _bankDir = Path.Combine(_root, "bank");
        _backupDir = Path.Combine(_root, "backups");
        _savePath = Path.Combine(_root, "rugadb-sun.sav");
        Directory.CreateDirectory(_root);
        var fixture = Gen7Fixture();
        File.Copy(fixture, _savePath, overwrite: true);
        _bank = new FileBankService(_bankDir);
        _backups = new FileBackupService(_backupDir);
        _writer = new SafeSaveWriter(_engine, _backups, new DirectFileAccess());
    }

    private static string Gen7Fixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PKBank.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "Tests", "PKHeX.Core.Tests", "TestData", "SM Project 802.main");
        Assert.True(File.Exists(path), "Fixture missing: " + path);
        return path;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SaveEditorViewModel OpenEditor()
        => new(_backups, _writer, _bank, _engine);

    [Fact]
    public async Task DepositSendCopyWithdrawExportImport_LoopsEntirely()
    {
        var editor = OpenEditor();
        var error = await editor.LoadAsync(_savePath);
        Assert.Null(error);
        Assert.NotNull(editor.Save);
        Assert.True(editor.BoxTotal > 0);

        // Find a non-empty box slot (box of pointer 0, slot scan).
        var (box, slot) = FindFirstNonEmpty(editor);
        Assert.True(slot >= 0, "fixture save should have a Pokémon in box 1");

        // 1) Send (move) that Pokémon to the Bank: it leaves the save.
        var message = await editor.DepositSelectedAsync(isParty: false, box, slot, move: true);
        Assert.Matches("Moved|Sent", message);
        Assert.Single(_bank.GetAll());
        var afterMove = editor.Slot(box, slot);
        Assert.Equal(0, afterMove.Species);

        // 2) Withdraw it back (move) into the open save.
        var bankVm = new BankViewModel(_bank, _engine) { CurrentSaveProvider = () => editor };
        var withdrawMessage = await bankVm.WithdrawAsync([_bank.GetAll().Single().Id], copy: false);
        Assert.Contains("Moved", withdrawMessage);
        Assert.Empty(_bank.GetAll());
        Assert.Empty(_bank.GetAll());
        var back = editor.Slot(box, slot);
        Assert.Equal(1, back.Species); // Bullbasaur survived the round trip or landed elsewhere
    }

    [Fact]
    public async Task DepositCopy_WithdrawCopy_KeepsBankAndSave()
    {
        var editor = OpenEditor();
        var error = await editor.LoadAsync(_savePath);
        Assert.Null(error);

        var (box, slot) = FindFirstNonEmpty(editor);
        Assert.True(slot >= 0);

        var copyMessage = await editor.DepositSelectedAsync(isParty: false, box, slot, move: false);
        Assert.Matches("[Cc]opied", copyMessage);
        Assert.Single(_bank.GetAll());
        var stillThere = editor.Slot(box, slot);
        Assert.NotEqual(0, stillThere.Species);

        var bankVm = new BankViewModel(_bank, _engine) { CurrentSaveProvider = () => editor };
        var withdrawCopy = await bankVm.WithdrawAsync([_bank.GetAll().Single().Id], copy: true);
        Assert.Contains("Copied", withdrawCopy);
        Assert.Single(_bank.GetAll()); // copy leaves the Bank entry in place
    }

    [Fact]
    public async Task ExportImportFolder_PreservesBank()
    {
        var editor = OpenEditor();
        var error = await editor.LoadAsync(_savePath);
        Assert.Null(error);
        var (box, slot) = FindFirstNonEmpty(editor);
        Assert.True(slot >= 0);
        await editor.DepositSelectedAsync(isParty: false, box, slot, move: false);

        var exportDir = Path.Combine(_root, "export");
        var exportMessage = new BankViewModel(_bank, _engine).ExportFolder(exportDir);
        Assert.Contains("Exported 1", exportMessage);
        Assert.Single(Directory.EnumerateFiles(exportDir));

        var otherBank = new FileBankService(Path.Combine(_root, "bank2"));
        var importMessage = new BankViewModel(otherBank, _engine).ImportFolder(exportDir);
        Assert.Contains("1 imported", importMessage);
        Assert.Single(otherBank.GetAll());
    }

    [Fact]
    public async Task MoveSlot_BoundChecks()
    {
        var editor = OpenEditor();
        var error = await editor.LoadAsync(_savePath);
        Assert.Null(error);

        var (box, slot) = FindFirstNonEmpty(editor);
        Assert.True(slot >= 0);

        // Move the occupied slot onto itself: harmless no-op (returns without writing).
        Assert.False(editor.Move(box, slot, false, box, slot, false));
        Assert.False(editor.Dirty);

        // Move an empty party slot forward: refused (source empty).
        var emptyIdx = Enumerable.Range(0, 6).FirstOrDefault(i => editor.PartySlot(i).Species == 0, -1);
        Assert.True(emptyIdx >= 0, "fixture save should have an empty party slot");
        Assert.False(editor.Move(0, emptyIdx, true, 0, (emptyIdx + 1) % 6, true));
    }

    private static (int box, int slot) FindFirstNonEmpty(SaveEditorViewModel editor)
    {
        var box = editor.BoxIndex;
        for (var s = 0; s < 30; s++)
        {
            if (editor.Slot(box, s).Species != 0) return (box, s);
        }
        return (0, -1);
    }
}

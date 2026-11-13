using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using PKBank.App.Services;
using PKBank.App.ViewModels;
using PKBank.App.Views;
using PKForge.Domain;
using PKForge.Engine;
using PKForge.Infrastructure;
using PKHeX.Core;
using Xunit;

namespace PKBank.App.Smoke.Tests;

/// <summary>
/// The click path of the save editor: selecting an entity must build the summary (and the
/// Pok�mon editor) without ever throwing, no matter how strange the entity's fields are.
/// A real save's data can be anything the game (or an external editor) put there, so the
/// view degrades to a safe value instead of taking the process down.
/// </summary>
public sealed class EditorInteractionTests : IDisposable
{
    /// <summary>The view decodes sprites, so the Avalonia platform must exist (headless, Skia-backed).</summary>
    private static void EnsurePlatform() => HeadlessTestPlatform.Ensure();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkbank-edit-" + Guid.NewGuid().ToString("N"));
    private readonly string _savePath;
    private readonly FileBankService _bank;
    private readonly FileBackupService _backups;
    private readonly SafeSaveWriter _writer;
    private readonly SaveEngine _engine = new();
    private readonly SpriteService _sprites = new();

    public EditorInteractionTests()
    {
        EnsurePlatform();
        Directory.CreateDirectory(_root);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PKBank.slnx")))
            directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "Tests", "PKHeX.Core.Tests", "TestData", "SM Project 802.main");
        Assert.True(File.Exists(fixture), "Fixture missing: " + fixture);
        _savePath = Path.Combine(_root, "clicked.main");
        File.Copy(fixture, _savePath, overwrite: true);
        _bank = new FileBankService(Path.Combine(_root, "bank"));
        _backups = new FileBackupService(Path.Combine(_root, "backups"));
        _writer = new SafeSaveWriter(_engine, _backups, new DirectAccess());
    }

    private sealed class DirectAccess : ISaveFileAccess
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

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SaveEditorViewModel OpenEditor()
    {
        var vm = new SaveEditorViewModel(_backups, _writer, _bank, _engine);
        Assert.Null(vm.LoadAsync(_savePath).GetAwaiter().GetResult());
        return vm;
    }

    private static (int Box, int Slot) FirstEntity(SaveEditorViewModel vm)
    {
        for (var b = 0; b < vm.Save!.BoxCount; b++)
            for (var s = 0; s < vm.Save.BoxSlotCount; s++)
                if (vm.Slot(b, s).Species != 0)
                    return (b, s);
        Assert.Fail("fixture save has no entity");
        return (0, 0);
    }

    [Fact]
    public void ClickingAnEntityBuildsTheSummary()
    {
        var vm = OpenEditor();
        var view = new SaveEditorView(vm, _sprites);
        var (box, slot) = FirstEntity(vm);
        var error = Record.Exception(() => view.OnCellPressed((false, box, slot), new PointerPressedEventArgs(null!, null!, null!, new Avalonia.Point(0, 0), 0, PointerPointProperties.None, KeyModifiers.None)));
        Assert.Null(error);
    }

    [Fact]
    public void ClickingAPoisonedEntitySurvives()
    {
        var vm = OpenEditor();
        var view = new SaveEditorView(vm, _sprites);
        var (box, slot) = FirstEntity(vm);
        var pk = vm.Slot(box, slot);
        // A save is user-writable data: anything can sit in a slot. Force impossible fields.
        pk.Move1 = 999;
        pk.Move2 = 888;
        pk.Move3 = 777;
        pk.Move4 = 666;
        pk.Ability = 999;
        pk.HeldItem = 9999;
        pk.Ball = 99;
        pk.Nature = (Nature)255;
        vm.Save!.SetBoxSlotAtIndex(pk, box, slot, EntityImportSettings.None);
        var error = Record.Exception(() => view.OnCellPressed((false, box, slot), new PointerPressedEventArgs(null!, null!, null!, new Avalonia.Point(0, 0), 0, PointerPointProperties.None, KeyModifiers.None)));
        Assert.Null(error);
    }
}



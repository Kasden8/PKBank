using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PKBank.App.Services;
using PKBank.App.Services.Pokepark;
using PKForge.Domain;
using PKForge.Engine;
using PKForge.Infrastructure;

namespace PKBank.App;

public sealed class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = BuildServices();
        AppLog.Info("app", "Services wired");
        // Release check (best-effort, never blocks startup on the network).
        _ = Task.Run(async () =>
        {
            try
            {
                var update = await Services.GetRequiredService<AppUpdateService>().CheckAsync().ConfigureAwait(false);
                AppLog.Info("update", update is { } u ? $"Newer build available: {u.Title}" : "No updates in feed.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
        });
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Views.MainWindow();
            // A deep link: "PKBank.exe --view park" opens straight into that screen.
            var args = desktop.Args ?? [];
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (!args[i].Equals("--view", StringComparison.OrdinalIgnoreCase)) continue;
                if (desktop.MainWindow is Views.MainWindow window)
                {
                    window.ShowView(args[i + 1]);
                    AppLog.Info("app", "Opened view " + args[i + 1]);
                }
                break;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServices()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKBank");
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddSingleton<SaveEngine>();
        services.AddSingleton<ISaveEngine>(provider => provider.GetRequiredService<SaveEngine>());
        services.AddSingleton<IGameDataService, GameDataService>();
        services.AddSingleton<ILegalizerService, LegalizerService>();
        services.AddSingleton<IEventDatabaseService, EventDatabaseService>();
        services.AddSingleton<IEncounterLookup, EncounterLookupService>();
        services.AddSingleton<IBackupService>(_ => new FileBackupService(Path.Combine(dataDir, "backups")));
        services.AddSingleton<IBankService>(provider =>
        {
            var bank = new FileBankService(Path.Combine(dataDir, "bank"));
            // One-time index migration off the UI thread: legacy entries get their exact format
            // (only when provable) and sprite traits recorded; retried next launch on failure.
            _ = Task.Run(() =>
            {
                try { PKForge.Engine.EntityBytes.MigrateBank(bank); }
                catch (Exception) { /* the index stays as it was; retried next launch */ }
            });
            return bank;
        });
        services.AddSingleton(_ => new InjectedGiftHistory(Path.Combine(dataDir, "injected-gifts.json")));
        services.AddSingleton<ISaveIdentityStore>(_ => new JsonSaveIdentityStore(Path.Combine(dataDir, "save-identities.json")));
        services.AddSingleton<ISaveFileAccess, LocalFileAccess>();
        services.AddSingleton<ISafeSaveWriter, SafeSaveWriter>();
        services.AddSingleton<ILegalityService, LegalityService>();
        services.AddSingleton<ILegalityAssistService>(LegalityAssistService.Shared);
        services.AddSingleton<IStatPreviewService, StatPreviewService>();
        services.AddSingleton<IMonInfoService, MonInfoService>();
        services.AddSingleton<IMonSummaryService, MonSummaryService>();
        services.AddSingleton<IEvolutionService, EvolutionService>();

        services.AddSingleton<SpriteService>();
        services.AddSingleton<PokeparkSpriteService>();
        services.AddSingleton<Services.Pokepark.PokeparkService>(provider => new Services.Pokepark.PokeparkService(
            provider.GetRequiredService<IBankService>(),
            provider.GetRequiredService<IGameDataService>()));
        services.AddSingleton<Services.Pokepark.ParkSession>(provider => new Services.Pokepark.ParkSession(
            provider.GetRequiredService<Services.Pokepark.PokeparkService>()));
        services.AddSingleton<Services.Pokepark.ParkRenderer>(provider => new Services.Pokepark.ParkRenderer(
            provider.GetRequiredService<PokeparkSpriteService>()));
        services.AddSingleton<Services.Pokepark.ParkDaycare>(provider => new Services.Pokepark.ParkDaycare(
            provider.GetRequiredService<Services.Pokepark.PokeparkService>(),
            provider.GetRequiredService<IBankService>()));
        services.AddSingleton<Navigation.Navigator>();
        services.AddSingleton<AppUpdateService>();
        return services.BuildServiceProvider();
    }
}

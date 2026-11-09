using System.IO.Compression;
using System.Text;

namespace PKBank.App.Services;

/// <summary>
/// The app's own diary, kept on this PC so a user can send it when something goes wrong:
/// a rolling log of what the app did (screens, saves opened, scans, writes, errors), and a
/// crash report written the moment the app dies from an unhandled error. Nothing leaves the
/// machine unless the user shares it from Settings. Every write is guarded: logging can never
/// be the thing that breaks the app.
/// </summary>
public static class AppLog
{
    private const long MaxLogBytes = 1 << 20;   // one file of recent activity, one previous
    private const int MaxCrashReports = 5;
    private const int TailLines = 200;          // recent lines copied into a crash report

    private static readonly Lock Gate = new();
    private static readonly Queue<string> Tail = new();
    private static bool _installed;

    private static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKBank");
    private static string Folder => Path.Combine(DataDirectory, "logs");
    private static string CurrentLog => Path.Combine(Folder, "pkbank.log");
    private static string PreviousLog => Path.Combine(Folder, "pkbank.previous.log");

    public static void Info(string area, string message) => Write("INFO", area, message);

    public static void Warn(string area, string message) => Write("WARN", area, message);

    public static void Error(string area, string message, Exception? error = null) =>
        Write("ERROR", area, error is null ? message : $"{message}\n{error}");

    /// <summary>Hooks every way the process can die from an error; call once at startup.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrash("Unhandled .NET exception", args.ExceptionObject as Exception, args.ExceptionObject?.ToString());
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Error("task", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        Info("app", $"Started on {Environment.OSVersion} ({Environment.MachineName})");
    }

    /// <summary>Crash reports from earlier runs that the user has not been told about yet.</summary>
    public static IReadOnlyList<string> UnseenCrashes()
    {
        try
        {
            return Directory.Exists(Folder)
                ? [.. Directory.EnumerateFiles(Folder, "crash-*.txt").Where(f => !File.Exists(f + ".seen")).Order()]
                : [];
        }
        catch (IOException) { return []; }
    }

    public static void MarkCrashesSeen()
    {
        foreach (var crash in UnseenCrashes())
            TryIo(() => File.WriteAllText(crash + ".seen", DateTimeOffset.Now.ToString("O")));
    }

    /// <summary>
    /// Packs the logs, crash reports and the given extra texts (the scan report) into one zip
    /// in the temp folder, ready to share.
    /// </summary>
    public static string BuildShareArchive(IReadOnlyDictionary<string, string> extras)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pkbank-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        lock (Gate)
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            AddText(zip, "device.txt", DeviceDetails());
            if (Directory.Exists(Folder))
            {
                foreach (var file in Directory.EnumerateFiles(Folder).Where(f => !f.EndsWith(".seen", StringComparison.Ordinal)))
                    zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
            }
            foreach (var (name, text) in extras)
                AddText(zip, name, text);
        }
        return path;
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), Encoding.UTF8);
        writer.Write(text);
    }

    private static void Write(string level, string area, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {level,-5} [{area}] {message}";
        lock (Gate)
        {
            Tail.Enqueue(line);
            while (Tail.Count > TailLines) Tail.Dequeue();
            TryIo(() =>
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(CurrentLog) && new FileInfo(CurrentLog).Length > MaxLogBytes)
                    File.Move(CurrentLog, PreviousLog, overwrite: true);
                File.AppendAllText(CurrentLog, line + "\n");
            });
        }
    }

    private static void WriteCrash(string kind, Exception? error, string? fallback)
    {
        lock (Gate)
        {
            TryIo(() =>
            {
                Directory.CreateDirectory(Folder);
                var report = new StringBuilder()
                    .AppendLine($"PKBank crash report — {DateTimeOffset.Now:O}")
                    .AppendLine(kind)
                    .AppendLine()
                    .AppendLine(DeviceDetails())
                    .AppendLine("── Error ──")
                    .AppendLine(error?.ToString() ?? fallback ?? "(no details)")
                    .AppendLine()
                    .AppendLine("── Recent activity ──");
                foreach (var line in Tail) report.AppendLine(line);
                File.WriteAllText(Path.Combine(Folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt"), report.ToString());
                foreach (var old in Directory.EnumerateFiles(Folder, "crash-*.txt").Order().SkipLast(MaxCrashReports))
                {
                    File.Delete(old);
                    File.Delete(old + ".seen");
                }
            });
        }
    }

    private static string DeviceSummary()
        => $"PKBank (.NET {Environment.Version}, {Environment.OSVersion.Platform})";

    private static string DeviceDetails()
        => new StringBuilder().AppendLine(DeviceSummary())
            .AppendLine($"OS: {Environment.OSVersion}")
            .AppendLine($"Machine: {Environment.MachineName}")
            .AppendLine($"User: {Environment.UserName}")
            .ToString();

    private static void TryIo(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}

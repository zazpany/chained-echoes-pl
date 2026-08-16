using System.Diagnostics;
using System.Text.Json;

namespace ChainedEchoesPolishInstaller.Core;

public sealed record FileSnapshot(string RelativePath, string Sha256, long Size);

public sealed record BackupState(
    string PackageId,
    IReadOnlyDictionary<string, FileSnapshot> Files);

public sealed class InstallationEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly InstallerPackage package;
    private readonly IPayloadProvider payloads;
    private readonly Action<string> log;
    private readonly Func<bool> isGameRunning;

    public InstallationEngine(
        InstallerPackage package,
        IPayloadProvider payloads,
        Action<string>? log = null,
        Func<bool>? isGameRunning = null)
    {
        this.package = package;
        this.payloads = payloads;
        this.log = log ?? (_ => { });
        this.isGameRunning = isGameRunning ?? DefaultGameRunningCheck;
    }

    public bool Verify(string gameDirectory)
    {
        ValidateGameDirectory(gameDirectory);
        ValidatePayloads();
        var current = SnapshotTargets(gameDirectory);
        var allValid = true;
        foreach (var file in package.Files)
        {
            var actual = current[file.RelativePath];
            var valid = file.MatchesPatched(actual);
            log($"{(valid ? "OK" : "NIEZGODNY"),-9} {file.RelativePath}");
            allValid &= valid;
        }

        return allValid;
    }

    public void Install(string gameDirectory)
    {
        ValidateGameDirectory(gameDirectory);
        EnsureGameClosed();
        ValidatePayloads();
        var current = SnapshotTargets(gameDirectory);

        if (package.Files.All(file => file.MatchesPatched(current[file.RelativePath])))
        {
            if (!Directory.Exists(BackupDirectory(gameDirectory)))
            {
                throw new InvalidOperationException(
                    "Pliki patcha są obecne, ale brakuje jego zweryfikowanego backupu. "
                    + "Przywróć czyste pliki przez Steam i uruchom instalator ponownie.");
            }

            ValidateBackup(gameDirectory, LoadState(gameDirectory));
            log("Ta wersja patcha jest już poprawnie zainstalowana.");
            if (!Verify(gameDirectory))
            {
                throw new InvalidOperationException(
                    "Weryfikacja istniejącej instalacji nie powiodła się.");
            }

            return;
        }

        var notClean = package.Files
            .Where(file => !file.MatchesSource(current[file.RelativePath]))
            .Select(file =>
                $"  - {file.RelativePath}: {DescribeState(file, current[file.RelativePath])}")
            .ToArray();
        if (notClean.Length > 0)
        {
            throw new InvalidDataException(
                "Instalacja wymaga dokładnie czystego klienta Steam build "
                + package.SupportedSteamBuild
                + ". Wykryto wcześniejszy patch, mieszaną instalację albo inną wersję:\n"
                + string.Join("\n", notClean)
                + "\nUżyj w Steam: Właściwości → Zainstalowane pliki → Sprawdź spójność plików.");
        }

        log($"Czysty klient Steam build {package.SupportedSteamBuild}: OK");
        var state = EnsureBackup(gameDirectory, current);
        try
        {
            foreach (var file in package.Files.OrderBy(file => file.InstallLast))
            {
                InstallFile(gameDirectory, file);
            }

            if (!Verify(gameDirectory))
            {
                throw new InvalidOperationException(
                    "Weryfikacja pełnego patcha po instalacji nie powiodła się.");
            }

            log("Pełny patch EF-001 został zainstalowany pomyślnie.");
        }
        catch (Exception installError)
        {
            log($"Instalacja nie powiodła się: {installError.Message}");
            log("Przywracam czysty klient z backupu...");
            try
            {
                RestoreFromState(gameDirectory, state);
                VerifyClean(gameDirectory);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "Instalacja i automatyczny rollback nie powiodły się.",
                    installError,
                    rollbackError);
            }

            throw new InvalidOperationException(
                "Instalacja nie powiodła się; czysty klient został przywrócony.",
                installError);
        }
    }

    public void Rollback(string gameDirectory)
    {
        ValidateGameDirectory(gameDirectory);
        EnsureGameClosed();
        var state = LoadState(gameDirectory);
        ValidateBackup(gameDirectory, state);
        var current = SnapshotTargets(gameDirectory);
        foreach (var file in package.Files)
        {
            var actual = current[file.RelativePath];
            if (!file.MatchesSource(actual) && !file.MatchesPatched(actual))
            {
                throw new InvalidOperationException(
                    "Plik został zmieniony przez inny program; rollback przerwany: "
                    + file.RelativePath);
            }
        }

        RestoreFromState(gameDirectory, state);
        VerifyClean(gameDirectory);
        log("Przywrócono dokładny czysty klient. Backup pozostawiono do audytu.");
    }

    private void ValidatePayloads()
    {
        foreach (var file in package.Files)
        {
            using var stream = payloads.OpenRead(file.PayloadName);
            var observed = Hashing.Sha256(stream);
            if (!string.Equals(
                    observed.Sha256,
                    file.PatchedSha256,
                    StringComparison.OrdinalIgnoreCase)
                || observed.Length != file.PatchedSize)
            {
                throw new InvalidDataException(
                    $"Uszkodzony payload instalatora: {file.PayloadName}");
            }
        }

        log($"Payloady instalatora ({package.Files.Count}): OK");
    }

    private IReadOnlyDictionary<string, FileSnapshot> SnapshotTargets(
        string gameDirectory)
    {
        var snapshots = new Dictionary<string, FileSnapshot>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in package.Files)
        {
            var target = TargetPath(gameDirectory, file);
            if (!File.Exists(target))
            {
                throw new FileNotFoundException(
                    $"Brak pliku gry: {file.RelativePath}", target);
            }

            var info = new FileInfo(target);
            snapshots[file.RelativePath] = new FileSnapshot(
                file.RelativePath,
                Hashing.Sha256File(target),
                info.Length);
        }

        return snapshots;
    }

    private BackupState EnsureBackup(
        string gameDirectory,
        IReadOnlyDictionary<string, FileSnapshot> current)
    {
        var backup = BackupDirectory(gameDirectory);
        if (Directory.Exists(backup))
        {
            var existing = LoadState(gameDirectory);
            ValidateBackup(gameDirectory, existing);
            log("Używam istniejącego, zweryfikowanego backupu czystego klienta.");
            return existing;
        }

        var temporary = backup + $".creating.{Environment.ProcessId}.{Guid.NewGuid():N}";
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var snapshot in current.Values)
            {
                var source = Path.Combine(
                    gameDirectory,
                    snapshot.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var target = Path.Combine(
                    temporary,
                    "files",
                    snapshot.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Hashing.AtomicCopy(source, target);
            }

            var state = new BackupState(package.PackageId, current);
            var stateBytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
            using (var stateStream = new MemoryStream(stateBytes))
            {
                Hashing.AtomicWrite(stateStream, Path.Combine(temporary, "state.json"));
            }

            Directory.Move(temporary, backup);
            ValidateBackup(gameDirectory, state);
            log($"Utworzono pełny backup: {backup}");
            return state;
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private BackupState LoadState(string gameDirectory)
    {
        var path = Path.Combine(BackupDirectory(gameDirectory), "state.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Nie znaleziono stanu rollbacku.", path);
        }

        var state = JsonSerializer.Deserialize<BackupState>(
            File.ReadAllBytes(path), JsonOptions)
            ?? throw new InvalidDataException("Nieprawidłowy stan rollbacku.");
        if (!string.Equals(state.PackageId, package.PackageId, StringComparison.Ordinal)
            || state.Files is null)
        {
            throw new InvalidDataException("Backup pochodzi z innego pakietu.");
        }

        return state;
    }

    private void ValidateBackup(string gameDirectory, BackupState state)
    {
        if (state.Files.Count != package.Files.Count)
        {
            throw new InvalidDataException("Backup ma nieprawidłową liczbę plików.");
        }

        foreach (var file in package.Files)
        {
            if (!state.Files.TryGetValue(file.RelativePath, out var snapshot)
                || !file.MatchesSource(snapshot))
            {
                throw new InvalidDataException(
                    $"Backup nie opisuje czystego pliku: {file.RelativePath}");
            }

            var backupFile = Path.Combine(
                BackupDirectory(gameDirectory),
                "files",
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(backupFile)
                || new FileInfo(backupFile).Length != snapshot.Size
                || !string.Equals(
                    Hashing.Sha256File(backupFile),
                    snapshot.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Uszkodzony backup: {file.RelativePath}");
            }
        }
    }

    private void InstallFile(string gameDirectory, PackageFile file)
    {
        var target = TargetPath(gameDirectory, file);
        using var payload = payloads.OpenRead(file.PayloadName);
        Hashing.AtomicWrite(payload, target);
        var snapshot = new FileSnapshot(
            file.RelativePath,
            Hashing.Sha256File(target),
            new FileInfo(target).Length);
        if (!file.MatchesPatched(snapshot))
        {
            throw new IOException(
                $"Weryfikacja zapisanego pliku nie powiodła się: {file.RelativePath}");
        }

        log($"Zainstalowano: {file.RelativePath}");
    }

    private void RestoreFromState(string gameDirectory, BackupState state)
    {
        ValidateBackup(gameDirectory, state);
        foreach (var file in package.Files.OrderByDescending(file => file.InstallLast))
        {
            var source = Path.Combine(
                BackupDirectory(gameDirectory),
                "files",
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var target = TargetPath(gameDirectory, file);
            Hashing.AtomicCopy(source, target);
            var restored = new FileSnapshot(
                file.RelativePath,
                Hashing.Sha256File(target),
                new FileInfo(target).Length);
            if (!file.MatchesSource(restored))
            {
                throw new IOException(
                    $"Weryfikacja rollbacku nie powiodła się: {file.RelativePath}");
            }

            log($"Przywrócono: {file.RelativePath}");
        }
    }

    private void VerifyClean(string gameDirectory)
    {
        var current = SnapshotTargets(gameDirectory);
        var invalid = package.Files
            .Where(file => !file.MatchesSource(current[file.RelativePath]))
            .Select(file => file.RelativePath)
            .ToArray();
        if (invalid.Length > 0)
        {
            throw new IOException(
                "Nie udało się przywrócić czystego klienta: "
                + string.Join(", ", invalid));
        }

        log("Czysty klient po rollbacku: OK");
    }

    private static string DescribeState(PackageFile file, FileSnapshot snapshot)
    {
        if (file.MatchesPatched(snapshot))
        {
            return "plik z wcześniejszego patcha EF-001";
        }

        return $"nieznany SHA-256 {snapshot.Sha256}, rozmiar {snapshot.Size}";
    }

    private void ValidateGameDirectory(string gameDirectory)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Katalog gry nie istnieje: {gameDirectory}");
        }

        var windowsLayout = package.Files.Any(file => file.RelativePath.Contains(
            "/StandaloneWindows64/",
            StringComparison.Ordinal));
        var linuxLayout = package.Files.Any(file => file.RelativePath.Contains(
            "/StandaloneLinux64/",
            StringComparison.Ordinal));
        if (windowsLayout == linuxLayout)
        {
            throw new InvalidDataException(
                "Manifest instalatora nie wskazuje dokładnie jednej platformy docelowej.");
        }

        var executable = windowsLayout ? "Chained Echoes.exe" : "Chained_Echoes.x86_64";
        if (!File.Exists(Path.Combine(gameDirectory, executable)))
        {
            missing.Add(executable);
        }

        var database = package.Files.SingleOrDefault(file => file.RelativePath.EndsWith(
            "/bansheegz_database.bytes",
            StringComparison.OrdinalIgnoreCase))?.RelativePath
            ?? throw new InvalidDataException(
                "Manifest instalatora nie zawiera docelowej ścieżki BGDatabase.");
        if (!File.Exists(Path.Combine(
            gameDirectory,
            database.Replace('/', Path.DirectorySeparatorChar))))
        {
            missing.Add(database);
        }

        if (missing.Count > 0)
        {
            throw new DirectoryNotFoundException(
                "Wybrany katalog nie jest kompletną instalacją Chained Echoes. Brakuje:\n  - "
                + string.Join("\n  - ", missing));
        }
    }

    private void EnsureGameClosed()
    {
        if (isGameRunning())
        {
            throw new InvalidOperationException(
                "Chained Echoes jest uruchomione. Zamknij grę i spróbuj ponownie.");
        }
    }

    private static bool DefaultGameRunningCheck()
    {
        string[] names =
        [
            "Chained Echoes",
            "Chained_Echoes",
            "ChainedEchoes",
            "Chained_Echoes.x86_64",
        ];
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (names.Contains(
                        process.ProcessName,
                        StringComparer.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Proces zakończył się w trakcie odczytu.
                }
            }
        }

        return false;
    }

    private string BackupDirectory(string gameDirectory) =>
        Path.Combine(gameDirectory, $".{package.PackageId}-backup");

    private static string TargetPath(string gameDirectory, PackageFile file) =>
        Path.Combine(
            gameDirectory,
            file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
}

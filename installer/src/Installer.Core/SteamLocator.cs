using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ChainedEchoesPolishInstaller.Core;

public static partial class SteamLocator
{
    private const string SteamAppId = "1229240";

    public static string? FindGameDirectory()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            AddWindowsSteamRoots(steamRoots);
        }
        else if (OperatingSystem.IsLinux())
        {
            AddLinuxSteamRoots(steamRoots);
        }

        return FindGameDirectoryFromSteamRoots(steamRoots);
    }

    public static string? FindGameDirectoryFromSteamRoots(IEnumerable<string> steamRoots)
    {
        ArgumentNullException.ThrowIfNull(steamRoots);
        var roots = new HashSet<string>(
            steamRoots.Where(path => !string.IsNullOrWhiteSpace(path)),
            StringComparer.OrdinalIgnoreCase);
        var libraries = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in roots)
        {
            var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(libraryFile);
                foreach (Match match in LibraryPathRegex().Matches(content))
                {
                    var path = match.Groups["path"].Value.Replace(@"\\", @"\");
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        libraries.Add(path);
                    }
                }
            }
            catch (IOException)
            {
                // A locked library file is skipped; manual selection remains available.
            }
            catch (UnauthorizedAccessException)
            {
                // Manual selection remains available.
            }
        }

        foreach (var library in libraries)
        {
            if (TryResolveSteamLibrary(library, out var resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    public static string ResolveGameDirectory(string input)
    {
        if (TryResolveGameDirectory(input, out var resolved, out var problem))
        {
            return resolved!;
        }

        throw new DirectoryNotFoundException(problem);
    }

    public static bool TryResolveGameDirectory(
        string? input,
        out string? gameDirectory,
        out string problem)
    {
        gameDirectory = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            problem = "Nie podano ścieżki.";
            return false;
        }

        var problemDetails = new List<string>();
        foreach (var pastedValue in PastedPathCandidates(input))
        {
            if (TryResolveCandidate(pastedValue, out gameDirectory, out var candidateProblem))
            {
                problem = string.Empty;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(candidateProblem))
            {
                problemDetails.Add(candidateProblem);
            }
        }

        problem = problemDetails.FirstOrDefault()
            ?? "Nie znaleziono katalogu Chained_Echoes_Data w podanej ścieżce ani nad nią.";
        problem += " Spacje są obsługiwane; nie trzeba zamieniać ich na podkreślenia.";
        return false;
    }

    internal static IReadOnlyList<string> PastedPathCandidates(string input)
    {
        var value = input.Trim().Trim('"', '\'', '“', '”', '„', '‘', '’').Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            value = uri.LocalPath;
        }

        var candidates = new List<string> { value };
        var repaired = DataDirectoryAliasRegex().Replace(value, "$1Chained_Echoes_Data");
        repaired = GameDirectoryAliasRegex().Replace(repaired, "$1Chained Echoes");
        if (!string.Equals(repaired, value, StringComparison.Ordinal))
        {
            candidates.Add(repaired);
        }

        return candidates;
    }

    private static bool TryResolveCandidate(
        string value,
        out string? gameDirectory,
        out string problem)
    {
        gameDirectory = null;
        try
        {
            value = Path.GetFullPath(value);
        }
        catch (Exception error) when (error is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            problem = $"Nieprawidłowa ścieżka: {error.Message}";
            return false;
        }

        if (File.Exists(value))
        {
            value = Path.GetDirectoryName(value) ?? value;
        }

        var current = new DirectoryInfo(value);
        for (var depth = 0; current is not null && depth < 10; depth++, current = current.Parent)
        {
            var candidate = current.Name.Equals(
                "Chained_Echoes_Data",
                StringComparison.OrdinalIgnoreCase)
                ? current.Parent?.FullName
                : current.FullName;
            if (candidate is not null && HasDataDirectory(candidate))
            {
                gameDirectory = Path.GetFullPath(candidate);
                problem = string.Empty;
                return true;
            }

            if (TryResolveSteamLibrary(current.FullName, out gameDirectory))
            {
                problem = string.Empty;
                return true;
            }
        }

        problem = "Nie znaleziono instalacji Chained Echoes (Steam App 1229240) "
            + "ani katalogu Chained_Echoes_Data w podanej ścieżce lub bibliotece Steam.";
        return false;
    }

    private static bool TryResolveSteamLibrary(string value, out string? gameDirectory)
    {
        gameDirectory = null;
        string libraryRoot;
        var directory = new DirectoryInfo(value);
        if (directory.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
        {
            libraryRoot = directory.Parent?.FullName ?? value;
        }
        else if (directory.Name.Equals("common", StringComparison.OrdinalIgnoreCase)
            && directory.Parent?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) is true)
        {
            libraryRoot = directory.Parent.Parent?.FullName ?? value;
        }
        else
        {
            libraryRoot = value;
        }

        var steamApps = Path.Combine(libraryRoot, "steamapps");
        if (!Directory.Exists(steamApps))
        {
            return false;
        }

        var manifestPath = Path.Combine(steamApps, $"appmanifest_{SteamAppId}.acf");
        if (File.Exists(manifestPath))
        {
            try
            {
                var content = File.ReadAllText(manifestPath);
                var match = InstallDirectoryRegex().Match(content);
                if (match.Success)
                {
                    var installDirectory = match.Groups["installdir"].Value.Trim();
                    if (IsSafeInstallDirectoryName(installDirectory))
                    {
                        var candidate = Path.Combine(steamApps, "common", installDirectory);
                        if (HasDataDirectory(candidate))
                        {
                            gameDirectory = Path.GetFullPath(candidate);
                            return true;
                        }
                    }
                }
            }
            catch (IOException)
            {
                // The caller can still use the conventional directory fallback.
            }
            catch (UnauthorizedAccessException)
            {
                // The caller can still use the conventional directory fallback.
            }
        }

        var conventional = Path.Combine(steamApps, "common", "Chained Echoes");
        if (!HasDataDirectory(conventional))
        {
            return false;
        }

        gameDirectory = Path.GetFullPath(conventional);
        return true;
    }

    private static bool IsSafeInstallDirectoryName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value is not "." and not ".."
        && !Path.IsPathRooted(value)
        && value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;

    public static string DetectAssetPlatform(string gameDirectory)
    {
        var root = Path.Combine(
            gameDirectory,
            "Chained_Echoes_Data",
            "StreamingAssets",
            "aa");
        var windows = Directory.Exists(Path.Combine(root, "StandaloneWindows64"));
        var linux = Directory.Exists(Path.Combine(root, "StandaloneLinux64"));
        if (OperatingSystem.IsWindows() && windows)
        {
            return "windows";
        }

        if (OperatingSystem.IsLinux() && linux)
        {
            return "linux";
        }

        if (windows)
        {
            return "windows";
        }

        if (linux)
        {
            return "linux";
        }

        throw new DirectoryNotFoundException(
            "Nie znaleziono ani StreamingAssets/aa/StandaloneWindows64, "
            + "ani StreamingAssets/aa/StandaloneLinux64. Instalator niczego nie zmienił.");
    }

    private static bool HasDataDirectory(string path) =>
        Directory.Exists(Path.Combine(path, "Chained_Echoes_Data"));

    [SupportedOSPlatform("windows")]
    private static void AddRegistrySteamPath(
        ISet<string> roots,
        RegistryKey hive,
        string keyPath,
        string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key?.GetValue(valueName) is string path && !string.IsNullOrWhiteSpace(path))
            {
                roots.Add(path.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Manual selection remains available.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsSteamRoots(ISet<string> roots)
    {
        AddRegistrySteamPath(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddRegistrySteamPath(roots, Registry.LocalMachine, @"Software\Valve\Steam", "InstallPath");
        AddRegistrySteamPath(
            roots,
            Registry.LocalMachine,
            @"Software\WOW6432Node\Valve\Steam",
            "InstallPath");

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            roots.Add(Path.Combine(programFilesX86, "Steam"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            roots.Add(Path.Combine(programFiles, "Steam"));
        }
    }

    private static void AddLinuxSteamRoots(ISet<string> roots)
    {
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userHome))
        {
            return;
        }

        string[] relativeRoots =
        [
            ".steam/steam",
            ".steam/debian-installation",
            ".local/share/Steam",
            ".var/app/com.valvesoftware.Steam/.local/share/Steam",
        ];
        foreach (var relativeRoot in relativeRoots)
        {
            var candidate = Path.Combine(
                userHome,
                relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(candidate))
            {
                roots.Add(candidate);
            }
        }
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();

    [GeneratedRegex("\\\"installdir\\\"\\s+\\\"(?<installdir>[^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirectoryRegex();

    [GeneratedRegex(@"(^|[\\/])Chained[ _]Echoes[ _]Data(?=$|[\\/])", RegexOptions.IgnoreCase)]
    private static partial Regex DataDirectoryAliasRegex();

    [GeneratedRegex(@"(^|[\\/])Chained_Echoes(?=$|[\\/])", RegexOptions.IgnoreCase)]
    private static partial Regex GameDirectoryAliasRegex();
}

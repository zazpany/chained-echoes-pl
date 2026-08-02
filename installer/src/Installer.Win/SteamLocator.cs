using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ChainedEchoesPolishInstaller;

internal static partial class SteamLocator
{
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

        var libraries = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in steamRoots.ToArray())
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
            var candidate = Path.Combine(library, "steamapps", "common", "Chained Echoes");
            if (TryResolveGameDirectory(candidate, out var resolved, out _))
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

        var value = input.Trim().Trim('"', '\'');
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
        }

        problem = "Nie znaleziono katalogu Chained_Echoes_Data w podanej ścieżce ani nad nią.";
        return false;
    }

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
}

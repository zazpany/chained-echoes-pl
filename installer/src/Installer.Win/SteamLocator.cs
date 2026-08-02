using System.Text.RegularExpressions;
using System.Runtime.Versioning;
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
            if (IsGameDirectory(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public static bool IsGameDirectory(string path) =>
        File.Exists(Path.Combine(
            path,
            OperatingSystem.IsWindows()
                ? "Chained Echoes.exe"
                : "Chained_Echoes.x86_64"))
        && Directory.Exists(Path.Combine(
            path,
            "Chained_Echoes_Data",
            "StreamingAssets",
            "aa",
            OperatingSystem.IsWindows()
                ? "StandaloneWindows64"
                : "StandaloneLinux64"))
        && File.Exists(Path.Combine(
            path,
            "Chained_Echoes_Data",
            "StreamingAssets",
            "aa",
            "catalog.json"))
        && File.Exists(Path.Combine(
            path,
            "Chained_Echoes_Data",
            "StreamingAssets",
            "bansheegz_database.bytes"));

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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChainedEchoesPolishInstaller.Core;

public sealed record PackageFile(
    string RelativePath,
    string PayloadName,
    string Role,
    string SourceSha256,
    long SourceSize,
    string PatchedSha256,
    long PatchedSize,
    bool InstallLast = false)
{
    public bool MatchesSource(FileSnapshot snapshot) =>
        snapshot.Size == SourceSize
        && string.Equals(snapshot.Sha256, SourceSha256, StringComparison.OrdinalIgnoreCase);

    public bool MatchesPatched(FileSnapshot snapshot) =>
        snapshot.Size == PatchedSize
        && string.Equals(snapshot.Sha256, PatchedSha256, StringComparison.OrdinalIgnoreCase);
}

public sealed record TranslationScope(
    int DialogueFields,
    string DialogueConversations,
    int DatabaseSelectedFields,
    int DatabaseChangedFields,
    int DatabaseIdenticalFields,
    IReadOnlyDictionary<string, int> PerTable);

public sealed record InstallerPackage(
    string PackageId,
    string DisplayName,
    string Version,
    string Status,
    string SupportedSteamBuild,
    TranslationScope TranslationScope,
    IReadOnlyList<PackageFile> Files);

public static class ReleaseManifestLoader
{
    public const string SupportedSchema =
        "echoforge.chained-echoes-cumulative-release/v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static InstallerPackage Load(Stream stream, string? targetPlatform = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        targetPlatform ??= OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsLinux()
                ? "linux"
                : throw new PlatformNotSupportedException(
                    "Instalator obsługuje tylko Windows x64 i Linux x64.");
        Require(targetPlatform is "windows" or "linux",
            "Nieobsługiwana platforma docelowa manifestu.");
        ReleaseManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReleaseManifest>(stream, JsonOptions)
                ?? throw new InvalidDataException("Manifest wydania jest pusty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Manifest wydania ma nieprawidłowy format JSON.", error);
        }

        Require(manifest.Schema == SupportedSchema, "Nieobsługiwany schemat manifestu.");
        RequireSafeIdentifier(manifest.ReleaseId, "release_id");
        RequireVersion(manifest.Version);
        RequireText(manifest.Status, "status");
        Require(manifest.Game is not null, "Brak sekcji game.");
        Require(manifest.Game!.SteamAppId == 1_229_240, "Manifest dotyczy innej gry.");
        RequireText(manifest.Game.Name, "game.name");
        RequireDigits(manifest.Game.SteamBuildId, "game.steam_build_id");
        Require(manifest.TranslationScope is not null, "Brak translation_scope.");
        Require(manifest.Files is { Count: > 0 }, "Manifest nie zawiera plików.");
        ValidateInstallerContract(manifest.InstallerContract);

        var scope = ParseScope(manifest.TranslationScope!);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var platformPaths = new Dictionary<string, HashSet<string>>
        {
            ["windows"] = new(StringComparer.OrdinalIgnoreCase),
            ["linux"] = new(StringComparer.OrdinalIgnoreCase),
        };
        var payloadNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<PackageFile>(manifest.Files!.Count);
        foreach (var entry in manifest.Files)
        {
            var (relativePath, targetPaths) = SelectAndValidateTargetPaths(
                entry.TargetPaths,
                targetPlatform);
            RequireSafePayloadName(entry.PayloadName);
            RequireText(entry.Role, "files[].role");
            RequireSha256(entry.SourceSha256, "files[].source_sha256");
            RequireSha256(entry.OutputSha256, "files[].output_sha256");
            Require(entry.SourceSize > 0, "source_size musi być dodatni.");
            Require(entry.OutputSize > 0, "output_size musi być dodatni.");
            Require(paths.Add(relativePath), "Powtórzona ścieżka docelowa w manifeście.");
            foreach (var target in targetPaths)
            {
                Require(platformPaths[target.Key].Add(target.Value),
                    $"Powtórzona ścieżka docelowa dla platformy {target.Key}.");
            }
            Require(payloadNames.Add(entry.PayloadName!), "Powtórzona nazwa payloadu w manifeście.");
            files.Add(new PackageFile(
                relativePath,
                entry.PayloadName!,
                entry.Role!,
                entry.SourceSha256!.ToLowerInvariant(),
                entry.SourceSize,
                entry.OutputSha256!.ToLowerInvariant(),
                entry.OutputSize,
                entry.InstalledLast));
        }

        Require(files.Count(file => file.InstallLast) <= 1,
            "Co najwyżej jeden plik może mieć installed_last=true.");

        return new InstallerPackage(
            manifest.ReleaseId!,
            $"{manifest.Game.Name} — polska wersja {manifest.Version}",
            manifest.Version!,
            manifest.Status!,
            manifest.Game.SteamBuildId!,
            scope,
            files);
    }

    private static TranslationScope ParseScope(ReleaseTranslationScope scope)
    {
        Require(scope.DialogueFields >= 0, "dialogue_fields nie może być ujemne.");
        RequireText(scope.DialogueConversations, "dialogue_conversations");
        Require(scope.DatabaseSelectedFields >= 0, "database_selected_fields nie może być ujemne.");
        Require(scope.DatabaseChangedFields >= 0, "database_changed_fields nie może być ujemne.");
        Require(scope.DatabaseIdenticalFields >= 0, "database_identical_fields nie może być ujemne.");
        Require(
            scope.DatabaseSelectedFields
                == scope.DatabaseChangedFields + scope.DatabaseIdenticalFields,
            "database_selected_fields != database_changed_fields + database_identical_fields.");
        var perTable = scope.PerTable
            ?? throw new InvalidDataException("Brak liczników per_table.");
        Require(perTable.Count > 0, "Brak liczników per_table.");
        Require(perTable.All(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) && pair.Value >= 0),
            "Nieprawidłowy licznik per_table.");
        Require(perTable.Values.Sum() == scope.DatabaseSelectedFields,
            "Suma per_table nie zgadza się z database_selected_fields.");

        return new TranslationScope(
            scope.DialogueFields,
            scope.DialogueConversations!,
            scope.DatabaseSelectedFields,
            scope.DatabaseChangedFields,
            scope.DatabaseIdenticalFields,
            new Dictionary<string, int>(perTable, StringComparer.Ordinal));
    }

    private static void ValidateInstallerContract(ReleaseInstallerContract? contract)
    {
        Require(contract is not null, "Brak installer_contract.");
        Require(contract!.CleanClientOnly
            && contract.GameMustBeStopped
            && contract.BackupAllReplacedFiles
            && contract.AtomicSiblingReplacement
            && contract.VerifyAfterInstall
            && contract.AutomaticRollbackOnFailure
            && contract.IndependentVerify
            && contract.IndependentUninstall
            && contract.UnknownOrMixedClientIsTerminal,
            "Manifest osłabia wymagany kontrakt bezpieczeństwa instalatora.");
    }

    private static void RequireSafeRelativePath(string? value)
    {
        RequireText(value, "files[].relative_path");
        Require(!value!.StartsWith('/')
            && !value.StartsWith('\\')
            && !value.Contains('\\')
            && !value.Contains(':')
            && !value.Contains("//", StringComparison.Ordinal)
            && value.Split('/').All(part => part is not "" and not "." and not ".."),
            "Niebezpieczna ścieżka docelowa w manifeście.");
    }

    private static (string Selected, IReadOnlyDictionary<string, string> All)
        SelectAndValidateTargetPaths(
        IReadOnlyDictionary<string, string>? targetPaths,
        string targetPlatform)
    {
        Require(targetPaths is not null, "Brak files[].target_paths.");
        Require(targetPaths!.Count == 2
            && targetPaths.ContainsKey("windows")
            && targetPaths.ContainsKey("linux"),
            "target_paths musi zawierać dokładnie windows i linux.");
        foreach (var path in targetPaths.Values)
        {
            RequireSafeRelativePath(path);
        }

        return (targetPaths[targetPlatform], targetPaths);
    }

    private static void RequireSafePayloadName(string? value)
    {
        RequireText(value, "files[].payload_name");
        Require(value!.Length <= 160
            && value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-'),
            "Niebezpieczna nazwa payloadu w manifeście.");
    }

    private static void RequireSafeIdentifier(string? value, string field)
    {
        RequireText(value, field);
        Require(value!.Length <= 120
            && value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-'),
            $"Nieprawidłowy {field}.");
    }

    private static void RequireVersion(string? value)
    {
        RequireText(value, "version");
        Require(value!.Length <= 40
            && value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-'),
            "Nieprawidłowa wersja.");
    }

    private static void RequireDigits(string? value, string field)
    {
        RequireText(value, field);
        Require(value!.All(char.IsAsciiDigit), $"Nieprawidłowy {field}.");
    }

    private static void RequireSha256(string? value, string field)
    {
        Require(value is { Length: 64 }
            && value.All(character => char.IsAsciiHexDigit(character)),
            $"Nieprawidłowy {field}.");
    }

    private static void RequireText(string? value, string field) =>
        Require(!string.IsNullOrWhiteSpace(value), $"Brak {field}.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private sealed record ReleaseManifest
    {
        [JsonPropertyName("schema")]
        public string? Schema { get; init; }

        [JsonPropertyName("release_id")]
        public string? ReleaseId { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("game")]
        public ReleaseGame? Game { get; init; }

        [JsonPropertyName("source_checkpoint")]
        public JsonElement SourceCheckpoint { get; init; }

        [JsonPropertyName("evidence_files")]
        public JsonElement EvidenceFiles { get; init; }

        [JsonPropertyName("translation_scope")]
        public ReleaseTranslationScope? TranslationScope { get; init; }

        [JsonPropertyName("files")]
        public List<ReleaseFile>? Files { get; init; }

        [JsonPropertyName("installer_contract")]
        public ReleaseInstallerContract? InstallerContract { get; init; }
    }

    private sealed record ReleaseGame
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("steam_app_id")]
        public int SteamAppId { get; init; }

        [JsonPropertyName("steam_build_id")]
        public string? SteamBuildId { get; init; }

        [JsonPropertyName("windows_depot_id")]
        public string? WindowsDepotId { get; init; }

        [JsonPropertyName("windows_manifest_id")]
        public string? WindowsManifestId { get; init; }
    }

    private sealed record ReleaseTranslationScope
    {
        [JsonPropertyName("dialogue_fields")]
        public int DialogueFields { get; init; }

        [JsonPropertyName("dialogue_conversations")]
        public string? DialogueConversations { get; init; }

        [JsonPropertyName("database_selected_fields")]
        public int DatabaseSelectedFields { get; init; }

        [JsonPropertyName("database_changed_fields")]
        public int DatabaseChangedFields { get; init; }

        [JsonPropertyName("database_identical_fields")]
        public int DatabaseIdenticalFields { get; init; }

        [JsonPropertyName("per_table")]
        public Dictionary<string, int>? PerTable { get; init; }
    }

    private sealed record ReleaseFile
    {
        [JsonPropertyName("target_paths")]
        public Dictionary<string, string>? TargetPaths { get; init; }

        [JsonPropertyName("payload_name")]
        public string? PayloadName { get; init; }

        [JsonPropertyName("role")]
        public string? Role { get; init; }

        [JsonPropertyName("source_sha256")]
        public string? SourceSha256 { get; init; }

        [JsonPropertyName("source_size")]
        public long SourceSize { get; init; }

        [JsonPropertyName("source_crc32")]
        public uint? SourceCrc32 { get; init; }

        [JsonPropertyName("output_sha256")]
        public string? OutputSha256 { get; init; }

        [JsonPropertyName("output_size")]
        public long OutputSize { get; init; }

        [JsonPropertyName("output_crc32")]
        public uint? OutputCrc32 { get; init; }

        [JsonPropertyName("deterministic_rebuild")]
        public string? DeterministicRebuild { get; init; }

        [JsonPropertyName("non_target_comparison")]
        public string? NonTargetComparison { get; init; }

        [JsonPropertyName("installed_last")]
        public bool InstalledLast { get; init; }
    }

    private sealed record ReleaseInstallerContract
    {
        [JsonPropertyName("clean_client_only")]
        public bool CleanClientOnly { get; init; }

        [JsonPropertyName("game_must_be_stopped")]
        public bool GameMustBeStopped { get; init; }

        [JsonPropertyName("backup_all_replaced_files")]
        public bool BackupAllReplacedFiles { get; init; }

        [JsonPropertyName("atomic_sibling_replacement")]
        public bool AtomicSiblingReplacement { get; init; }

        [JsonPropertyName("verify_after_install")]
        public bool VerifyAfterInstall { get; init; }

        [JsonPropertyName("automatic_rollback_on_failure")]
        public bool AutomaticRollbackOnFailure { get; init; }

        [JsonPropertyName("independent_verify")]
        public bool IndependentVerify { get; init; }

        [JsonPropertyName("independent_uninstall")]
        public bool IndependentUninstall { get; init; }

        [JsonPropertyName("unknown_or_mixed_client_is_terminal")]
        public bool UnknownOrMixedClientIsTerminal { get; init; }
    }
}

public interface IPayloadProvider
{
    Stream OpenRead(string payloadName);
}

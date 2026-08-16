using System.Security.Cryptography;
using System.Text.Json;
using ChainedEchoesPolishInstaller.Core;

var tests = new InstallerTests();
tests.RunAll();

internal sealed class InstallerTests
{
    private readonly byte[] sourceBundle = "clean bundle fixture"u8.ToArray();
    private readonly byte[] patchedBundle = "patched Polish bundle fixture"u8.ToArray();
    private readonly byte[] sourceDatabase = "clean database fixture"u8.ToArray();
    private readonly byte[] patchedDatabase = "patched database fixture"u8.ToArray();
    private readonly byte[] sourceCatalog = "clean catalog fixture"u8.ToArray();
    private readonly byte[] patchedCatalog = "patched cumulative catalog fixture"u8.ToArray();

    public void RunAll()
    {
        Run(nameof(ProductionPayloadMatchesContract), ProductionPayloadMatchesContract);
        Run(nameof(ReleaseManifestMatchesContract), ReleaseManifestMatchesContract);
        Run(nameof(ReleaseEvidenceMatchesManifest), ReleaseEvidenceMatchesManifest);
        Run(nameof(ManualPathAcceptsTheReportedSpaceInsteadOfUnderscore), ManualPathAcceptsTheReportedSpaceInsteadOfUnderscore);
        Run(nameof(ManualPathRepairsCommonGameAndDataDirectoryAliases), ManualPathRepairsCommonGameAndDataDirectoryAliases);
        Run(nameof(ManualPathAcceptsExplorerQuotes), ManualPathAcceptsExplorerQuotes);
        Run(nameof(UnquotedCommandLinePathWithSpacesIsJoined), UnquotedCommandLinePathWithSpacesIsJoined);
        Run(nameof(ManifestRejectsPathTraversal), ManifestRejectsPathTraversal);
        Run(nameof(ManifestRejectsDuplicatePayload), ManifestRejectsDuplicatePayload);
        Run(nameof(ManifestRejectsWeakenedSafetyContract), ManifestRejectsWeakenedSafetyContract);
        Run(nameof(ManifestSelectsBothTargetLayouts), ManifestSelectsBothTargetLayouts);
        Run(nameof(ManifestRejectsUnsupportedTargetLayout), ManifestRejectsUnsupportedTargetLayout);
        Run(nameof(InstallVerifyRollbackReinstallRoundTrip), InstallVerifyRollbackReinstallRoundTrip);
        Run(nameof(MixedLayeredSourceIsRejectedBeforeBackup), MixedLayeredSourceIsRejectedBeforeBackup);
        Run(nameof(UnknownSourceIsRejectedBeforeBackup), UnknownSourceIsRejectedBeforeBackup);
        Run(nameof(PatchedFilesWithoutBackupAreRejected), PatchedFilesWithoutBackupAreRejected);
        Run(nameof(RunningGameBlocksMutation), RunningGameBlocksMutation);
        Run(nameof(WriteFailureRollsBackToCleanClient), WriteFailureRollsBackToCleanClient);
        Run(nameof(UnrelatedFilesRemainUntouched), UnrelatedFilesRemainUntouched);
        Run(nameof(CorruptedBackupBlocksRollback), CorruptedBackupBlocksRollback);
        Console.WriteLine("SELF-TEST OK: 20/20");
    }

    private void ManualPathAcceptsTheReportedSpaceInsteadOfUnderscore()
    {
        using var fixture = NewFixture();
        var reportedInput = Path.Combine(fixture.GameDirectory, "Chained Echoes_Data");
        Assert(
            SteamLocator.TryResolveGameDirectory(reportedInput, out var resolved, out var problem),
            $"reported pasted path was rejected: {problem}");
        Assert(resolved == Path.GetFullPath(fixture.GameDirectory), "reported path resolution");
    }

    private void ManualPathRepairsCommonGameAndDataDirectoryAliases()
    {
        using var fixture = NewFixture();
        var common = Directory.GetParent(fixture.GameDirectory)!.FullName;
        var mistaken = Path.Combine(common, "Chained_Echoes", "Chained Echoes_Data");
        Assert(
            SteamLocator.TryResolveGameDirectory(mistaken, out var resolved, out var problem),
            $"friendly aliases were rejected: {problem}");
        Assert(resolved == Path.GetFullPath(fixture.GameDirectory), "friendly alias resolution");
    }

    private void ManualPathAcceptsExplorerQuotes()
    {
        using var fixture = NewFixture();
        var copiedFromExplorer = $"“{fixture.GameDirectory}”";
        Assert(
            SteamLocator.TryResolveGameDirectory(copiedFromExplorer, out var resolved, out var problem),
            $"Explorer-quoted path was rejected: {problem}");
        Assert(resolved == Path.GetFullPath(fixture.GameDirectory), "Explorer quote resolution");
    }

    private static void UnquotedCommandLinePathWithSpacesIsJoined()
    {
        var parsed = InstallerArguments.Parse(
            ["install", "--game-dir", @"D:\Steam", @"Library\steamapps\common\Chained", "Echoes"]);
        Assert(parsed.Action == "install", "unquoted CLI action");
        Assert(
            parsed.GameDirectory == @"D:\Steam Library\steamapps\common\Chained Echoes",
            "unquoted CLI path");
    }

    private static void ProductionPayloadMatchesContract()
    {
        var root = FindProjectRoot();
        var payloadDirectory = Path.Combine(root, "src", "Installer.Win", "Payload");
        var package = LoadProductionPackage();
        Assert(package.Files.Count == 7, "production file count");
        foreach (var file in package.Files)
        {
            var path = Path.Combine(payloadDirectory, file.PayloadName);
            Assert(File.Exists(path), $"missing production payload: {file.PayloadName}");
            Assert(new FileInfo(path).Length == file.PatchedSize, $"wrong size: {file.PayloadName}");
            Assert(
                string.Equals(Hashing.Sha256File(path), file.PatchedSha256, StringComparison.OrdinalIgnoreCase),
                $"wrong hash: {file.PayloadName}");
        }
    }

    private static void ReleaseManifestMatchesContract()
    {
        var root = FindProjectRoot();
        var path = Path.Combine(
            root, "src", "Installer.Win", "Payload", "release-manifest.json");
        using var stream = File.OpenRead(path);
        var package = ReleaseManifestLoader.Load(stream);
        Assert(package.PackageId == $"chained-echoes-polish-ef001-{package.Version}", "release manifest package id");
        Assert(!string.IsNullOrWhiteSpace(package.Version), "release manifest version");
        Assert(package.SupportedSteamBuild.All(char.IsAsciiDigit), "release manifest Steam build");
        Assert(package.Files.Count == 7, "release manifest file count");
        Assert(package.Files.All(file => !string.IsNullOrWhiteSpace(file.Role)), "manifest roles");
        Assert(package.TranslationScope.DatabaseSelectedFields > 0, "manifest DB count");
    }

    private static void ManifestRejectsPathTraversal()
    {
        AssertManifestMutationRejected(text => text.Replace(
            "Chained_Echoes_Data/StreamingAssets/aa/StandaloneLinux64/gfx_assets_all.bundle",
            "../outside.bundle",
            StringComparison.Ordinal));
    }

    private static void ManifestRejectsDuplicatePayload()
    {
        AssertManifestMutationRejected(text => text.Replace(
            "\"payload_name\": \"gfx2_assets_all.bundle\"",
            "\"payload_name\": \"gfx_assets_all.bundle\"",
            StringComparison.Ordinal));
    }

    private static void ManifestRejectsWeakenedSafetyContract()
    {
        AssertManifestMutationRejected(text => text.Replace(
            "\"automatic_rollback_on_failure\": true",
            "\"automatic_rollback_on_failure\": false",
            StringComparison.Ordinal));
    }

    private static void ManifestSelectsBothTargetLayouts()
    {
        var windows = LoadProductionPackage("windows");
        var linux = LoadProductionPackage("linux");
        Assert(windows.Files.Any(file => file.RelativePath.Contains(
            "/StandaloneWindows64/", StringComparison.Ordinal)),
            "Windows target layout");
        Assert(linux.Files.Any(file => file.RelativePath.Contains(
            "/StandaloneLinux64/", StringComparison.Ordinal)),
            "Linux target layout");
        Assert(windows.Files.Single(file => file.PayloadName == "bansheegz_database.bytes").RelativePath
            == linux.Files.Single(file => file.PayloadName == "bansheegz_database.bytes").RelativePath,
            "shared database target path");
    }

    private static void ManifestRejectsUnsupportedTargetLayout()
    {
        AssertThrows<InvalidDataException>(() => LoadProductionPackage("macos"));
    }

    private static void ReleaseEvidenceMatchesManifest()
    {
        var root = FindProjectRoot();
        var manifestPath = Path.Combine(
            root, "src", "Installer.Win", "Payload", "release-manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var evidence = manifest.RootElement.GetProperty("evidence_files").EnumerateArray().ToArray();
        Assert(evidence.Length == 1, "release evidence count");
        Assert(
            evidence[0].GetProperty("name").GetString() == "runtime-release-manifest.json",
            "release evidence identity");
        foreach (var entry in evidence)
        {
            var name = entry.GetProperty("name").GetString()
                ?? throw new InvalidDataException("missing evidence name");
            var path = Path.Combine(root, "release-evidence", name);
            Assert(File.Exists(path), $"missing release evidence: {name}");
            Assert(new FileInfo(path).Length == entry.GetProperty("size").GetInt64(), "evidence size");
            Assert(
                Hashing.Sha256File(path) == entry.GetProperty("sha256").GetString(),
                "evidence hash");
        }
    }

    private void InstallVerifyRollbackReinstallRoundTrip()
    {
        using var fixture = NewFixture();
        var engine = NewEngine(fixture.Payloads);
        engine.Install(fixture.GameDirectory);
        Assert(engine.Verify(fixture.GameDirectory), "verify after install");
        AssertPatched(fixture);
        Assert(Directory.Exists(fixture.BackupDirectory), "backup exists");

        engine.Install(fixture.GameDirectory);
        Assert(engine.Verify(fixture.GameDirectory), "idempotent install with backup");

        engine.Rollback(fixture.GameDirectory);
        AssertClean(fixture);
        Assert(Directory.Exists(fixture.BackupDirectory), "backup retained");

        engine.Install(fixture.GameDirectory);
        Assert(engine.Verify(fixture.GameDirectory), "reinstall from retained clean backup");
    }

    private void MixedLayeredSourceIsRejectedBeforeBackup()
    {
        using var fixture = NewFixture();
        File.WriteAllBytes(fixture.BundlePath, patchedBundle);
        var engine = NewEngine(fixture.Payloads);
        AssertThrows<InvalidDataException>(() => engine.Install(fixture.GameDirectory));
        Assert(!Directory.Exists(fixture.BackupDirectory), "no backup for mixed client");
        AssertBytes(fixture.DatabasePath, sourceDatabase);
        AssertBytes(fixture.CatalogPath, sourceCatalog);
    }

    private void UnknownSourceIsRejectedBeforeBackup()
    {
        using var fixture = NewFixture();
        File.WriteAllBytes(fixture.DatabasePath, "unknown external mutation"u8.ToArray());
        var engine = NewEngine(fixture.Payloads);
        AssertThrows<InvalidDataException>(() => engine.Install(fixture.GameDirectory));
        Assert(!Directory.Exists(fixture.BackupDirectory), "no backup for unknown source");
        AssertBytes(fixture.BundlePath, sourceBundle);
        AssertBytes(fixture.CatalogPath, sourceCatalog);
    }

    private void PatchedFilesWithoutBackupAreRejected()
    {
        using var fixture = NewFixture();
        File.WriteAllBytes(fixture.BundlePath, patchedBundle);
        File.WriteAllBytes(fixture.DatabasePath, patchedDatabase);
        File.WriteAllBytes(fixture.CatalogPath, patchedCatalog);
        var engine = NewEngine(fixture.Payloads);
        AssertThrows<InvalidOperationException>(() => engine.Install(fixture.GameDirectory));
        Assert(!Directory.Exists(fixture.BackupDirectory), "missing backup is not invented");
    }

    private void RunningGameBlocksMutation()
    {
        using var fixture = NewFixture();
        var engine = NewEngine(fixture.Payloads, isGameRunning: () => true);
        AssertThrows<InvalidOperationException>(() => engine.Install(fixture.GameDirectory));
        AssertClean(fixture);
        Assert(!Directory.Exists(fixture.BackupDirectory), "no backup while game runs");
    }

    private void WriteFailureRollsBackToCleanClient()
    {
        using var fixture = NewFixture();
        var payloads = new FlakyPayloadProvider(
            new Dictionary<string, byte[]>
            {
                ["bundle.payload"] = patchedBundle,
                ["database.payload"] = patchedDatabase,
                ["catalog.payload"] = patchedCatalog,
            },
            failOnOpen: 5);
        var engine = NewEngine(payloads);
        AssertThrows<InvalidOperationException>(() => engine.Install(fixture.GameDirectory));
        AssertClean(fixture);
        Assert(Directory.Exists(fixture.BackupDirectory), "backup retained after automatic rollback");
    }

    private void UnrelatedFilesRemainUntouched()
    {
        using var fixture = NewFixture();
        var before = File.ReadAllBytes(fixture.SentinelPath);
        var engine = NewEngine(fixture.Payloads);
        engine.Install(fixture.GameDirectory);
        AssertBytes(fixture.SentinelPath, before);
        engine.Rollback(fixture.GameDirectory);
        AssertBytes(fixture.SentinelPath, before);
    }

    private void CorruptedBackupBlocksRollback()
    {
        using var fixture = NewFixture();
        var engine = NewEngine(fixture.Payloads);
        engine.Install(fixture.GameDirectory);
        var backupBundle = Path.Combine(
            fixture.BackupDirectory,
            "files",
            BundleRelative.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllBytes(backupBundle, "corrupted backup"u8.ToArray());
        AssertThrows<InvalidDataException>(() => engine.Rollback(fixture.GameDirectory));
        AssertPatched(fixture);
    }

    private const string BundleRelative =
        "Chained_Echoes_Data/StreamingAssets/aa/StandaloneWindows64/fixture.bundle";
    private const string DatabaseRelative =
        "Chained_Echoes_Data/StreamingAssets/bansheegz_database.bytes";
    private const string CatalogRelative =
        "Chained_Echoes_Data/StreamingAssets/aa/catalog.json";

    private TestFixture NewFixture()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "ce-polish-installer-test-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "Chained Echoes");
        var bundle = Path.Combine(game, BundleRelative.Replace('/', Path.DirectorySeparatorChar));
        var database = Path.Combine(game, DatabaseRelative.Replace('/', Path.DirectorySeparatorChar));
        var catalog = Path.Combine(game, CatalogRelative.Replace('/', Path.DirectorySeparatorChar));
        var sentinel = Path.Combine(game, "Chained_Echoes_Data", "unrelated.asset");
        Directory.CreateDirectory(Path.GetDirectoryName(bundle)!);
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        File.WriteAllBytes(
            Path.Combine(
                game,
                OperatingSystem.IsWindows()
                    ? "Chained Echoes.exe"
                    : "Chained_Echoes.x86_64"),
            "fixture executable"u8.ToArray());
        File.WriteAllBytes(bundle, sourceBundle);
        File.WriteAllBytes(database, sourceDatabase);
        File.WriteAllBytes(catalog, sourceCatalog);
        File.WriteAllBytes(sentinel, "must remain untouched"u8.ToArray());
        var payloads = new MemoryPayloadProvider(new Dictionary<string, byte[]>
        {
            ["bundle.payload"] = patchedBundle,
            ["database.payload"] = patchedDatabase,
            ["catalog.payload"] = patchedCatalog,
        });
        return new TestFixture(root, game, bundle, database, catalog, sentinel, payloads);
    }

    private InstallationEngine NewEngine(
        IPayloadProvider payloads,
        Func<bool>? isGameRunning = null)
    {
        var package = new InstallerPackage(
            "fixture-package",
            "fixture",
            "fixture-version",
            "fixture-status",
            "fixture-build",
            new TranslationScope(
                1,
                "fixture",
                1,
                1,
                0,
                new Dictionary<string, int> { ["fixture"] = 1 }),
            new PackageFile[]
            {
                new(
                    BundleRelative,
                    "bundle.payload",
                    "fixture-bundle",
                    Sha(sourceBundle),
                    sourceBundle.Length,
                    Sha(patchedBundle),
                    patchedBundle.Length),
                new(
                    DatabaseRelative,
                    "database.payload",
                    "fixture-database",
                    Sha(sourceDatabase),
                    sourceDatabase.Length,
                    Sha(patchedDatabase),
                    patchedDatabase.Length),
                new(
                    CatalogRelative,
                    "catalog.payload",
                    "fixture-catalog",
                    Sha(sourceCatalog),
                    sourceCatalog.Length,
                    Sha(patchedCatalog),
                    patchedCatalog.Length,
                    InstallLast: true),
            });
        return new InstallationEngine(
            package,
            payloads,
            _ => { },
            isGameRunning ?? (() => false));
    }

    private static string Sha(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static string FindProjectRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(
                current.FullName, "src", "Installer.Win", "Installer.Win.csproj")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Nie znaleziono katalogu projektu instalatora.");
    }

    private static InstallerPackage LoadProductionPackage(string? targetPlatform = null)
    {
        var path = Path.Combine(
            FindProjectRoot(),
            "src",
            "Installer.Win",
            "Payload",
            "release-manifest.json");
        using var stream = File.OpenRead(path);
        return ReleaseManifestLoader.Load(stream, targetPlatform);
    }

    private static void AssertManifestMutationRejected(Func<string, string> mutate)
    {
        var path = Path.Combine(
            FindProjectRoot(),
            "src",
            "Installer.Win",
            "Payload",
            "release-manifest.json");
        var original = File.ReadAllText(path);
        var changed = mutate(original);
        Assert(changed != original, "manifest mutation applied");
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(changed));
        AssertThrows<InvalidDataException>(() => ReleaseManifestLoader.Load(stream));
    }

    private void AssertClean(TestFixture fixture)
    {
        AssertBytes(fixture.BundlePath, sourceBundle);
        AssertBytes(fixture.DatabasePath, sourceDatabase);
        AssertBytes(fixture.CatalogPath, sourceCatalog);
    }

    private void AssertPatched(TestFixture fixture)
    {
        AssertBytes(fixture.BundlePath, patchedBundle);
        AssertBytes(fixture.DatabasePath, patchedDatabase);
        AssertBytes(fixture.CatalogPath, patchedCatalog);
    }

    private static void AssertBytes(string path, byte[] expected)
    {
        Assert(File.ReadAllBytes(path).SequenceEqual(expected), $"bytes differ: {path}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("ASSERTION FAILED: " + message);
        }
    }

    private static void AssertThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException(
            $"ASSERTION FAILED: expected {typeof(T).Name}");
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
}

internal class MemoryPayloadProvider(IReadOnlyDictionary<string, byte[]> files)
    : IPayloadProvider
{
    public virtual Stream OpenRead(string payloadName) =>
        new MemoryStream(files[payloadName], writable: false);
}

internal sealed class FlakyPayloadProvider(
    IReadOnlyDictionary<string, byte[]> files,
    int failOnOpen) : MemoryPayloadProvider(files)
{
    private int calls;

    public override Stream OpenRead(string payloadName)
    {
        calls++;
        if (calls == failOnOpen)
        {
            throw new IOException("simulated payload read failure");
        }

        return base.OpenRead(payloadName);
    }
}

internal sealed class TestFixture : IDisposable
{
    public TestFixture(
        string root,
        string gameDirectory,
        string bundlePath,
        string databasePath,
        string catalogPath,
        string sentinelPath,
        IPayloadProvider payloads)
    {
        Root = root;
        GameDirectory = gameDirectory;
        BundlePath = bundlePath;
        DatabasePath = databasePath;
        CatalogPath = catalogPath;
        SentinelPath = sentinelPath;
        Payloads = payloads;
    }

    public string Root { get; }
    public string GameDirectory { get; }
    public string BundlePath { get; }
    public string DatabasePath { get; }
    public string CatalogPath { get; }
    public string SentinelPath { get; }
    public IPayloadProvider Payloads { get; }
    public string BackupDirectory => Path.Combine(GameDirectory, ".fixture-package-backup");

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

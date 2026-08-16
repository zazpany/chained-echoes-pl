using System.Text;
using ChainedEchoesPolishInstaller.Core;

namespace ChainedEchoesPolishInstaller;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "Chained Echoes — polska wersja EF-001";

        var interactive = args.Length == 0;
        try
        {
            var payloads = new EmbeddedPayloadProvider();
            using var manifest = payloads.OpenRead("release-manifest.json");
            var package = ReleaseManifestLoader.Load(manifest);
            PrintHeader(package);
            var parsed = InstallerArguments.Parse(args);
            var gameDirectory = parsed.GameDirectory is null
                ? ResolveGameDirectory(interactive)
                : SteamLocator.ResolveGameDirectory(parsed.GameDirectory);
            var assetPlatform = SteamLocator.DetectAssetPlatform(gameDirectory);
            using (var platformManifest = payloads.OpenRead("release-manifest.json"))
            {
                package = ReleaseManifestLoader.Load(platformManifest, assetPlatform);
            }
            var action = parsed.Action ?? SelectAction();
            var engine = new InstallationEngine(
                package,
                payloads,
                message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"));

            Console.WriteLine();
            Console.WriteLine($"Katalog gry: {gameDirectory}");
            Console.WriteLine($"Układ plików Addressables: {assetPlatform}");
            Console.WriteLine();
            switch (action)
            {
                case "install":
                    ConfirmInstall(package);
                    engine.Install(gameDirectory);
                    break;
                case "verify":
                    if (!engine.Verify(gameDirectory))
                    {
                        throw new InvalidDataException("Pakiet nie jest w pełni zainstalowany.");
                    }

                    Console.WriteLine("Pakiet jest poprawnie zainstalowany.");
                    break;
                case "rollback":
                case "uninstall":
                    ConfirmRollback();
                    engine.Rollback(gameDirectory);
                    break;
                default:
                    throw new ArgumentException($"Nieznana operacja: {action}");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.WriteLine("OPERACJA ZAKOŃCZONA POMYŚLNIE");
            Console.ResetColor();
            PauseIfInteractive(interactive);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operacja anulowana.");
            PauseIfInteractive(interactive);
            return 2;
        }
        catch (Exception error)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("OPERACJA PRZERWANA");
            Console.WriteLine(error.Message);
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Nieznany ani warstwowo zmodyfikowany klient nie jest akceptowany.");
            Console.WriteLine("Jeżeli rozpoczął się zapis, instalator podjął automatyczny rollback do czystej wersji.");
            PauseIfInteractive(interactive);
            return 1;
        }
    }

    private static void PrintHeader(InstallerPackage package)
    {
        var scope = package.TranslationScope;
        Console.WriteLine("============================================================");
        Console.WriteLine($"  CHAINED ECHOES — POLSKA WERSJA {package.Version}");
        Console.WriteLine("============================================================");
        Console.WriteLine("  • polskie fonty i wymagany katalog Addressables");
        Console.WriteLine($"  • {scope.DialogueFields} pól dialogowych z rozmów {scope.DialogueConversations}");
        Console.WriteLine($"  • {scope.DatabaseSelectedFields} pól UI, przedmiotów i umiejętności");
        Console.WriteLine("  • pełny backup, weryfikacja i odinstalowanie");
        Console.WriteLine();
        Console.WriteLine($"Instalacja jest obsługiwana wyłącznie na czystym Steam build {package.SupportedSteamBuild}.");
        Console.WriteLine();
    }

    private static string ResolveGameDirectory(bool interactive)
    {
        var detected = SteamLocator.FindGameDirectory();
        if (detected is not null)
        {
            Console.WriteLine($"Wykryto Steam: {detected}");
            if (!interactive || AskYesNo("Użyć tego katalogu?"))
            {
                return detected;
            }
        }

        if (!interactive)
        {
            throw new DirectoryNotFoundException(
                "Nie wykryto Chained Echoes. Użyj --game-dir PATH.");
        }

        Console.WriteLine("Skopiuj ścieżkę z paska Eksploratora — spacje i cudzysłowy są w porządku.");
        Console.WriteLine(@"Przykład: D:\SteamLibrary\steamapps\common\Chained Echoes");
        Console.WriteLine("Niczego nie zamieniaj ręcznie na podkreślenia.");
        while (true)
        {
            Console.Write("Wklej pełną ścieżkę do katalogu „Chained Echoes”: ");
            var value = Console.ReadLine();
            if (SteamLocator.TryResolveGameDirectory(value, out var resolved, out var problem))
            {
                return resolved!;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(problem);
            Console.WriteLine(
                "Możesz wkleić katalog gry, plik EXE albo katalog Chained_Echoes_Data. "
                + "Spacje, cudzysłowy i wariant „Chained Echoes_Data” są obsługiwane automatycznie.");
            Console.ResetColor();
        }
    }

    private static string SelectAction()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Wybierz operację:");
            Console.WriteLine("  1 — ZAINSTALUJ pełny polski patch");
            Console.WriteLine("  2 — SPRAWDŹ wszystkie pliki patcha");
            Console.WriteLine("  3 — ODINSTALUJ i przywróć czystą grę");
            Console.WriteLine("  0 — Anuluj");
            Console.Write("Twój wybór: ");
            switch (Console.ReadLine()?.Trim())
            {
                case "1": return "install";
                case "2": return "verify";
                case "3": return "uninstall";
                case "0": throw new OperationCanceledException();
                default: Console.WriteLine("Wpisz 1, 2, 3 albo 0."); break;
            }
        }
    }

    private static void ConfirmInstall(InstallerPackage package)
    {
        var scope = package.TranslationScope;
        Console.WriteLine("Zostanie zainstalowany pełny cumulative patch EF-001.");
        Console.WriteLine(
            $"Obejmuje fonty, katalog, dialogi {scope.DialogueConversations} "
            + "oraz bieżące tłumaczenia BGDatabase.");
        Console.WriteLine("Gra musi być zamknięta, a wszystkie pliki muszą pochodzić z czystego klienta.");
        Console.WriteLine("Przed pierwszym zapisem powstanie pełny, zweryfikowany backup.");
        if (!AskYesNo("Kontynuować?"))
        {
            throw new OperationCanceledException();
        }
    }

    private static void ConfirmRollback()
    {
        Console.WriteLine("Wszystkie pliki patcha zostaną przywrócone do czystej wersji Steam.");
        if (!AskYesNo("Kontynuować?"))
        {
            throw new OperationCanceledException();
        }
    }

    private static bool AskYesNo(string prompt)
    {
        while (true)
        {
            Console.Write($"{prompt} [T/N]: ");
            var answer = Console.ReadLine()?.Trim().ToUpperInvariant();
            if (answer is "T" or "TAK" or "Y" or "YES")
            {
                return true;
            }

            if (answer is "N" or "NIE" or "NO")
            {
                return false;
            }
        }
    }

    private static void PauseIfInteractive(bool interactive)
    {
        if (!interactive)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Naciśnij dowolny klawisz, aby zamknąć instalator.");
        Console.ReadKey(intercept: true);
    }
}

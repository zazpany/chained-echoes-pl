namespace ChainedEchoesPolishInstaller.Core;

public static class InstallerArguments
{
    public static (string? Action, string? GameDirectory) Parse(string[] args)
    {
        string? action = null;
        string? gameDirectory = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--game-dir")
            {
                if (gameDirectory is not null)
                {
                    throw new ArgumentException("Opcję --game-dir podano więcej niż raz.");
                }

                var parts = new List<string>();
                while (index + 1 < args.Length
                    && args[index + 1] is not "install" and not "verify" and not "rollback" and not "uninstall"
                    && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    parts.Add(args[++index]);
                }

                if (parts.Count == 0)
                {
                    throw new ArgumentException("Po --game-dir brakuje ścieżki do gry.");
                }

                // Be forgiving when a user pastes an unquoted Windows path containing spaces.
                gameDirectory = string.Join(' ', parts);
            }
            else if (args[index] is "install" or "verify" or "rollback" or "uninstall")
            {
                if (action is not null)
                {
                    throw new ArgumentException("Podano więcej niż jedną operację instalatora.");
                }

                action = args[index];
            }
            else
            {
                throw new ArgumentException(
                    "Użycie: ChainedEchoesPolishInstaller [install|verify|uninstall] [--game-dir PATH]");
            }
        }

        return (action, gameDirectory);
    }
}

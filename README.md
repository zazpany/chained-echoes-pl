# Chained Echoes PL

Fanowskie polskie tłumaczenie gry **Chained Echoes**.

Repozytorium służy do publikowania kolejnych wersji kompletnego patcha oraz
instrukcji instalacji. Gotowe archiwa ZIP będą dostępne wyłącznie w zakładce
**[Releases](https://github.com/zazpany/chained-echoes-pl/releases)** — nie
trzeba pobierać kodu repozytorium.

> Projekt nie jest powiązany z Umami Tiger ani Deck13. Do użycia patcha
> wymagana jest legalna kopia Chained Echoes.

## Status

Pierwsze wydania Windows i Kubuntu są publikowane jako oznaczony **pre-release**
do smoke testu przez odbiorcę. Pobieraj wyłącznie właściwy asset ZIP z zakładki Releases,
sprawdź dołączony SHA-256 i zgłoś wynik według
[checklisty smoke testu](docs/SMOKE-TEST.md).

## Co zawiera patch

Każde wydanie jest samodzielnym, kompletnym patchem dla opisanego checkpointu.
Może obejmować:

- polskie fonty i atlasy TextMeshPro;
- wymagany katalog Addressables;
- zatwierdzone dialogi;
- UI, menu, przedmioty i umiejętności zapisane w BGDatabase;
- instalator Windows/Kubuntu z backupem, weryfikacją i odinstalowaniem.

Dokładny zakres i obsługiwany build Steam są zawsze podane w notatkach danego
wydania.

## Instalacja Windows

1. W Steam wybierz Chained Echoes → **Właściwości** → **Zainstalowane pliki** →
   **Sprawdź spójność plików gry**.
2. Zamknij grę.
3. Pobierz najnowszy ZIP z zakładki **Releases** oraz sprawdź jego SHA-256.
4. Rozpakuj archiwum.
5. Uruchom `ChainedEchoesPolishInstaller.exe` i zaakceptuj pytanie UAC.
6. Potwierdź wykryty katalog Steam i wybierz `1 — ZAINSTALUJ`. Jeżeli katalog
   nie zostanie wykryty, skopiuj pełną ścieżkę z paska Eksploratora i wklej ją
   bez zamieniania spacji na podkreślenia.
7. W grze pozostaw język **English**.

Instalator odrzuca nieznaną wersję, wcześniejszy patch i mieszaną instalację
przed pierwszym zapisem. Tworzy pełny backup wszystkich zastępowanych plików i
automatycznie przywraca czystą grę, jeżeli instalacja się nie powiedzie.

## Weryfikacja i odinstalowanie

Uruchom instalator ponownie:

- opcja `2` sprawdza wszystkie pliki patcha;
- opcja `3` odinstalowuje patch i przywraca dokładny czysty klient.

Nie instaluj nowego wydania na starszy patch. Najpierw użyj opcji `3` albo
przywróć czyste pliki przez Steam.

## Instalacja Kubuntu

1. W Steam sprawdź spójność plików Chained Echoes i zamknij grę.
2. Pobierz i rozpakuj ZIP oznaczony `linux-x64`.
3. Uruchom `Uruchom-instalator.sh` w terminalu — bez `sudo`.
4. Potwierdź wykrytą bibliotekę Steam, wybierz `1`, a w grze pozostaw język
   **English**.

Wydania Windows i Kubuntu zawierają te same zweryfikowane payloady. Instalator
kieruje bundle odpowiednio do `StandaloneWindows64` lub `StandaloneLinux64`.

## Zgłaszanie problemów

Użyj zakładki **Issues** i podaj:

- wersję patcha oraz build Steam;
- etap, na którym wystąpił problem;
- pełny komunikat instalatora;
- czy pliki gry zostały wcześniej sprawdzone przez Steam;
- zrzut ekranu lub fragment `Player.log`, bez danych prywatnych.

Nie dołączaj plików gry, save'ów ani całych logów zawierających dane konta.

## Dla maintainerów

Źródła wspólnego instalatora i fabryki wydań są w [installer/](installer/).
To repozytorium jest kanonicznym źródłem wyłącznie instalatora Windows/Linux,
publicznych notatek i publikacji GitHub. Tłumaczenia, acceptance stores,
build artefaktów runtime i clean-source identities należą do canonical
`EchoForge/main`; nie są duplikowane tutaj.
Procedura publikowania kolejnych ZIP-ów znajduje się w
[docs/RELEASING.md](docs/RELEASING.md). Binariów, payloadów i manifestów
roboczych nie commitujemy — trafiają wyłącznie do assets GitHub Releases.

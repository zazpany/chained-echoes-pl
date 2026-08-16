# Chained Echoes — polska wersja EF-001

Samodzielny, manifestowy instalator dla Windows x64 i Kubuntu/Linux x64.
Każde wydanie zawiera polskie fonty, zatwierdzone dialogi, skumulowane
tłumaczenia BGDatabase oraz wymagany katalog Addressables. Dokładne liczniki,
wersję gry i sumy plików instalator odczytuje z osadzonego `MANIFEST.json`.

Użytkownik nie musi instalować Pythona ani .NET i nie kopiuje plików ręcznie.
Paczka zawiera także dowody budowy bazy, dialogów i fontów w `evidence/`.

## Instalacja na Windows

1. W Steam: Chained Echoes → **Właściwości** → **Zainstalowane pliki** →
   **Sprawdź spójność plików gry**.
2. Zamknij grę i rozpakuj ZIP `win-x64`.
3. Uruchom `ChainedEchoesPolishInstaller.exe`.
4. Potwierdź automatycznie wykryty katalog Steam. Przy wyborze ręcznym możesz
   wskazać katalog gry, plik EXE albo `Chained_Echoes_Data`.
5. Wybierz `1 — ZAINSTALUJ` i odpowiedz `T`.

Niepodpisany EXE może wywołać ostrzeżenie SmartScreen. Najpierw porównaj jego
SHA-256 z `SHA256SUMS.txt`, potem użyj **Więcej informacji → Uruchom mimo to**.

## Instalacja na Kubuntu

1. W Steam sprawdź spójność plików Chained Echoes i zamknij grę.
2. Rozpakuj ZIP `linux-x64`.
3. Uruchom `Uruchom-instalator.sh` (dwuklik i **Uruchom w terminalu**) albo:

   ```bash
   ./Uruchom-instalator.sh
   ```

4. Potwierdź automatycznie wykrytą bibliotekę Steam i wybierz `1`.

Nie używaj `sudo`. Instalator obsługuje zwykłą instalację Steam oraz typowe
ścieżki pakietu Flatpak. Te same zweryfikowane payloady są kierowane do
`StandaloneWindows64` na Windows i `StandaloneLinux64` na Kubuntu.

W grze pozostaw język **English** — patch zastępuje zatwierdzone pola tej
wersji językowej polskimi wartościami.

## Bezpieczeństwo i rollback

Przed pierwszym zapisem instalator:

1. waliduje ścisły manifest i każdy osadzony payload;
2. wymaga dokładnie wspieranego, czystego klienta Steam;
3. odrzuca inną wersję, wcześniejszy patch i mieszaną instalację;
4. tworzy pełny backup wszystkich zastępowanych plików;
5. zapisuje atomowo i instaluje katalog Addressables jako ostatni;
6. weryfikuje wynik i automatycznie wykonuje rollback po błędzie.

Backup znajduje się w katalogu gry jako:

```text
.chained-echoes-polish-ef001-<wersja>-backup
```

Uruchom instalator ponownie i wybierz `2`, aby zweryfikować patch, albo `3`,
aby go odinstalować i przywrócić czyste pliki. Backup pozostaje do audytu.

## Wiersz poleceń

Windows:

```powershell
.\ChainedEchoesPolishInstaller.exe install --game-dir "C:\...\steamapps\common\Chained Echoes"
.\ChainedEchoesPolishInstaller.exe verify --game-dir "C:\...\steamapps\common\Chained Echoes"
.\ChainedEchoesPolishInstaller.exe uninstall --game-dir "C:\...\steamapps\common\Chained Echoes"
```

Kubuntu:

```bash
./ChainedEchoesPolishInstaller install --game-dir "$HOME/.steam/steam/steamapps/common/Chained Echoes"
./ChainedEchoesPolishInstaller verify --game-dir "$HOME/.steam/steam/steamapps/common/Chained Echoes"
./ChainedEchoesPolishInstaller uninstall --game-dir "$HOME/.steam/steam/steamapps/common/Chained Echoes"
```

## Jedno polecenie dla opiekuna wydania

Wymagane są Python 3 i .NET SDK 8. Fabryka pobiera wszystkie siedem payloadów
wyłącznie z jawnie wskazanego, podwójnie zbudowanego versioned runtime candidate
utworzonego z czystego `EchoForge/main`. Binaria gry pozostają poza historią
publicznego repo.

```bash
python3 tools/release.py \
  --version VERSION \
  --echoforge-root /ścieżka/do/czystego/EchoForge \
  --runtime-manifest /ścieżka/do/czystego/EchoForge/var/runtime-patches/chained-echoes-pl-runtime-VERSION/manifest.json
```

Polecenie wymaga czystych canonical `main` obu repozytoriów, waliduje exact
EchoForge source SHA, pełne acceptance, bazę, dialogi, DLC, fonty i katalog,
generuje manifest i evidence,
uruchamia self-testy, buduje oba self-contained instalatory, tworzy
deterministyczne ZIP-y i sprawdza drugi byte-identical rebuild.

Dodanie `--publish --repo zazpany/chained-echoes-pl` publikuje oba ZIP-y i sumy
SHA-256 jako GitHub prerelease, ale dopiero po potwierdzeniu, że tag, `HEAD` i
`origin/main` wskazują exact public source SHA zapisane w manifeście. Publikacja
nadal wymaga ręcznego smoke testu na Windows, Kubuntu i w grze.

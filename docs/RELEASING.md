# Publikowanie wydania

Wydanie jest kompletnym patchem dla jednego czystego checkpointu EchoForge.
Nie publikujemy incremental patchy zależnych od wcześniejszej instalacji.

## Jedno polecenie

Wszystkie payloady są pobierane z jawnie wskazanego, podwójnie zbudowanego
versioned runtime candidate w czystym `EchoForge/main`: po siedem natywnych
plików dla Windows i Linux.
Fabryka nie ma domyślnego ani awaryjnego kandydata i nie wyszukuje ND-7.

```bash
cd installer
python3 tools/release.py \
  --version VERSION \
  --echoforge-root /ścieżka/do/EchoForge \
  --runtime-manifest /ścieżka/do/EchoForge/var/runtime-patches/chained-echoes-pl-runtime-VERSION/manifest.json
```

Generator:

1. odrzuca brudny lub niekanoniczny `EchoForge/main` i `chained-echoes-pl/main`;
2. wymaga, aby runtime manifest wskazywał dokładny bieżący commit EchoForge;
3. wymaga kompletnego acceptance `1807 / 17846 / 0`, sprawdza dowody
   dopasowania źródeł, strukturę bazy, pola poza zakresem i byte-identical rebuild;
4. waliduje SHA-256, CRC32 i rozmiary siedmiu plików każdej platformy;
5. generuje jeden ścisły manifest z osobnym inventory Windows i Kubuntu;
6. uruchamia testy C# i Pythona;
7. buduje self-contained `win-x64` i `linux-x64`;
8. tworzy deterministyczne ZIP-y i powtarza build dla porównania bajtowego;
9. zapisuje sumy SHA-256 oraz notatki wydania w `installer/dist/`.

Dodanie poniższych parametrów publikuje oba ZIP-y jako GitHub prerelease:

```bash
  --publish --repo zazpany/chained-echoes-pl
```

Publisher ponownie sprawdza clean `main`, wymaga `HEAD == origin/main ==` SHA
z osadzonego manifestu, tworzy lub potwierdza tag dokładnie na tym SHA,
odczytuje target tagu z remote i dopiero wtedy wywołuje `gh release create`
z `--verify-tag --target <SHA>`. Istniejący tag lub release nie jest przepisywany.

## Bramka wydania

Przed publikacją:

1. upewnij się, że instalator wskazuje dokładny obsługiwany build Steam;
2. przejrzyj wygenerowany `MANIFEST.json`, liczniki i notatki;
3. potwierdź `SELF-TEST OK`, testy Pythona i dwa wyniki byte-identical;
4. sprawdź oba ZIP-y przez `unzip -t` i ich pliki `.sha256`;
5. sprawdź, że exact source SHA jest już na `origin/main`;
6. nie commituj payloadów, plików gry, evidence ani `dist/`.

Wydanie pozostaje pre-release do czasu ręcznych testów:

- Windows: install → verify → uruchomienie gry → uninstall → czysty klient;
- Kubuntu: install → verify → uruchomienie gry → uninstall → czysty klient;
- ponowna instalacja tego samego RC i finalny smoke test rozgrywki.

Po potwierdzeniu obu platform promuj istniejący release bez przebudowy,
zmiany taga lub podmiany assets:

```bash
gh release edit vVERSION \
  --repo zazpany/chained-echoes-pl \
  --prerelease=false
```

Po operacji odczytaj release ponownie i potwierdź `prerelease=false`, exact
target taga oraz niezmienione nazwy i rozmiary wszystkich assets. Status stable
dotyczy bramki technicznej; dopóki projekt nie przejdzie osobnego pełnego QA
językowego i redakcji, notatki wydania i README muszą jasno zachowywać status
**pełne tłumaczenie przed QA i redakcją**.

Gotowe ZIP-y i EXE/ELF są assets GitHub Releases, nigdy historią Git.

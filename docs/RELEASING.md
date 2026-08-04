# Publikowanie wydania

Wydanie jest kompletnym patchem dla jednego czystego checkpointu EchoForge.
Nie publikujemy incremental patchy zależnych od wcześniejszej instalacji.

## Jedno polecenie

Wszystkie payloady są pobierane z podwójnie zbudowanego, siedmioplikowego RC
ND-7 w ignorowanym `var/runtime-patches/` czystego worktree EchoForge.

```bash
cd installer
python3 tools/release.py \
  --version VERSION \
  --echoforge-root /ścieżka/do/EchoForge \
  --payload-source /ścieżka/do/prywatnych/payloadów
```

Generator:

1. odrzuca brudny checkpoint EchoForge;
2. wymaga preflightu 9/9 i dokładnego RC `ef001-nd7-rc1`;
3. sprawdza dowody dopasowania źródeł, brak placeholderów/review-required,
   strukturę bazy, pola poza zakresem i byte-identical rebuild;
4. waliduje SHA-256, CRC32 i rozmiary wszystkich siedmiu plików;
5. generuje jeden ścisły manifest dla Windows oraz Kubuntu;
6. uruchamia testy C# i Pythona;
7. buduje self-contained `win-x64` i `linux-x64`;
8. tworzy deterministyczne ZIP-y i powtarza build dla porównania bajtowego;
9. zapisuje sumy SHA-256 oraz notatki wydania w `installer/dist/`.

Dodanie poniższych parametrów publikuje oba ZIP-y jako GitHub prerelease:

```bash
  --publish --repo zazpany/chained-echoes-pl
```

## Bramka wydania

Przed publikacją:

1. upewnij się, że instalator wskazuje dokładny obsługiwany build Steam;
2. przejrzyj wygenerowany `MANIFEST.json`, liczniki i notatki;
3. potwierdź `SELF-TEST OK`, testy Pythona i dwa wyniki byte-identical;
4. sprawdź oba ZIP-y przez `unzip -t` i ich pliki `.sha256`;
5. nie commituj payloadów, plików gry, evidence ani `dist/`.

Pierwsze wydanie pozostaje pre-release do czasu ręcznych testów:

- Windows: install → verify → uruchomienie gry → uninstall → czysty klient;
- Kubuntu: install → verify → uruchomienie gry → uninstall → czysty klient;
- ponowna instalacja tego samego RC i finalny smoke test rozgrywki.

Gotowe ZIP-y i EXE/ELF są assets GitHub Releases, nigdy historią Git.

# Publikowanie wydania

Wydanie jest kompletnym patchem dla jednego czystego checkpointu EchoForge.
Nie publikujemy incremental patchy zależnych od wcześniejszej instalacji.

## Jedno polecenie

Statyczne payloady fontów i `catalog.json` są przechowywane prywatnie. Bieżąca
baza i dialogi są pobierane z ignorowanego `var/runtime-patches/` w czystym
worktree EchoForge.

```bash
cd installer
python3 tools/release.py \
  --version VERSION \
  --echoforge-root /ścieżka/do/EchoForge \
  --payload-source /ścieżka/do/prywatnych/payloadów
```

Generator:

1. odrzuca brudny checkpoint EchoForge;
2. sprawdza dowody dopasowania źródeł, brak placeholderów/review-required,
   strukturę bazy, pola poza zakresem i byte-identical rebuild;
3. waliduje SHA-256 i rozmiary bazy, dialogów, fontów i katalogu;
4. generuje jeden ścisły manifest dla Windows oraz Kubuntu;
5. uruchamia testy C# i Pythona;
6. buduje self-contained `win-x64` i `linux-x64`;
7. tworzy deterministyczne ZIP-y i powtarza build dla porównania bajtowego;
8. zapisuje sumy SHA-256 oraz notatki wydania w `installer/dist/`.

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

# Publikowanie wydania

Wydanie jest kompletnym patchem dla jednego checkpointu. Nie publikujemy
incremental patchy zależnych od wcześniejszej instalacji.

## Bramka wydania

Przed publikacją:

1. zbuduj wszystkie komponenty z dokładnie czystych plików obsługiwanego builda;
2. zweryfikuj źródłowe i wynikowe SHA-256 oraz rozmiary;
3. uruchom pełne testy builderów i instalatora;
4. wykonaj na Windows sekwencję install → verify → uruchomienie gry → uninstall;
5. potwierdź przywrócenie czystego klienta;
6. ponownie zainstaluj ten sam release candidate i wykonaj finalny smoke test;
7. sprawdź ZIP przez `unzip -t` lub `Test-Archive` oraz wszystkie sumy SHA-256.

## Zawartość GitHub Release

- `ChainedEchoesPolishInstaller-VERSION-win-x64.zip`;
- `ChainedEchoesPolishInstaller-VERSION-win-x64.zip.sha256`;
- opis obsługiwanego builda Steam;
- dokładne liczniki przetłumaczonych tabel i dialogów;
- wynik testów oraz znane ograniczenia;
- krótka lista punktów smoke testu.

ZIP i EXE są assets wydania. Nie dodajemy ich do historii Git.

## Przykładowa publikacja

```powershell
gh release create vVERSION `
  .\dist\ChainedEchoesPolishInstaller-VERSION-win-x64.zip `
  .\dist\ChainedEchoesPolishInstaller-VERSION-win-x64.zip.sha256 `
  --title "Chained Echoes PL VERSION" `
  --notes-file .\dist\release-notes.md
```

Pierwsze wydanie publikujemy jako publiczny pre-release, aby wyznaczona osoba
mogła pobrać dokładny asset i wykonać smoke test. Oznaczenie pre-release usuwamy
dopiero po zaakceptowaniu instalacji, rozgrywki i rollbacku.

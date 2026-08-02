# Smoke test Windows

Ta checklista dotyczy publicznego pre-release. Wykonuj ją na legalnej kopii gry
Steam i nie dołączaj plików gry do zgłoszenia.

## Przygotowanie

- [ ] Zanotuj wersję patcha i numer builda Steam.
- [ ] W Steam uruchom **Sprawdź spójność plików gry**.
- [ ] Zamknij grę.
- [ ] Pobierz ZIP wyłącznie z GitHub Releases.
- [ ] Porównaj SHA-256 ZIP-a z plikiem `.sha256` z tego samego wydania.

## Instalacja

- [ ] Rozpakuj ZIP i uruchom `ChainedEchoesPolishInstaller.exe`.
- [ ] Potwierdź automatycznie wykryty katalog gry.
- [ ] Wybierz opcję `1`.
- [ ] Instalator zakończy operację zielonym komunikatem.
- [ ] Opcja `2` potwierdzi zgodność wszystkich siedmiu plików.

## Gra

Pozostaw język gry ustawiony na **English**.

- [ ] Gra uruchamia się bez błędu i dochodzi do ekranu tytułowego.
- [ ] Polskie znaki `ąćęłńóśźż` oraz wielkie odpowiedniki są wyświetlane poprawnie.
- [ ] Menu, opcje, sterowanie oraz zapis/odczyt nie mają pustych etykiet.
- [ ] Nowa gra wyświetla polski prolog z rozmów 166–186.
- [ ] Ekrany przedmiotów pokazują polskie nazwy i opisy z aktualnego checkpointu.
- [ ] Ekrany umiejętności kilku postaci i Sky Armor wyświetlają polskie nazwy.
- [ ] Dłuższe teksty nie powodują awarii; zanotuj przycięcia lub złe zawijanie.
- [ ] Zapisz grę, zamknij ją, uruchom ponownie i wczytaj zapis.
- [ ] Sprawdź `Player.log` pod kątem błędów BGDatabase, Addressables, indeksów i kodowania.

## Odinstalowanie

- [ ] Zamknij grę i wybierz w instalatorze opcję `3`.
- [ ] Rollback zakończy się pomyślnie.
- [ ] Steam nie pobiera plików po ponownym sprawdzeniu spójności albo zgłoś dokładnie, co przywrócił.
- [ ] Gra uruchamia się po rollbacku.

## Zgłoszenie wyniku

Utwórz Issue typu **Problem z patchem**. Jeśli wszystko działa, wpisz w tytule
`[Smoke test OK] wersja — build Steam` i podaj krótko wynik instalacji,
rozgrywki oraz rollbacku. Przy błędzie dołącz komunikat instalatora, kroki i
bezpieczny fragment logu.

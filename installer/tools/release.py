#!/usr/bin/env python3
"""Prepare and build deterministic Windows and Kubuntu installer releases."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
PAYLOAD_DIR = ROOT / "src" / "Installer.Win" / "Payload"
EVIDENCE_DIR = ROOT / "release-evidence"
DIST_DIR = ROOT / "dist"
DB_PATCH_DIR = Path("var/runtime-patches/ef001-database-reviewed-v1")
PROLOGUE_PATCH_DIR = Path("var/runtime-patches/ef001-prologue-166-186-v1")
SHA_PREFIX = "sha256:"
FIXED_ZIP_TIME = (2020, 1, 1, 0, 0, 0)


class ReleaseError(RuntimeError):
    """A release contract was not satisfied."""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Buduje zweryfikowane instalatory Windows i Kubuntu z jednego manifestu.")
    parser.add_argument("--version", required=True, help="Wersja, np. 0.2.0-rc.2")
    parser.add_argument("--echoforge-root", type=Path, required=True)
    parser.add_argument(
        "--payload-source",
        type=Path,
        default=PAYLOAD_DIR,
        help="Prywatny katalog z fontami i catalog.json (domyślnie bieżący Payload).")
    parser.add_argument(
        "--font-manifest",
        type=Path,
        default=EVIDENCE_DIR / "font-manifest.json")
    parser.add_argument("--skip-prepare", action="store_true")
    parser.add_argument("--no-determinism-check", action="store_true")
    parser.add_argument("--publish", action="store_true")
    parser.add_argument("--repo", default="zazpany/chained-echoes-pl")
    return parser.parse_args()


def run(command: list[str], *, cwd: Path = ROOT, capture: bool = False) -> str:
    result = subprocess.run(
        command,
        cwd=cwd,
        check=True,
        text=True,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE if capture else None,
    )
    return result.stdout.strip() if capture else ""


def load_json(path: Path) -> dict[str, Any]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ReleaseError(f"Nie można odczytać JSON: {path}: {error}") from error
    if not isinstance(data, dict):
        raise ReleaseError(f"Oczekiwano obiektu JSON: {path}")
    return data


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def strip_sha(value: str) -> str:
    return value.removeprefix(SHA_PREFIX).lower()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ReleaseError(message)


def verify_file(path: Path, expected_hash: str, expected_size: int) -> None:
    require(path.is_file(), f"Brak pliku: {path}")
    require(path.stat().st_size == expected_size, f"Niezgodny rozmiar: {path}")
    require(sha256(path) == strip_sha(expected_hash), f"Niezgodny SHA-256: {path}")


def copy_file(source: Path, target: Path) -> None:
    source = source.resolve()
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and source == target.resolve():
        return
    temporary = target.with_name(f".{target.name}.tmp.{os.getpid()}")
    shutil.copyfile(source, temporary)
    os.replace(temporary, target)


def require_clean_checkpoint(root: Path) -> str:
    require((root / ".git").exists() or run(
        ["git", "rev-parse", "--is-inside-work-tree"], cwd=root, capture=True) == "true",
        f"To nie jest worktree Git: {root}")
    status = run(["git", "status", "--porcelain"], cwd=root, capture=True)
    require(not status, "EchoForge ma niezacommitowane zmiany; wydanie zostało zatrzymane.")
    return run(["git", "rev-parse", "HEAD"], cwd=root, capture=True)


def evidence_entry(path: Path) -> dict[str, Any]:
    return {"name": path.name, "sha256": sha256(path), "size": path.stat().st_size}


def prepare_release(args: argparse.Namespace) -> dict[str, Any]:
    require(re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.-]{0,39}", args.version) is not None,
            "Nieprawidłowa wersja.")
    echoforge = args.echoforge_root.resolve()
    checkpoint = require_clean_checkpoint(echoforge)
    baseline = load_json(ROOT / "release-baseline.json")
    require(baseline.get("schema") == "echoforge.chained-echoes-installer-baseline/v1",
            "Nieobsługiwany release-baseline.json.")

    db_dir = echoforge / DB_PATCH_DIR
    prologue_dir = echoforge / PROLOGUE_PATCH_DIR
    db_manifest_path = db_dir / "manifest.json"
    db_inventory_path = db_dir / "inventory.json"
    db_payload_path = db_dir / "bansheegz_database.bytes"
    prologue_manifest_path = prologue_dir / "manifest.json"
    prologue_payload_path = prologue_dir / "packedassets_assets_all.bundle"
    db = load_json(db_manifest_path)
    prologue = load_json(prologue_manifest_path)

    selected = db.get("selected_fields")
    changed = db.get("changed_fields")
    identical = db.get("identical_fields")
    require(isinstance(selected, int) and selected > 0, "Nieprawidłowe selected_fields bazy.")
    require(selected == changed + identical,
            "selected_fields != changed_fields + identical_fields.")
    require(db.get("source_strings_and_sha256_matched") is True,
            "Źródła pakietów bazy nie zostały w pełni dopasowane.")
    require(db.get("placeholder_rows_selected") == [],
            "Baza zawiera techniczne placeholdery.")
    require(db.get("review_required_rows") == 0,
            "Baza zawiera wiersze review_required.")
    require(db.get("non_target_comparison", {}).get("result") == "identical",
            "Pola poza zakresem bazy nie są identyczne.")
    deterministic = db.get("deterministic_rebuild", {})
    output_db = db.get("output_database", {})
    source_db = db.get("source_database", {})
    require(deterministic.get("passed") is True
            and deterministic.get("result") == "byte_identical"
            and strip_sha(deterministic.get("second_build_sha256", ""))
                == strip_sha(output_db.get("sha256", "")),
            "Baza nie ma dowodu deterministycznego rebuildu.")
    require(db.get("structural_verification", {}).get("tables_fields_rows_unchanged") is True,
            "Brak pełnej weryfikacji strukturalnej bazy.")
    verify_file(db_payload_path, output_db["sha256"], output_db["size"])

    conversations = prologue.get("conversations")
    require(isinstance(conversations, list) and conversations
            and conversations == list(range(min(conversations), max(conversations) + 1)),
            "Manifest dialogów nie zawiera ciągłego zakresu rozmów.")
    require(prologue.get("changed_fields", 0) > 0, "Brak zatwierdzonych pól dialogowych.")
    require(prologue.get("unintended_logical_changes") == 0,
            "Dialogi zawierają niezamierzone zmiany logiczne.")
    source_dialogue = prologue.get("source_bundle", {})
    output_dialogue = prologue.get("output_bundle", {})
    verify_file(prologue_payload_path, output_dialogue["sha256"], output_dialogue["size"])

    payload_source = args.payload_source.resolve()
    static_files = baseline.get("static_files", [])
    require(len(static_files) == 5, "Baseline musi zawierać cztery fonty i katalog.")
    PAYLOAD_DIR.mkdir(parents=True, exist_ok=True)
    files: list[dict[str, Any]] = []
    for entry in static_files:
        source = payload_source / entry["payload_name"]
        verify_file(source, entry["output_sha256"], entry["output_size"])
        copy_file(source, PAYLOAD_DIR / entry["payload_name"])
        files.append(dict(entry))

    dynamic = baseline["dynamic_files"]
    dialogue_entry = {
        **dynamic["dialogue"],
        "source_sha256": strip_sha(source_dialogue["sha256"]),
        "source_size": source_dialogue["size"],
        "output_sha256": strip_sha(output_dialogue["sha256"]),
        "output_size": output_dialogue["size"],
    }
    database_entry = {
        **dynamic["database"],
        "source_sha256": strip_sha(source_db["sha256"]),
        "source_size": source_db["size"],
        "source_crc32": source_db["file_crc32"],
        "output_sha256": strip_sha(output_db["sha256"]),
        "output_size": output_db["size"],
        "output_crc32": output_db["file_crc32"],
        "deterministic_rebuild": "byte-identical",
        "non_target_comparison": "identical",
    }
    files.insert(4, dialogue_entry)
    files.insert(5, database_entry)
    copy_file(prologue_payload_path, PAYLOAD_DIR / dialogue_entry["payload_name"])
    copy_file(db_payload_path, PAYLOAD_DIR / database_entry["payload_name"])

    evidence_sources = [
        (db_inventory_path, EVIDENCE_DIR / "database-inventory.json"),
        (db_manifest_path, EVIDENCE_DIR / "database-manifest.json"),
        (prologue_manifest_path, EVIDENCE_DIR / "prologue-manifest.json"),
        (args.font_manifest.resolve(), EVIDENCE_DIR / "font-manifest.json"),
    ]
    for source, target in evidence_sources:
        require(source.is_file(), f"Brak evidence: {source}")
        copy_file(source, target)
    evidence = [evidence_entry(target) for _, target in evidence_sources]

    per_table = db.get("per_table_counts")
    require(isinstance(per_table, dict) and sum(per_table.values()) == selected,
            "Liczniki per_table nie zgadzają się z selected_fields.")
    manifest = {
        "schema": "echoforge.chained-echoes-cumulative-release/v2",
        "release_id": f"chained-echoes-polish-ef001-{args.version}",
        "version": args.version,
        "status": "windows-kubuntu-release-candidate",
        "game": baseline["game"],
        "source_checkpoint": {
            "repository": "EchoForge",
            "commit": checkpoint,
            "database_inventory_sha256": sha256(EVIDENCE_DIR / "database-inventory.json"),
            "database_manifest_sha256": sha256(EVIDENCE_DIR / "database-manifest.json"),
        },
        "evidence_files": evidence,
        "translation_scope": {
            "dialogue_fields": prologue["changed_fields"],
            "dialogue_conversations": f"{min(conversations)}-{max(conversations)}",
            "database_selected_fields": selected,
            "database_changed_fields": changed,
            "database_identical_fields": identical,
            "per_table": dict(sorted(per_table.items())),
        },
        "files": files,
        "installer_contract": {
            "clean_client_only": True,
            "game_must_be_stopped": True,
            "backup_all_replaced_files": True,
            "atomic_sibling_replacement": True,
            "verify_after_install": True,
            "automatic_rollback_on_failure": True,
            "independent_verify": True,
            "independent_uninstall": True,
            "unknown_or_mixed_client_is_terminal": True,
        },
    }
    PAYLOAD_DIR.joinpath("release-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return manifest


def write_sums(package: Path) -> None:
    lines = []
    for path in sorted(item for item in package.rglob("*") if item.is_file()
                       and item.name != "SHA256SUMS.txt"):
        lines.append(f"{sha256(path)}  {path.relative_to(package).as_posix()}")
    (package / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


def deterministic_zip(source: Path, target: Path) -> None:
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(item for item in source.rglob("*") if item.is_file()):
            relative = path.relative_to(source).as_posix()
            info = zipfile.ZipInfo(relative, FIXED_ZIP_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            executable = path.name == "ChainedEchoesPolishInstaller" or path.suffix == ".sh"
            mode = 0o755 if executable else 0o644
            info.external_attr = (0o100000 | mode) << 16
            info.create_system = 3
            archive.writestr(info, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED,
                             compresslevel=9)


def package_platform(version: str, runtime: str, binary_name: str) -> Path:
    publish = DIST_DIR / "publish" / runtime
    package = DIST_DIR / "package" / runtime
    package.mkdir(parents=True, exist_ok=True)
    copy_file(publish / binary_name, package / binary_name)
    copy_file(ROOT / "README.md", package / "README.md")
    copy_file(PAYLOAD_DIR / "release-manifest.json", package / "MANIFEST.json")
    shutil.copytree(EVIDENCE_DIR, package / "evidence", dirs_exist_ok=True)
    if runtime == "linux-x64":
        copy_file(ROOT / "packaging" / "Uruchom-instalator.sh",
                  package / "Uruchom-instalator.sh")
        os.chmod(package / binary_name, 0o755)
        os.chmod(package / "Uruchom-instalator.sh", 0o755)
    write_sums(package)
    archive = DIST_DIR / f"ChainedEchoesPolishInstaller-{version}-{runtime}.zip"
    deterministic_zip(package, archive)
    archive.with_suffix(archive.suffix + ".sha256").write_text(
        f"{sha256(archive)}  {archive.name}\n", encoding="utf-8")
    return archive


def build_packages(version: str) -> list[Path]:
    for path in [DIST_DIR / "publish", DIST_DIR / "package"]:
        shutil.rmtree(path, ignore_errors=True)
    DIST_DIR.mkdir(parents=True, exist_ok=True)
    run(["dotnet", "build", "src/Installer.Win/Installer.Win.csproj", "-c", "Release",
         f"-p:Version={version}"])
    run(["dotnet", "run", "--project", "tests/Installer.SelfTest/Installer.SelfTest.csproj",
         "-c", "Release"])
    run([sys.executable, "-m", "unittest", "discover", "-s", "tests",
         "-p", "test_*.py", "-v"])
    for runtime in ["win-x64", "linux-x64"]:
        run(["dotnet", "publish", "src/Installer.Win/Installer.Win.csproj",
             "-c", "Release", "-r", runtime, "--self-contained", "true",
             f"-p:Version={version}", "-o", str(DIST_DIR / "publish" / runtime)])
    return [
        package_platform(version, "win-x64", "ChainedEchoesPolishInstaller.exe"),
        package_platform(version, "linux-x64", "ChainedEchoesPolishInstaller"),
    ]


def write_release_notes(manifest: dict[str, Any], archives: list[Path]) -> Path:
    scope = manifest["translation_scope"]
    table_lines = "\n".join(
        f"- `{name}`: {count}" for name, count in scope["per_table"].items())
    asset_lines = "\n".join(
        f"- `{archive.name}` — SHA-256 `{sha256(archive)}`" for archive in archives)
    notes = f"""# Chained Echoes PL {manifest['version']}

Kandydat wydania do ręcznego smoke testu na Windows x64 i Kubuntu x64.

Zakres:

- dialogi {scope['dialogue_conversations']}: {scope['dialogue_fields']} pól;
- BGDatabase: {scope['database_selected_fields']} pól
  ({scope['database_changed_fields']} zmienionych, {scope['database_identical_fields']} identycznych);
- polskie fonty i skumulowany katalog Addressables.

BGDatabase per tabela:

{table_lines}

Paczki:

{asset_lines}

Instalator wymaga czystego Steam build `{manifest['game']['steam_build_id']}`.
Nie uruchamia gry. Po instalacji wykonaj checklistę smoke testu z repozytorium.
"""
    path = DIST_DIR / f"release-notes-{manifest['version']}.md"
    path.write_text(notes, encoding="utf-8")
    return path


def publish(args: argparse.Namespace, notes: Path, archives: list[Path]) -> None:
    tag = f"v{args.version}"
    assets: list[str] = []
    for archive in archives:
        assets.extend([str(archive), str(archive.with_suffix(archive.suffix + ".sha256"))])
    run(["gh", "release", "create", tag, "--repo", args.repo, "--prerelease",
         "--title", f"Chained Echoes PL {args.version}", "--notes-file", str(notes), *assets])


def main() -> int:
    args = parse_args()
    try:
        manifest = (load_json(PAYLOAD_DIR / "release-manifest.json")
                    if args.skip_prepare else prepare_release(args))
        first = build_packages(args.version)
        first_hashes = {path.name: sha256(path) for path in first}
        if not args.no_determinism_check:
            second = build_packages(args.version)
            second_hashes = {path.name: sha256(path) for path in second}
            require(first_hashes == second_hashes,
                    f"Paczki nie są deterministyczne: {first_hashes} != {second_hashes}")
            archives = second
            print("Deterministyczny rebuild obu ZIP-ów: byte-identical")
        else:
            archives = first
        notes = write_release_notes(manifest, archives)
        for archive in archives:
            print(f"Gotowe: {archive} ({sha256(archive)})")
        if args.publish:
            publish(args, notes, archives)
            print(f"Opublikowano prerelease v{args.version} w {args.repo}")
        return 0
    except (ReleaseError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        print(f"BŁĄD WYDANIA: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

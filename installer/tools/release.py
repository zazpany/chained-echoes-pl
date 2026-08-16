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
RUNTIME_SCHEMA = "echoforge.chained-echoes-runtime-release/v2"
RUNTIME_PREFIX = "chained-echoes-pl-runtime-"
EXPECTED_RC_FILES = {
    "linux": frozenset({
        "Chained_Echoes_Data/StreamingAssets/bansheegz_database.bytes",
        "Chained_Echoes_Data/StreamingAssets/aa/catalog.json",
        "Chained_Echoes_Data/StreamingAssets/aa/StandaloneLinux64/packedassets_assets_all.bundle",
        "Chained_Echoes_Data/StreamingAssets/aa/StandaloneLinux64/duplicateassetisolation6_assets_all_4a1bbd777aa93f48f34767d6afbe9f14.bundle",
        "Chained_Echoes_Data/StreamingAssets/aa/StandaloneLinux64/gfx_assets_all.bundle",
        "Chained_Echoes_Data/StreamingAssets/aa/StandaloneLinux64/gfx2_assets_all.bundle",
        "Chained_Echoes_Data/StreamingAssets/aa/StandaloneLinux64/systemgfx_assets_all.bundle",
    }),
    "windows": frozenset({
        "Chained Echoes_Data/StreamingAssets/bansheegz_database.bytes",
        "Chained Echoes_Data/StreamingAssets/aa/catalog.json",
        "Chained Echoes_Data/StreamingAssets/aa/StandaloneWindows64/packedassets_assets_all.bundle",
        "Chained Echoes_Data/StreamingAssets/aa/StandaloneWindows64/duplicateassetisolation6_assets_all_0912e6789a7419e465e2306a084992fe.bundle",
        "Chained Echoes_Data/StreamingAssets/aa/StandaloneWindows64/gfx_assets_all.bundle",
        "Chained Echoes_Data/StreamingAssets/aa/StandaloneWindows64/gfx2_assets_all.bundle",
        "Chained Echoes_Data/StreamingAssets/aa/StandaloneWindows64/systemgfx_assets_all.bundle",
    }),
}
SHA_PREFIX = "sha256:"
FIXED_ZIP_TIME = (2020, 1, 1, 0, 0, 0)


class ReleaseError(RuntimeError):
    """A release contract was not satisfied."""


class PreparedRelease:
    def __init__(self, *, manifest: dict[str, Any], runtime_dir: Path) -> None:
        self.manifest = manifest
        self.runtime_dir = runtime_dir


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Buduje zweryfikowane instalatory Windows i Kubuntu z jednego manifestu.")
    parser.add_argument("--version", required=True, help="Wersja, np. 0.2.0-rc.2")
    parser.add_argument("--echoforge-root", type=Path, required=True)
    parser.add_argument("--runtime-manifest", type=Path, required=True)
    parser.add_argument("--no-determinism-check", action="store_true")
    parser.add_argument("--publish", action="store_true")
    parser.add_argument("--repo", default="zazpany/chained-echoes-pl")
    return parser.parse_args()


def run(
    command: list[str],
    *,
    cwd: Path = ROOT,
    capture: bool = False,
    environment: dict[str, str] | None = None,
) -> str:
    process_environment = None
    if environment is not None:
        process_environment = os.environ.copy()
        process_environment.update(environment)
    result = subprocess.run(
        command,
        cwd=cwd,
        check=True,
        text=True,
        env=process_environment,
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


def reset_generated_directory(path: Path) -> None:
    resolved = path.resolve(strict=False)
    require(resolved.parent.is_relative_to(ROOT.resolve()),
            f"Odmowa resetu katalogu poza installer/: {path}")
    require(not path.is_symlink(), f"Odmowa resetu dowiązania: {path}")
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True)


def require_clean_checkpoint(root: Path, *, repository: str) -> str:
    require((root / ".git").exists() or run(
        ["git", "rev-parse", "--is-inside-work-tree"], cwd=root, capture=True) == "true",
        f"To nie jest worktree Git: {root}")
    status = run(["git", "status", "--porcelain"], cwd=root, capture=True)
    require(not status, f"{repository} ma niezacommitowane zmiany; wydanie zostało zatrzymane.")
    branch = run(["git", "symbolic-ref", "--short", "HEAD"], cwd=root, capture=True)
    require(branch == "main", f"{repository} release source musi być canonical main.")
    return run(["git", "rev-parse", "HEAD"], cwd=root, capture=True)


def evidence_entry(path: Path) -> dict[str, Any]:
    return {"name": path.name, "sha256": sha256(path), "size": path.stat().st_size}


def _require_identity(value: object, label: str) -> dict[str, Any]:
    require(isinstance(value, dict), f"Brak tożsamości pliku: {label}.")
    identity = value
    digest = identity.get("sha256")
    require(isinstance(digest, str) and re.fullmatch(r"(?:sha256:)?[0-9a-fA-F]{64}", digest)
            is not None, f"Nieprawidłowy SHA-256: {label}.")
    require(isinstance(identity.get("size"), int) and identity["size"] > 0,
            f"Nieprawidłowy rozmiar: {label}.")
    require(isinstance(identity.get("file_crc32"), int),
            f"Brak CRC32: {label}.")
    return identity


def validate_runtime_release(
    release: dict[str, Any],
    *,
    version: str,
    echoforge_commit: str,
) -> None:
    require(release.get("schema") == RUNTIME_SCHEMA,
            "Nieobsługiwany versioned runtime manifest.")
    require(release.get("runtime_id") == RUNTIME_PREFIX + version,
            "Runtime manifest nie odpowiada jawnej wersji instalatora.")
    require(release.get("version") == version,
            "Wersja runtime manifestu i instalatora jest różna.")
    source = release.get("source", {})
    require(source == {
        "repository": "EchoForge",
        "ref": "refs/heads/main",
        "commit": echoforge_commit,
    }, "Runtime candidate nie pochodzi z dokładnego canonical EchoForge/main HEAD.")
    require(release.get("installation_enabled") is False,
            "Runtime candidate nie jest source-only.")
    runtime_acceptance = release.get("runtime_acceptance", {})
    require(runtime_acceptance.get("required") is True
            and runtime_acceptance.get("status") == "pending_manual_smoke"
            and runtime_acceptance.get("platforms") == {
                "linux": "user-confirmed-pass", "windows": "pending"},
            "Runtime candidate nie zachowuje Linux PASS i Windows PENDING.")
    acceptance = release.get("acceptance", {})
    dialogue_source = acceptance.get("dialogue_source", {})
    require(acceptance.get("accepted_checkpoints") == 1807
            and acceptance.get("accepted_fields") == 17846
            and acceptance.get("remaining_checkpoints") == 0
            and acceptance.get("excluded_raw_records") == 22
            and isinstance(acceptance.get("sha256"), str)
            and re.fullmatch(r"sha256:[0-9a-f]{64}", acceptance["sha256"]) is not None
            and dialogue_source.get("records") == 17868
            and isinstance(dialogue_source.get("sha256"), str)
            and re.fullmatch(r"sha256:[0-9a-f]{64}", dialogue_source["sha256"]) is not None,
            "Runtime candidate nie ma kompletnego canonical acceptance 1807/17846.")
    deterministic = release.get("deterministic_rebuild", {})
    require(deterministic.get("passed") is True
            and deterministic.get("build_count") == 2
            and deterministic.get("component_reports") == "byte_identical"
            and deterministic.get("platforms") == {
                "linux": "byte_identical", "windows": "byte_identical"}
            and deterministic.get("payload_count") == 14,
            "Runtime candidate nie ma podwójnego byte-identical rebuildu obu platform.")

    rollback = release.get("rollback", {})
    require(rollback.get("required") is True
            and rollback.get("catalog_install_order") == "last"
            and rollback.get("catalog_restore_order") == "first",
            "Runtime candidate nie ma kompletnego kontraktu rollbacku.")
    platforms = release.get("platforms")
    require(isinstance(platforms, dict) and set(platforms) == {"linux", "windows"},
            "Runtime candidate musi zawierać dokładnie linux i windows.")
    for platform in ("linux", "windows"):
        platform_release = platforms[platform]
        expected_files = EXPECTED_RC_FILES[platform]
        files = platform_release.get("files")
        clean = platform_release.get("clean_client", {}).get("files")
        per_platform = platform_release.get("deterministic_rebuild", {})
        payload_determinism = per_platform.get("payloads")
        require(isinstance(files, dict) and set(files) == expected_files,
                f"Runtime {platform} nie zawiera dokładnego 7-plikowego scope'u.")
        require(isinstance(clean, dict) and set(clean) == expected_files,
                f"Runtime {platform} nie zawiera tożsamości czystego klienta.")
        require(per_platform.get("passed") is True
                and per_platform.get("build_count") == 2
                and per_platform.get("component_reports") == "byte_identical"
                and isinstance(payload_determinism, dict)
                and set(payload_determinism) == expected_files
                and set(payload_determinism.values()) == {"byte_identical"},
                f"Runtime {platform} nie ma pełnego deterministycznego rebuildu.")
        for relative in expected_files:
            _require_identity(files[relative], f"patched {platform} {relative}")
            _require_identity(clean[relative], f"clean {platform} {relative}")

        components = platform_release.get("components")
        require(isinstance(components, dict) and set(components) == {
            "database", "dialogue-base", "dialogue-dlc", "font-runtime"
        }, f"Runtime {platform} ma nieprawidłowe inventory komponentów.")
        database = components["database"]
        base_dialogue = components["dialogue-base"]
        dlc_dialogue = components["dialogue-dlc"]
        font_runtime = components["font-runtime"]
        require(database.get("selected_fields") == 5712,
                "BGDatabase nie zawiera dokładnie 5712 pól.")
        require(database.get("selected_fields")
                == database.get("changed_fields") + database.get("identical_fields"),
                "selected_fields != changed_fields + identical_fields.")
        require(database.get("source_strings_and_sha256_matched") is True,
                "Źródła pakietów bazy nie zostały w pełni dopasowane.")
        require(database.get("placeholder_rows_selected") == []
                and database.get("review_required_rows") == 0,
                "Baza zawiera niezaakceptowane wiersze.")
        require(database.get("duplicate_audit", {}).get(
                    "unresolved_conflicting_duplicate_keys") == 0,
                "Baza zawiera nierozwiązane konflikty.")
        require(database.get("non_target_comparison", {}).get("result") == "identical"
                and database.get("structural_verification", {}).get(
                    "tables_fields_rows_unchanged") is True,
                "Baza nie przeszła bramki strukturalnej/non-target.")
        db_deterministic = database.get("deterministic_rebuild", {})
        output_db = database.get("output_database", {})
        require(db_deterministic.get("passed") is True
                and db_deterministic.get("result") == "byte_identical"
                and strip_sha(db_deterministic.get("second_build_sha256", ""))
                    == strip_sha(output_db.get("sha256", "")),
                "Baza nie ma dowodu deterministycznego rebuildu.")
        require(base_dialogue.get("schema") == "echoforge.runtime-dialogue-component/v1"
                and base_dialogue.get("accepted_fields") == 15292
                and base_dialogue.get("unintended_logical_changes") == 0,
                f"Bazowy komponent dialogowy {platform} nie przeszedł bramki.")
        require(dlc_dialogue.get("schema") == "echoforge.runtime-dialogue-component/v1"
                and dlc_dialogue.get("accepted_fields") == 2554
                and dlc_dialogue.get("unintended_logical_changes") == 0,
                f"Komponent DLC {platform} nie przeszedł bramki.")
        expected_font_patch = (
            "ef001-font-polish-windows-v1" if platform == "windows"
            else "ef001-font-polish-v1"
        )
        require(font_runtime.get("patch_id") == expected_font_patch
                and len(font_runtime.get("payloads", [])) == 4,
                f"Manifest fontów {platform} jest nieprawidłowy.")


def _payload_role(payload_name: str) -> str:
    if payload_name == "bansheegz_database.bytes":
        return "reviewed-database-translations"
    if payload_name == "packedassets_assets_all.bundle":
        return "reviewed-dialogue-translations"
    if payload_name.startswith("duplicateassetisolation6_"):
        return "polish-fonts-and-reviewed-dlc-dialogue"
    if payload_name == "catalog.json":
        return "cumulative-addressables-catalog"
    return "polish-fonts"


def prepare_release(args: argparse.Namespace) -> PreparedRelease:
    require(re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.-]{0,39}", args.version) is not None,
            "Nieprawidłowa wersja.")
    echoforge = args.echoforge_root.resolve()
    checkpoint = require_clean_checkpoint(echoforge, repository="EchoForge")
    public_checkpoint = require_clean_checkpoint(ROOT.parent, repository="chained-echoes-pl")
    baseline = load_json(ROOT / "release-baseline.json")
    require(baseline.get("schema") == "echoforge.chained-echoes-installer-baseline/v2",
            "Nieobsługiwany release-baseline.json.")

    require(not args.runtime_manifest.is_symlink(),
            "--runtime-manifest nie może być dowiązaniem symbolicznym.")
    rc_manifest_path = args.runtime_manifest.resolve()
    require(rc_manifest_path.is_file(), f"Brak runtime manifestu: {rc_manifest_path}.")
    require(rc_manifest_path.name == "manifest.json",
            "--runtime-manifest musi wskazywać dokładny manifest.json runtime candidate.")
    rc_dir = rc_manifest_path.parent
    require(rc_dir.parent == echoforge / "var/runtime-patches",
            "Runtime manifest musi pochodzić z EchoForge/var/runtime-patches/<runtime_id>.")
    rc = load_json(rc_manifest_path)
    require(rc_dir.name == rc.get("runtime_id"),
            "Nazwa katalogu runtime candidate różni się od runtime_id.")
    validate_runtime_release(rc, version=args.version, echoforge_commit=checkpoint)
    db = rc["platforms"]["linux"]["components"]["database"]

    selected = db["selected_fields"]
    changed = db["changed_fields"]
    identical = db["identical_fields"]
    reset_generated_directory(EVIDENCE_DIR)
    baseline_platforms = baseline.get("platforms")
    require(isinstance(baseline_platforms, dict)
            and set(baseline_platforms) == {"linux", "windows"},
            "Baseline musi opisywać dokładnie linux i windows.")
    release_platforms: dict[str, dict[str, Any]] = {}
    for platform in ("linux", "windows"):
        runtime_platform = rc["platforms"][platform]
        clean_files = runtime_platform["clean_client"]["files"]
        patched_files = runtime_platform["files"]
        baseline_files = baseline_platforms[platform].get("files")
        require(isinstance(baseline_files, dict)
                and set(baseline_files) == EXPECTED_RC_FILES[platform],
                f"Baseline {platform} nie ma dokładnego clean-client inventory.")
        entries: list[dict[str, Any]] = []
        for relative in sorted(EXPECTED_RC_FILES[platform]):
            source_identity = _require_identity(clean_files[relative], relative)
            output_identity = _require_identity(patched_files[relative], relative)
            expected_source = _require_identity(baseline_files[relative], relative)
            require(strip_sha(source_identity["sha256"])
                    == strip_sha(expected_source["sha256"])
                    and source_identity["size"] == expected_source["size"]
                    and source_identity["file_crc32"] == expected_source["file_crc32"],
                    f"Clean-client identity {platform} różni się od baseline: {relative}.")
            payload_path = rc_dir / relative
            require(not payload_path.is_symlink(), f"Payload nie może być symlinkiem: {relative}.")
            verify_file(payload_path, output_identity["sha256"], output_identity["size"])
            payload_name = Path(relative).name
            current = {
                "relative_path": relative,
                "payload_name": payload_name,
                "role": _payload_role(payload_name),
                "source_sha256": strip_sha(source_identity["sha256"]),
                "source_size": source_identity["size"],
                "source_crc32": source_identity["file_crc32"],
                "output_sha256": strip_sha(output_identity["sha256"]),
                "output_size": output_identity["size"],
                "output_crc32": output_identity["file_crc32"],
            }
            if payload_name == "bansheegz_database.bytes":
                current["deterministic_rebuild"] = "byte-identical"
                current["non_target_comparison"] = "identical"
            if payload_name == "catalog.json":
                current["installed_last"] = True
            entries.append(current)
        require(len({entry["payload_name"] for entry in entries}) == 7,
                f"Payload names {platform} nie są unikalne.")
        release_platforms[platform] = {"files": entries}

    evidence_sources = [(rc_manifest_path, EVIDENCE_DIR / "runtime-release-manifest.json")]
    for source, target in evidence_sources:
        require(source.is_file(), f"Brak evidence: {source}")
        copy_file(source, target)
    evidence = [evidence_entry(target) for _, target in evidence_sources]

    per_table = db.get("per_table_counts")
    require(isinstance(per_table, dict) and sum(per_table.values()) == selected,
            "Liczniki per_table nie zgadzają się z selected_fields.")
    manifest = {
        "schema": "echoforge.chained-echoes-cumulative-release/v3",
        "release_id": f"chained-echoes-polish-ef001-{args.version}",
        "version": args.version,
        "status": "windows-kubuntu-release-candidate",
        "game": baseline["game"],
        "source_checkpoint": {
            "echoforge": {
                "repository": "EchoForge",
                "ref": "refs/heads/main",
                "commit": checkpoint,
                "runtime_id": rc["runtime_id"],
                "runtime_manifest_sha256": sha256(EVIDENCE_DIR / "runtime-release-manifest.json"),
                "acceptance_manifest_sha256": strip_sha(rc["acceptance"]["sha256"]),
            },
            "release_repository": {
                "repository": "chained-echoes-pl",
                "ref": "refs/heads/main",
                "commit": public_checkpoint,
            },
        },
        "evidence_files": evidence,
        "translation_scope": {
            "dialogue_fields": rc["acceptance"]["accepted_fields"],
            "dialogue_conversations": "pełny canonical EF-001R (1807 checkpointów)",
            "database_selected_fields": selected,
            "database_changed_fields": changed,
            "database_identical_fields": identical,
            "per_table": dict(sorted(per_table.items())),
        },
        "platforms": release_platforms,
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
    return PreparedRelease(manifest=manifest, runtime_dir=rc_dir)


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


def stage_platform_payloads(prepared: PreparedRelease, platform: str) -> None:
    require(platform in {"windows", "linux"}, f"Nieobsługiwana platforma: {platform}.")
    reset_generated_directory(PAYLOAD_DIR)
    entries = prepared.manifest["platforms"][platform]["files"]
    for entry in entries:
        source = prepared.runtime_dir / entry["relative_path"]
        verify_file(source, entry["output_sha256"], entry["output_size"])
        copy_file(source, PAYLOAD_DIR / entry["payload_name"])
    PAYLOAD_DIR.joinpath("release-manifest.json").write_text(
        json.dumps(prepared.manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def build_packages(prepared: PreparedRelease) -> list[Path]:
    version = prepared.manifest["version"]
    for path in [DIST_DIR / "publish", DIST_DIR / "package"]:
        shutil.rmtree(path, ignore_errors=True)
    DIST_DIR.mkdir(parents=True, exist_ok=True)
    run([sys.executable, "-m", "unittest", "discover", "-s", "tests",
         "-p", "test_*.py", "-v"])
    archives: list[Path] = []
    for platform, runtime, binary_name in [
        ("windows", "win-x64", "ChainedEchoesPolishInstaller.exe"),
        ("linux", "linux-x64", "ChainedEchoesPolishInstaller"),
    ]:
        stage_platform_payloads(prepared, platform)
        run(["dotnet", "build", "src/Installer.Win/Installer.Win.csproj",
             "-c", "Release", "-t:Rebuild", f"-p:Version={version}"])
        run(["dotnet", "run", "--project",
             "tests/Installer.SelfTest/Installer.SelfTest.csproj", "-c", "Release"],
            environment={"CE_INSTALLER_TEST_PLATFORM": platform})
        run(["dotnet", "publish", "src/Installer.Win/Installer.Win.csproj",
             "-c", "Release", "-r", runtime, "--self-contained", "true", "-t:Rebuild",
             f"-p:Version={version}", "-o", str(DIST_DIR / "publish" / runtime)])
        archives.append(package_platform(version, runtime, binary_name))
    return archives


def write_release_notes(manifest: dict[str, Any], archives: list[Path]) -> Path:
    scope = manifest["translation_scope"]
    table_lines = "\n".join(
        f"- `{name}`: {count}" for name, count in scope["per_table"].items())
    asset_lines = "\n".join(
        f"- `{archive.name}` — SHA-256 `{sha256(archive)}`" for archive in archives)
    notes = f"""# Chained Echoes PL {manifest['version']}

Kandydat wydania do ręcznego smoke testu na Windows x64 i Kubuntu x64.

Ulepszenia instalatora:

- ręcznie wklejane ścieżki Windows akceptują spacje i cudzysłowy;
- instalator automatycznie sprawdza wszystkie biblioteki skonfigurowane w Steam
  i odczytuje rzeczywisty katalog gry z manifestu App 1229240;
- Windows używa natywnego katalogu `Chained Echoes_Data`, a Linux
  `Chained_Echoes_Data`; instalacja, weryfikacja i rollback zachowują ten podział.
- każdy ZIP zawiera własny zestaw 7 natywnych payloadów; bundle'e Windows
  i Linux nie są traktowane jako zamienne.

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


def validate_publish_identity(
    manifest: dict[str, Any], *, head: str, branch: str, origin_main: str
) -> str:
    source = manifest.get("source_checkpoint", {}).get("release_repository", {})
    expected = source.get("commit")
    require(source.get("ref") == "refs/heads/main",
            "Zbudowany manifest nie wskazuje canonical chained-echoes-pl/main.")
    require(isinstance(expected, str) and re.fullmatch(r"[0-9a-f]{40}", expected) is not None,
            "Zbudowany manifest nie zawiera exact public source SHA.")
    require(branch == "main", "Publikacja wymaga canonical chained-echoes-pl/main.")
    require(head == expected, "HEAD różni się od SHA użytego do budowy instalatorów.")
    require(origin_main == expected, "origin/main różni się od SHA użytego do budowy instalatorów.")
    return expected


def _optional(command: list[str], *, cwd: Path = ROOT.parent) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, cwd=cwd, text=True, capture_output=True, check=False)


def _remote_tag_target(tag: str) -> str | None:
    output = run(
        ["git", "ls-remote", "--tags", "origin", f"refs/tags/{tag}", f"refs/tags/{tag}^{{}}"],
        cwd=ROOT.parent,
        capture=True,
    )
    rows = [line.split() for line in output.splitlines() if line.strip()]
    peeled = [sha for sha, ref in rows if ref == f"refs/tags/{tag}^{{}}"]
    direct = [sha for sha, ref in rows if ref == f"refs/tags/{tag}"]
    targets = peeled or direct
    require(len(targets) <= 1, f"Remote tag {tag} ma niejednoznaczny target.")
    return targets[0] if targets else None


def ensure_exact_remote_tag(tag: str, source_sha: str) -> None:
    remote_target = _remote_tag_target(tag)
    require(remote_target in {None, source_sha},
            f"Istniejący remote tag {tag} wskazuje inne SHA; tag nie zostanie zmieniony.")
    local = _optional(["git", "rev-parse", "-q", "--verify", f"refs/tags/{tag}^{{commit}}"])
    if local.returncode != 0 and remote_target is not None:
        run(["git", "fetch", "origin", f"refs/tags/{tag}:refs/tags/{tag}"], cwd=ROOT.parent)
        local = _optional(["git", "rev-parse", "-q", "--verify", f"refs/tags/{tag}^{{commit}}"])
    if local.returncode == 0:
        require(local.stdout.strip() == source_sha,
                f"Istniejący lokalny tag {tag} wskazuje inne SHA; tag nie zostanie zmieniony.")
    else:
        require(remote_target is None, f"Nie można potwierdzić targetu tagu {tag}.")
        run(["git", "tag", "-a", tag, source_sha, "-m", f"Chained Echoes PL {tag}"],
            cwd=ROOT.parent)
    if remote_target is None:
        run(["git", "push", "origin", f"refs/tags/{tag}:refs/tags/{tag}"], cwd=ROOT.parent)
    require(_remote_tag_target(tag) == source_sha,
            f"Remote tag {tag} nie wskazuje exact release source SHA po pushu.")


def publish(
    args: argparse.Namespace,
    manifest: dict[str, Any],
    notes: Path,
    archives: list[Path],
) -> None:
    tag = f"v{args.version}"
    status = run(["git", "status", "--porcelain"], cwd=ROOT.parent, capture=True)
    require(not status, "Publiczne repo ma niezacommitowane zmiany; publikacja zatrzymana.")
    branch = run(["git", "symbolic-ref", "--short", "HEAD"], cwd=ROOT.parent, capture=True)
    head = run(["git", "rev-parse", "HEAD"], cwd=ROOT.parent, capture=True)
    run(["git", "fetch", "origin", "main", "--tags"], cwd=ROOT.parent)
    origin_main = run(["git", "rev-parse", "refs/remotes/origin/main"], cwd=ROOT.parent, capture=True)
    source_sha = validate_publish_identity(
        manifest, head=head, branch=branch, origin_main=origin_main
    )
    remote_url = run(["git", "remote", "get-url", "origin"], cwd=ROOT.parent, capture=True)
    require(args.repo in remote_url or remote_url.endswith(args.repo + ".git"),
            "--repo nie odpowiada origin publicznego repozytorium.")
    existing_release = _optional(["gh", "release", "view", tag, "--repo", args.repo])
    require(existing_release.returncode != 0,
            f"GitHub release {tag} już istnieje; publikacja nie zmieni istniejącego release'u.")
    ensure_exact_remote_tag(tag, source_sha)
    assets: list[str] = []
    for archive in archives:
        assets.extend([str(archive), str(archive.with_suffix(archive.suffix + ".sha256"))])
    run(["gh", "release", "create", tag, "--repo", args.repo, "--prerelease",
         "--verify-tag", "--target", source_sha,
         "--title", f"Chained Echoes PL {args.version}", "--notes-file", str(notes), *assets])


def main() -> int:
    args = parse_args()
    try:
        prepared = prepare_release(args)
        manifest = prepared.manifest
        first = build_packages(prepared)
        first_hashes = {path.name: sha256(path) for path in first}
        if not args.no_determinism_check:
            second = build_packages(prepared)
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
            require(not args.no_determinism_check,
                    "Publikacja jest zabroniona po --no-determinism-check.")
            publish(args, manifest, notes, archives)
            print(f"Opublikowano prerelease v{args.version} w {args.repo}")
        return 0
    except (ReleaseError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        print(f"BŁĄD WYDANIA: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

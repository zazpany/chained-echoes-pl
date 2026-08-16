from __future__ import annotations

import hashlib
import importlib.util
import tempfile
import unittest
import zipfile
from unittest import mock
from pathlib import Path
from types import SimpleNamespace


MODULE_PATH = Path(__file__).resolve().parents[1] / "tools" / "release.py"
SPEC = importlib.util.spec_from_file_location("installer_release", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)


class ReleaseFactoryTests(unittest.TestCase):
    def valid_runtime_manifest(self, commit: str = "a" * 40) -> dict[str, object]:
        identity = {"sha256": "sha256:" + "1" * 64, "size": 1, "file_crc32": 1}
        files = {relative: dict(identity) for relative in release.EXPECTED_RC_FILES}
        database = {
            "selected_fields": 5712,
            "changed_fields": 5700,
            "identical_fields": 12,
            "source_strings_and_sha256_matched": True,
            "placeholder_rows_selected": [],
            "review_required_rows": 0,
            "duplicate_audit": {"unresolved_conflicting_duplicate_keys": 0},
            "non_target_comparison": {"result": "identical"},
            "structural_verification": {"tables_fields_rows_unchanged": True},
            "deterministic_rebuild": {
                "passed": True,
                "result": "byte_identical",
                "second_build_sha256": "sha256:" + "2" * 64,
            },
            "output_database": {"sha256": "sha256:" + "2" * 64},
        }
        return {
            "schema": release.RUNTIME_SCHEMA,
            "runtime_id": release.RUNTIME_PREFIX + "0.8.0",
            "version": "0.8.0",
            "source": {"repository": "EchoForge", "ref": "refs/heads/main", "commit": commit},
            "installation_enabled": False,
            "runtime_acceptance": {"required": True, "status": "pending_manual_smoke"},
            "acceptance": {
                "accepted_checkpoints": 1807,
                "accepted_fields": 17846,
                "remaining_checkpoints": 0,
                "excluded_raw_records": 22,
                "sha256": "sha256:" + "3" * 64,
                "dialogue_source": {
                    "label": "dialogue_search_index.jsonl",
                    "records": 17868,
                    "sha256": "sha256:" + "4" * 64,
                },
            },
            "deterministic_rebuild": {
                "passed": True,
                "build_count": 2,
                "component_reports": "byte_identical",
                "payloads": {relative: "byte_identical" for relative in release.EXPECTED_RC_FILES},
            },
            "files": files,
            "clean_client": {"files": files},
            "rollback": {"required": True, "catalog_install_order": "last", "catalog_restore_order": "first"},
            "components": {
                "database": database,
                "dialogue-base": {"schema": "echoforge.runtime-dialogue-component/v1", "accepted_fields": 15292, "unintended_logical_changes": 0},
                "dialogue-dlc": {"schema": "echoforge.runtime-dialogue-component/v1", "accepted_fields": 2554, "unintended_logical_changes": 0},
                "font-runtime": {"patch_id": "ef001-font-polish-v1", "payloads": [{}, {}, {}, {}]},
            },
        }

    def test_strip_sha_accepts_prefixed_and_plain_digest(self) -> None:
        digest = "a" * 64
        self.assertEqual(release.strip_sha(digest), digest)
        self.assertEqual(release.strip_sha("sha256:" + digest), digest)

    def test_verify_file_checks_hash_and_size(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "payload"
            data = b"verified payload"
            path.write_bytes(data)
            digest = hashlib.sha256(data).hexdigest()
            release.verify_file(path, digest, len(data))
            with self.assertRaises(release.ReleaseError):
                release.verify_file(path, "0" * 64, len(data))
            with self.assertRaises(release.ReleaseError):
                release.verify_file(path, digest, len(data) + 1)

    def test_generated_staging_reset_removes_stale_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            generated = root / "installer" / "Payload"
            generated.mkdir(parents=True)
            (generated / "stale-nd7.json").write_text("stale", encoding="utf-8")
            with mock.patch.object(release, "ROOT", root / "installer"):
                release.reset_generated_directory(generated)
            self.assertTrue(generated.is_dir())
            self.assertEqual(list(generated.iterdir()), [])

    def test_deterministic_zip_is_byte_identical_and_preserves_exec_mode(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source"
            source.mkdir()
            (source / "README.md").write_text("zażółć\n", encoding="utf-8")
            executable = source / "ChainedEchoesPolishInstaller"
            executable.write_bytes(b"binary")
            first = root / "first.zip"
            second = root / "second.zip"
            release.deterministic_zip(source, first)
            release.deterministic_zip(source, second)
            self.assertEqual(first.read_bytes(), second.read_bytes())
            with zipfile.ZipFile(first) as archive:
                info = archive.getinfo("ChainedEchoesPolishInstaller")
                self.assertEqual((info.external_attr >> 16) & 0o777, 0o755)
                self.assertEqual(info.date_time, release.FIXED_ZIP_TIME)

    def test_release_notes_describe_forgiving_windows_path_input(self) -> None:
        manifest = {
            "version": "0.8.1",
            "game": {"steam_build_id": "21147362"},
            "translation_scope": {
                "dialogue_conversations": "pełny canonical EF-001R",
                "dialogue_fields": 17846,
                "database_selected_fields": 5712,
                "database_changed_fields": 5303,
                "database_identical_fields": 409,
                "per_table": {"tr_MENU": 986},
            },
        }
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / "installer.zip"
            archive.write_bytes(b"zip")
            with mock.patch.object(release, "DIST_DIR", root):
                notes = release.write_release_notes(manifest, [archive]).read_text(
                    encoding="utf-8"
                )
        self.assertIn("ścieżki Windows akceptują spacje i cudzysłowy", notes)
        self.assertIn("`Chained Echoes_Data`", notes)
        self.assertIn("`Chained_Echoes`", notes)

    def test_publish_identity_rejects_wrong_sha_or_ref(self) -> None:
        manifest = {
            "source_checkpoint": {
                "release_repository": {
                    "ref": "refs/heads/main",
                    "commit": "a" * 40,
                }
            }
        }
        with self.assertRaises(release.ReleaseError):
            release.validate_publish_identity(
                manifest, head="b" * 40, branch="main", origin_main="a" * 40
            )
        with self.assertRaises(release.ReleaseError):
            release.validate_publish_identity(
                manifest, head="a" * 40, branch="feature", origin_main="a" * 40
            )
        with self.assertRaises(release.ReleaseError):
            release.validate_publish_identity(
                manifest, head="a" * 40, branch="main", origin_main="b" * 40
            )

    def test_publish_uses_verified_tag_and_exact_target_sha(self) -> None:
        source_sha = "a" * 40
        manifest = {
            "source_checkpoint": {
                "release_repository": {
                    "ref": "refs/heads/main",
                    "commit": source_sha,
                }
            }
        }
        args = SimpleNamespace(version="0.8.0", repo="zazpany/chained-echoes-pl")
        commands: list[list[str]] = []

        def fake_run(command: list[str], **kwargs: object) -> str:
            commands.append(command)
            if command[:3] == ["git", "status", "--porcelain"]:
                return ""
            if command[:3] == ["git", "symbolic-ref", "--short"]:
                return "main"
            if command[:3] == ["git", "rev-parse", "HEAD"]:
                return source_sha
            if command[:3] == ["git", "rev-parse", "refs/remotes/origin/main"]:
                return source_sha
            if command[:3] == ["git", "remote", "get-url"]:
                return "https://github.com/zazpany/chained-echoes-pl.git"
            return ""

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            notes = root / "notes.md"
            notes.write_text("notes", encoding="utf-8")
            archive = root / "release.zip"
            archive.write_bytes(b"zip")
            archive.with_suffix(".zip.sha256").write_text("sum", encoding="utf-8")
            with (
                mock.patch.object(release, "run", side_effect=fake_run),
                mock.patch.object(release, "_optional", return_value=SimpleNamespace(returncode=1)),
                mock.patch.object(release, "ensure_exact_remote_tag") as ensure_tag,
            ):
                release.publish(args, manifest, notes, [archive])
        ensure_tag.assert_called_once_with("v0.8.0", source_sha)
        gh_command = next(command for command in commands if command[:3] == ["gh", "release", "create"])
        self.assertIn("--verify-tag", gh_command)
        self.assertEqual(gh_command[gh_command.index("--target") + 1], source_sha)

    def test_historical_nd7_manifest_is_not_a_supported_runtime_source(self) -> None:
        with self.assertRaises(release.ReleaseError):
            release.validate_runtime_release(
                {"schema": "echoforge.nd7-release-candidate/v1alpha1"},
                version="0.8.0",
                echoforge_commit="a" * 40,
            )

    def test_full_versioned_runtime_manifest_passes_factory_gate(self) -> None:
        release.validate_runtime_release(
            self.valid_runtime_manifest(), version="0.8.0", echoforge_commit="a" * 40
        )
        with self.assertRaises(release.ReleaseError):
            release.validate_runtime_release(
                self.valid_runtime_manifest(), version="0.8.0", echoforge_commit="b" * 40
            )


if __name__ == "__main__":
    unittest.main()

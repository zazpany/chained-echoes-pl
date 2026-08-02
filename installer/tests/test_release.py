from __future__ import annotations

import hashlib
import importlib.util
import tempfile
import unittest
import zipfile
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "tools" / "release.py"
SPEC = importlib.util.spec_from_file_location("installer_release", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)


class ReleaseFactoryTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()

import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch

import run_campaign


class AcquisitionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = {
            "source": "sqlglot",
            "repository": "tobymao/sqlglot",
            "revision": "a" * 40,
            "license": "MIT",
            "licensePath": "LICENSE",
            "include": ["tests", "LICENSE"],
        }

    def archive(self, entries):
        buffer = io.BytesIO()
        with tarfile.open(fileobj=buffer, mode="w") as archive:
            for name, content, kind in entries:
                member = tarfile.TarInfo(name)
                member.type = kind
                member.size = len(content)
                archive.addfile(member, io.BytesIO(content))
        buffer.seek(0)
        archive = tarfile.open(fileobj=buffer, mode="r")
        self.addCleanup(archive.close)
        self.addCleanup(buffer.close)
        return archive

    def test_extracts_only_selected_inert_files(self):
        prefix = "sqlglot-" + self.source["revision"]
        archive = self.archive([
            (prefix + "/tests/input.py", b"SELECT 1", tarfile.REGTYPE),
            (prefix + "/LICENSE", b"MIT license", tarfile.REGTYPE),
            (prefix + "/unrelated/file", b"ignored", tarfile.REGTYPE),
        ])
        run_campaign.extract_archive(archive, self.root, self.source)
        self.assertEqual(b"SELECT 1", (self.root / "tests/input.py").read_bytes())
        self.assertTrue((self.root / "LICENSE").is_file())
        self.assertFalse((self.root / "unrelated").exists())

    def test_rejects_traversal_wrong_revisions_and_symlinks(self):
        prefix = "sqlglot-" + self.source["revision"]
        for name, kind in [
            (prefix + "/tests/../../outside", tarfile.REGTYPE),
            ("sqlglot-wrong/tests/input.py", tarfile.REGTYPE),
            (prefix + "/tests/link", tarfile.SYMTYPE),
            ("/absolute/tests/input.py", tarfile.REGTYPE),
        ]:
            with self.subTest(name=name), self.assertRaises(ValueError):
                run_campaign.extract_archive(
                    self.archive([(name, b"", kind)]), self.root, self.source
                )

    def test_rejects_archive_size_over_budget(self):
        prefix = "sqlglot-" + self.source["revision"]
        archive = self.archive([(prefix + "/tests/input.py", b"123", tarfile.REGTYPE)])
        with patch.object(run_campaign, "MAX_SOURCE_BYTES", 2), self.assertRaises(ValueError):
            run_campaign.extract_archive(archive, self.root, self.source)

    def test_offline_cache_requires_exact_contents(self):
        target = self.root / ("sqlglot-" + self.source["revision"])
        target.mkdir()
        (target / "LICENSE").write_text("MIT license", encoding="utf-8")
        run_campaign.write_json(target / ".source.json", {
            "source": self.source,
            "files": run_campaign.file_hashes(target),
        })
        self.assertEqual(target, run_campaign.acquire_source(self.source, self.root, offline=True))
        self.assertEqual(target, run_campaign.acquire_source(
            self.source | {"expectedCandidates": 720}, self.root, offline=True
        ))
        (target / "extra.sql").write_text("SELECT 1", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "cache changed"):
            run_campaign.acquire_source(self.source, self.root, offline=True)

    def test_offline_mode_never_downloads_missing_sources(self):
        with patch("urllib.request.urlopen") as download, self.assertRaisesRegex(ValueError, "not cached"):
            run_campaign.acquire_source(self.source, self.root, offline=True)
        download.assert_not_called()

    def test_duplicate_ids_fail_instead_of_shrinking_denominator(self):
        fake_case = {"id": "duplicate"}
        with patch.object(run_campaign, "acquire_source", return_value=self.root), patch.dict(
            run_campaign.EXTRACTORS, {"sqlglot": lambda _: ([fake_case, fake_case], [])}
        ), self.assertRaisesRegex(ValueError, "duplicate case IDs"):
            run_campaign.build_corpus([self.source], self.root)

    def test_pinned_candidate_and_exclusion_counts_are_enforced(self):
        for key in ("expectedCandidates", "expectedExclusions"):
            with self.subTest(key=key), patch.object(
                run_campaign, "acquire_source", return_value=self.root
            ), patch.dict(
                run_campaign.EXTRACTORS, {"sqlglot": lambda _: ([{"id": "one"}], [])}
            ), self.assertRaisesRegex(ValueError, key):
                run_campaign.build_corpus([self.source | {key: 2}], self.root)

    def test_manifest_pins_both_sources_and_their_denominators(self):
        manifest = json.loads((run_campaign.HERE / "sources.json").read_text(encoding="utf-8"))
        self.assertEqual({"sqlglot", "scriptdom"}, {source["source"] for source in manifest["sources"]})
        for source in manifest["sources"]:
            self.assertRegex(source["revision"], r"^[0-9a-f]{40}$")
            self.assertGreater(source["expectedCandidates"], 0)
            self.assertGreaterEqual(source["expectedExclusions"], 0)
            self.assertIn(source["licensePath"], source["include"])

    def test_json_preserves_unicode_and_newlines(self):
        path = self.root / "data.json"
        value = {"sql": "SELECT N'\u00e9';\nGO\n"}
        run_campaign.write_json(path, value)
        self.assertEqual(value, json.loads(path.read_text(encoding="utf-8")))


if __name__ == "__main__":
    unittest.main()

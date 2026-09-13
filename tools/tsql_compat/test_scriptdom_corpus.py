"""Synthetic, offline tests for original ScriptDom fixture extraction."""

import codecs
import errno
from pathlib import Path
import shutil
from tempfile import TemporaryDirectory
import unittest
from unittest import mock

from tools.tsql_compat.scriptdom_corpus import extract


class ScriptDomCorpusTests(unittest.TestCase):
    def setUp(self):
        temporary = TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def write(self, relative_path, data=b"SELECT 1;\n"):
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def test_originals_instead_of_versioned_generated_baselines(self):
        original = "Test/SqlDom/TestScripts/Select.sql"
        self.write(original, b"select  1\nGO\n")
        baseline_directories = (
            "Baselines80", "Baselines90", "Baselines160", "Baselines180",
            "BaselinesCommon", "BaselinesFabricDW",
        )
        for directory in reversed(baseline_directories):
            self.write(f"Test/SqlDom/{directory}/Select.sql", b"\xff")

        candidates, exclusions = extract(self.root)

        self.assertEqual([original], [item["path"] for item in candidates])
        self.assertEqual("select  1\nGO\n", candidates[0]["sql"])
        self.assertEqual(len(baseline_directories), len(exclusions))
        for item in exclusions:
            self.assertIn("generated SQL comparison baseline", item["reason"])

    def test_selection_uses_root_category_not_filename_or_nested_directory(self):
        paths = [
            "Test/SqlDom/TestScripts/Baselines160.sql",
            "Test/SqlDom/TestScripts/nested/Baselines80/Select.sql",
            "Test/SqlDom/TestScripts/nested/Upper.SQL",
            "Test/SqlDom/PhaseOneTestScripts/nested/Recovery.sql",
        ]
        for path in paths:
            self.write(path)

        candidates, exclusions = extract(self.root)

        self.assertEqual(sorted(paths), [item["path"] for item in candidates])
        self.assertEqual([], exclusions)

    def test_unclassified_sql_is_explicit_but_non_parser_trees_are_out_of_scope(self):
        unknown_paths = [
            "Test/SqlDom/ScriptGenerator/Expected.sql",
            "Test/SqlDom/UnrecognizedInputs/Error.sql",
            "Test/SqlDom/Loose.sql",
        ]
        for path in unknown_paths:
            self.write(path)
        self.write("Test/SqlDom/TestScripts/not-sql.txt")
        self.write("docs/TestScripts/example.sql")

        candidates, exclusions = extract(self.root)

        self.assertEqual([], candidates)
        self.assertEqual(sorted(unknown_paths), [item["path"] for item in exclusions])
        for item in exclusions:
            self.assertIn("parser-input role not established", item["reason"])

    def test_invalid_lexer_recovery_and_empty_inputs_remain_candidates(self):
        inputs = {
            "Test/SqlDom/TestScripts/MultipleErrorTests.sql": b"SELECT FROM;\nGO\n",
            "Test/SqlDom/TestScripts/GetTokenTypesFailureTests.sql": b"'unclosed",
            "Test/SqlDom/PhaseOneTestScripts/CreateTable.sql": (
                b"This is not a valid TSql Statement.\r\n\r\ncreate table dbo.t1"
            ),
            "Test/SqlDom/TestScripts/ZeroLengthFile.sql": b"",
            "Test/SqlDom/TestScripts/CommentOnly.sql": b"-- no statements\n",
        }
        for path, data in inputs.items():
            self.write(path, data)

        candidates, exclusions = extract(self.root)

        self.assertEqual([], exclusions)
        self.assertEqual(len(inputs), len(candidates))
        for item in candidates:
            self.assertEqual(inputs[item["path"]].decode("utf-8"), item["sql"])
            if "/PhaseOneTestScripts/" in item["path"]:
                self.assertIn("phase-one/recovery", item["context"])
            else:
                self.assertIn("parser/lexer input", item["context"])

    def test_utf8_with_and_without_bom_preserves_non_ascii_text(self):
        sql = "SELECT N'caf\u00e9 \u6c49 \U0001f642 \ufeff';\r\nGO\n"
        self.write("Test/SqlDom/TestScripts/Plain.sql", sql.encode("utf-8"))
        self.write(
            "Test/SqlDom/TestScripts/Bom.sql",
            codecs.BOM_UTF8 + sql.encode("utf-8"),
        )

        candidates, exclusions = extract(self.root)

        self.assertEqual([], exclusions)
        self.assertEqual([sql, sql], [item["sql"] for item in candidates])

    def test_utf16_both_byte_orders_with_and_without_bom(self):
        sql = "-- original\r\nSELECT N'caf\u00e9 \u6c49 \U0001f642 \ufeff';\rGO\n"
        for encoding, bom in (
            ("utf-16-le", codecs.BOM_UTF16_LE),
            ("utf-16-be", codecs.BOM_UTF16_BE),
        ):
            self.write(
                f"Test/SqlDom/TestScripts/{encoding}-plain.sql",
                sql.encode(encoding),
            )
            self.write(
                f"Test/SqlDom/TestScripts/{encoding}-bom.sql",
                bom + sql.encode(encoding),
            )

        candidates, exclusions = extract(self.root)

        self.assertEqual([], exclusions)
        self.assertEqual([sql] * 4, [item["sql"] for item in candidates])

    def test_mixed_newlines_whitespace_and_go_are_not_normalized_or_split(self):
        sql = (
            "\r\n-- original whitespace\r"
            "SET QUOTED_IDENTIFIER OFF;\n"
            "SELECT 'GO;';\r\nGO 3\r\n"
            "CREATE PROCEDURE p AS BEGIN SELECT 1; SELECT 2; END;\n"
            "GO\nGO\r\n\t "
        )
        self.write("Test/SqlDom/TestScripts/Batches.sql", sql.encode("utf-8"))

        candidates, exclusions = extract(self.root)

        self.assertEqual([], exclusions)
        self.assertEqual(1, len(candidates))
        self.assertEqual(sql, candidates[0]["sql"])
        self.assertEqual(1, candidates[0]["line"])

    def test_isolated_nul_in_utf8_is_not_mistaken_for_utf16(self):
        sql = "SELECT N'a\x00b';\n"
        self.write("Test/SqlDom/TestScripts/Nul.sql", sql.encode("utf-8"))

        candidates, exclusions = extract(self.root)

        self.assertEqual([], exclusions)
        self.assertEqual(sql, candidates[0]["sql"])

    def test_decode_failures_are_explicit_without_replacement_or_codepage_guessing(self):
        invalid_inputs = {
            "LegacyBytes.sql": b"SELECT N'randomstring\xbd\xc1?\xc7\xb6\xae';",
            "InvalidUtf8.sql": codecs.BOM_UTF8 + b"SELECT '\xff';",
            "TruncatedUtf16.sql": codecs.BOM_UTF16_LE + b"S",
            "SurrogateUtf16.sql": (
                codecs.BOM_UTF16_BE + "SELECT '".encode("utf-16-be")
                + b"\xd8\x00" + "';".encode("utf-16-be")
            ),
            "TruncatedBomlessUtf16.sql": "SELECT 1;".encode("utf-16-le")[:-1],
            "SurrogateBomlessUtf16.sql": (
                "SELECT '".encode("utf-16-be") + b"\xd8\x00"
                + "';".encode("utf-16-be")
            ),
        }
        for name, data in invalid_inputs.items():
            self.write(f"Test/SqlDom/TestScripts/{name}", data)
        self.write("Test/SqlDom/TestScripts/Valid.sql")

        candidates, exclusions = extract(self.root)

        self.assertEqual(["SELECT 1;\n"], [item["sql"] for item in candidates])
        self.assertEqual(len(invalid_inputs), len(exclusions))
        self.assertEqual(
            sorted(invalid_inputs),
            [Path(item["path"]).name for item in exclusions],
        )
        for item in exclusions:
            self.assertTrue(item["reason"].startswith("decode failure:"))
            self.assertNotIn("\ufffd", item["reason"])

    def test_utf32_bom_is_reported_instead_of_misread_as_utf16(self):
        for encoding, bom in (
            ("utf-32-le", codecs.BOM_UTF32_LE),
            ("utf-32-be", codecs.BOM_UTF32_BE),
        ):
            self.write(
                f"Test/SqlDom/TestScripts/{encoding}.sql",
                bom + "SELECT 1;".encode(encoding),
            )

        candidates, exclusions = extract(self.root)

        self.assertEqual([], candidates)
        self.assertEqual(2, len(exclusions))
        for item in exclusions:
            self.assertIn("decode failure: UTF-32", item["reason"])

    def test_order_ids_provenance_and_schema_are_stable_across_roots(self):
        paths = [
            "Test/SqlDom/TestScripts/z.sql",
            "Test/SqlDom/TestScripts/a.sql",
            "Test/SqlDom/PhaseOneTestScripts/a.sql",
            "Test/SqlDom/TestScripts/nested/a.sql",
        ]
        for path in paths:
            self.write(path)
        self.write("Test/SqlDom/Baselines160/z.sql")
        self.write("Test/SqlDom/Baselines80/a.sql")
        expected = extract(self.root)
        self.assertEqual(expected, extract(self.root))
        with TemporaryDirectory() as relocated:
            copy = Path(relocated) / "different-checkout-name"
            shutil.copytree(self.root, copy)
            self.assertEqual(expected, extract(copy))

        candidates, exclusions = expected
        self.assertEqual(sorted(paths), [item["path"] for item in candidates])
        self.assertEqual(len(paths), len({item["id"] for item in candidates}))
        for item in candidates:
            self.assertEqual(
                {"id", "source", "path", "line", "sql", "context"}, set(item)
            )
            self.assertEqual(f"scriptdom:{item['path']}", item["id"])
            self.assertEqual("scriptdom", item["source"])
            self.assertIs(type(item["line"]), int)
            self.assertEqual(1, item["line"])
            self.assertIsInstance(item["context"], str)
            self.assertNotIn("\\", item["path"])
            self.assertFalse(Path(item["path"]).is_absolute())
        self.assertEqual(
            sorted(item["path"] for item in exclusions),
            [item["path"] for item in exclusions],
        )
        for item in exclusions:
            self.assertEqual(
                {"source", "path", "line", "context", "reason"}, set(item)
            )
            self.assertEqual("scriptdom", item["source"])
            self.assertEqual(1, item["line"])

    def test_individual_read_failure_is_an_exclusion(self):
        unreadable = self.write("Test/SqlDom/TestScripts/Unreadable.sql")
        self.write("Test/SqlDom/TestScripts/Valid.sql")
        original_read_bytes = Path.read_bytes

        def read_bytes(path):
            if path == unreadable:
                raise PermissionError(errno.EACCES, "fixture unreadable", str(path))
            return original_read_bytes(path)

        with mock.patch.object(
            Path, "read_bytes", autospec=True, side_effect=read_bytes
        ):
            candidates, exclusions = extract(self.root)

        self.assertEqual(1, len(candidates))
        self.assertEqual(1, len(exclusions))
        self.assertEqual(
            "Test/SqlDom/TestScripts/Unreadable.sql", exclusions[0]["path"]
        )
        self.assertEqual(
            "read failure (PermissionError): fixture unreadable",
            exclusions[0]["reason"],
        )

    def test_symlink_files_and_directories_are_explicitly_excluded(self):
        target = self.write("not-parser-inputs/External.sql")
        fixture_root = self.root / "Test/SqlDom/TestScripts"
        fixture_root.mkdir(parents=True)
        (fixture_root / "Linked.sql").symlink_to(target)
        (fixture_root / "LinkedDirectory").symlink_to(
            target.parent, target_is_directory=True
        )

        candidates, exclusions = extract(self.root)

        self.assertEqual([], candidates)
        self.assertEqual(2, len(exclusions))
        for item in exclusions:
            self.assertIn("symbolic link excluded", item["reason"])

    def test_missing_parser_test_root_raises_instead_of_returning_empty_success(self):
        with self.assertRaises(FileNotFoundError):
            extract(self.root)

    def test_non_directory_parser_test_root_raises(self):
        self.write("Test/SqlDom", b"not a directory")

        with self.assertRaises(NotADirectoryError):
            extract(self.root)

    def test_directory_scan_failure_is_not_silently_ignored(self):
        self.write("Test/SqlDom/TestScripts/Valid.sql")

        with mock.patch(
            "tools.tsql_compat.scriptdom_corpus.os.scandir",
            side_effect=PermissionError(errno.EACCES, "directory unreadable"),
        ):
            with self.assertRaisesRegex(PermissionError, "directory unreadable"):
                extract(self.root)


if __name__ == "__main__":
    unittest.main()

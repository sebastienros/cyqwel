import copy
import json
from pathlib import Path
import unittest

if __package__:
    from . import core_profile
else:
    import core_profile


ROOT = Path(__file__).resolve().parents[2]
PROFILE = ROOT / "tools/tsql_compat/core-profile.json"
FIXTURES = ROOT / "tests/Cyqwel.Tests/Fixtures/TSqlCore/sqlglot.json"
LICENSE = FIXTURES.parent / "licenses/SQLGlot.LICENSE"
PROFILE_SHA = "5dbea22035ebeecf9a623c98f123a7e1a723bf7f57a971df5d828525184c5253"
FIXTURE_SHA = "bc06eab66a62fd8c566db938512e5447b8ed11ea04de37bd7d35a5353511eb52"
CORE_IDS_SHA = "ae0343164b731756a72fea0241b404481aab44cc285e5dba5a3bf9937ed285ab"
ORIGINAL_IDS_SHA = "8e01b1d2aaf680a72147292a4fac2da4e44f70830ce6cad6eade013a780147b1"


class CoreProfileTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.profile = json.loads(PROFILE.read_text(encoding="utf-8"))
        cls.fixtures = json.loads(FIXTURES.read_text(encoding="utf-8"))
        cls.entries = {entry["id"]: entry for entry in cls.profile["entries"]}
        cls.cases = {case["id"]: case for case in cls.fixtures["cases"]}

    def test_exact_frozen_contract_hashes_counts_and_membership(self):
        self.assertEqual(core_profile.sha256(PROFILE.read_bytes()), PROFILE_SHA)
        self.assertEqual(core_profile.sha256(FIXTURES.read_bytes()), FIXTURE_SHA)
        self.assertEqual(self.profile["coreIdsSha256"], CORE_IDS_SHA)
        self.assertEqual(self.profile["originalIdsSha256"], ORIGINAL_IDS_SHA)
        self.assertEqual(self.profile["originalCount"], 720)
        self.assertEqual(self.profile["derivedCount"], 211)
        self.assertEqual(self.profile["coreCount"], 511)
        self.assertEqual(self.profile["contextSensitiveCount"], 0)
        self.assertEqual(self.profile["originalDispositions"], {
            "core": 316, "deferred": 160, "not-statement": 185, "reference-rejected": 59,
        })
        self.assertEqual(self.profile["derivedDispositions"], {
            "core": 195, "deferred": 8, "reference-rejected": 8,
        })
        core_profile.validate(self.profile, self.fixtures, LICENSE.read_bytes())

    def test_each_original_and_derivative_has_one_explicit_disposition(self):
        self.assertEqual(len(self.entries), 931)
        self.assertEqual(self.entries.keys(), self.cases.keys())
        self.assertEqual(len({c["id"] for c in self.fixtures["cases"]}), 931)
        for entry in self.entries.values():
            with self.subTest(entry["id"]):
                self.assertIn(entry["disposition"], core_profile.DISPOSITIONS)
                self.assertTrue(entry["reason"])
                self.assertTrue(entry["requirements"])

    def test_removal_reclassification_and_source_mutation_are_errors(self):
        profile = copy.deepcopy(self.profile)
        profile["entries"].pop()
        with self.assertRaisesRegex(ValueError, "exactly one disposition"):
            core_profile.validate(profile, self.fixtures, LICENSE.read_bytes())
        profile = copy.deepcopy(self.profile)
        next(entry for entry in profile["entries"] if entry["disposition"] == "core")["disposition"] = "deferred"
        with self.assertRaisesRegex(ValueError, "core membership"):
            core_profile.validate(profile, self.fixtures, LICENSE.read_bytes())
        fixtures = copy.deepcopy(self.fixtures)
        fixtures["cases"][0]["sql"] += " "
        with self.assertRaisesRegex(ValueError, "Original source text"):
            core_profile.validate(self.profile, fixtures, LICENSE.read_bytes())

    def test_recomputing_counts_and_checksums_cannot_authorize_scope_shrinkage(self):
        profile = copy.deepcopy(self.profile)
        entry = next(entry for entry in profile["entries"] if entry["disposition"] == "core")
        entry["disposition"] = "deferred"
        profile["coreIds"].remove(entry["id"])
        profile["coreCount"] -= 1
        profile["coreIdsSha256"] = core_profile.ids_hash(profile["coreIds"])
        profile["originalDispositions"]["core"] -= 1
        profile["originalDispositions"]["deferred"] += 1
        with self.assertRaisesRegex(ValueError, "does not authorize reclassification"):
            core_profile.validate(profile, self.fixtures, LICENSE.read_bytes())

    def test_duplicate_dispositions_and_fixtures_are_errors(self):
        profile = copy.deepcopy(self.profile)
        profile["entries"].append(profile["entries"][0])
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            core_profile.validate(profile, self.fixtures, LICENSE.read_bytes())
        fixtures = copy.deepcopy(self.fixtures)
        fixtures["cases"].append(fixtures["cases"][0])
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            core_profile.validate(self.profile, fixtures, LICENSE.read_bytes())

    def test_license_and_reference_pin_changes_are_errors(self):
        for license_text in (b"", LICENSE.read_bytes() + b"\n"):
            with self.subTest(license_text=license_text), self.assertRaisesRegex(ValueError, "license notice"):
                core_profile.validate(self.profile, self.fixtures, license_text)
        for key, value in (("version", "180.0.0"), ("parser", "Sql160"), ("quotedIdentifiers", False)):
            profile = copy.deepcopy(self.profile)
            profile["reference"][key] = value
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "profile/reference schema"):
                core_profile.validate(profile, self.fixtures, LICENSE.read_bytes())

    def test_slice_offset_encoding_cannot_be_reinterpreted(self):
        fixtures = copy.deepcopy(self.fixtures)
        case = next(case for case in fixtures["cases"] if case["kind"] == "statement-slice")
        case["derivation"]["offsetEncoding"] = "utf-8"
        with self.assertRaisesRegex(ValueError, "UTF-16 provenance"):
            core_profile.validate(self.profile, fixtures, LICENSE.read_bytes())

    def test_basic_sampling_schema_and_durability_stay_in_the_frozen_core(self):
        for sql in (
            "SELECT * FROM t TABLESAMPLE (10 PERCENT)",
            "SELECT * FROM t TABLESAMPLE (20 ROWS)",
            "CREATE SCHEMA testSchema",
            "COMMIT TRANSACTION @tran_name_variable WITH (DELAYED_DURABILITY = ON)",
            "COMMIT TRANSACTION transaction_name WITH (DELAYED_DURABILITY = OFF)",
        ):
            originals = [case for case in self.cases.values() if case["kind"] == "input" and case["sql"] == sql]
            self.assertTrue(originals, sql)
            for case in originals:
                with self.subTest(case["id"]):
                    self.assertEqual(self.entries[case["id"]]["disposition"], "core")
        for identifier in (
            "sqlglot:tests/dialects/test_tsql.py:47:8:validate_identity.sql:0",
            "sqlglot:tests/dialects/test_tsql.py:831:8:validate_identity.sql:0",
        ):
            self.assertEqual(self.entries[identifier]["disposition"], "deferred")

    def test_table_variables_rowstore_inline_tvfs_and_transactions_remain_core(self):
        ids = [
            "sqlglot:tests/dialects/test_tsql.py:64:8:validate_identity.sql:0",
            "sqlglot:tests/dialects/test_tsql.py:1157:12:validate_identity.sql:0",
            "sqlglot:tests/dialects/test_tsql.py:1157:12:validate_identity.sql:1",
            "sqlglot:tests/dialects/test_tsql.py:1209:8:validate_identity.sql:0",
            "sqlglot:tests/dialects/test_tsql.py:1429:8:validate_identity.sql:0",
            "sqlglot:tests/dialects/test_tsql.py:1431:8:validate_identity.sql:0",
            "sqlglot:tests/dialects/test_tsql.py:1447:8:validate_all.sql:0",
            "sqlglot:tests/dialects/test_tsql.py:2442:8:validate_identity.sql:0",
        ]
        for identifier in ids:
            with self.subTest(identifier):
                self.assertEqual(self.entries[identifier]["disposition"], "core")
        transactions = [
            case for case in self.cases.values() if case["kind"] == "input"
            and case["context"].split("/")[0] in {
                "TestTSQL.test_transaction", "TestTSQL.test_commit", "TestTSQL.test_rollback",
            }
        ]
        self.assertEqual(len(transactions), 17)
        self.assertTrue(all(self.entries[case["id"]]["disposition"] == "core" for case in transactions))

    def test_bare_identifiers_are_not_implicit_execute_requirements(self):
        originals = [
            case for case in self.cases.values()
            if case["kind"] == "input" and case["sql"] in {"#x", "##x", "@x"}
        ]
        self.assertEqual(len(originals), 5)
        for case in originals:
            with self.subTest(case["id"]):
                self.assertEqual(self.entries[case["id"]]["disposition"], "not-statement")
                probe = self.cases[case["id"] + "/core/expression-probe"]
                self.assertEqual(probe["sql"], "SELECT " + case["sql"])
                self.assertEqual(self.entries[probe["id"]]["disposition"], "core")

    def test_mixed_storage_procedure_preserves_ordinary_source_slices(self):
        source_id = "sqlglot:tests/dialects/test_tsql.py:2874:38:sqlglot.parse_one.sql:0"
        self.assertEqual(self.entries[source_id]["disposition"], "deferred")
        slices = [
            case for case in self.cases.values()
            if case["sourceId"] == source_id and case["kind"] == "statement-slice"
        ]
        self.assertEqual(len(slices), 3)
        self.assertEqual([self.entries[case["id"]]["disposition"] for case in slices], ["core", "core", "deferred"])
        self.assertEqual(slices[0]["sql"], "DECLARE @CurrentDate VARCHAR(20);")
        for case in slices:
            self.assertEqual(core_profile.utf16_slice(
                self.cases[source_id]["sql"], case["derivation"]["offset"], case["derivation"]["length"],
            ), case["sql"])

    def test_rejected_full_scripts_have_no_recovered_derivatives(self):
        rejected = {
            entry["id"] for entry in self.entries.values()
            if entry["kind"] == "input" and entry["disposition"] == "reference-rejected"
        }
        self.assertEqual(len(rejected), 59)
        self.assertFalse(any(case["sourceId"] in rejected for case in self.cases.values() if case["kind"] != "input"))

    def test_literal_sql_json_paths_and_quoted_names_are_not_feature_keywords(self):
        tokens = [
            {"type": "Select", "text": "SELECT", "offset": 0},
            {"type": "AsciiStringLiteral", "text": "'FOR XML EXPLICIT; NATIVE_COMPILATION; REPEATABLE'", "offset": 7},
            {"type": "UnicodeStringLiteral", "text": "N'$.SYSTEM_VERSIONING.EXECUTE AS OWNER'", "offset": 90},
            {"type": "QuotedIdentifier", "text": "[AUTHORIZATION]", "offset": 140},
            {"type": "AsciiStringOrQuotedIdentifier", "text": '"CURSOR"', "offset": 160},
            {"type": "MultilineComment", "text": "/* COLUMNSTORE */", "offset": 170},
        ]
        self.assertEqual(core_profile.words(tokens), ["SELECT"])
        evidence = {
            "tokens": tokens,
            "facts": [{"type": "SelectStatement", "values": {}, "offset": 0, "length": 200}],
        }
        required, deferred = core_profile.features({"id": "synthetic"}, evidence)
        self.assertEqual(required, ["queries"])
        self.assertEqual(deferred, [])
        for identifier in (
            "sqlglot:tests/dialects/test_tsql.py:2344:8:validate_all.sql:0",
            "sqlglot:tests/dialects/test_tsql.py:2350:8:validate_all.sql:0",
            "sqlglot:tests/dialects/test_tsql.py:1289:8:validate_all.sql:0",
        ):
            self.assertEqual(self.entries[identifier]["disposition"], "core")

    def test_bounded_options_use_real_node_options_not_whole_methods(self):
        for kind, expected in (("Recompile", []), ("MaxDop", []), ("UsePlan", ["advanced-option-useplan"])):
            evidence = {
                "tokens": [],
                "facts": [{"type": "LiteralOptimizerHint", "values": {"HintKind": kind}}],
            }
            _, deferred = core_profile.features({"id": "synthetic"}, evidence)
            self.assertEqual(deferred, expected)
        options = [
            entry for entry in self.entries.values() if entry["kind"] == "input"
            and self.cases[entry["id"]]["context"].startswith("TestTSQL.test_option/")
        ]
        self.assertEqual(sum(entry["disposition"] == "core" for entry in options), 18)
        self.assertEqual(len(options), 100)

    def test_unknown_statement_shapes_and_stale_reference_text_fail(self):
        case = {"id": "synthetic", "sql": "SELECT 1"}
        evidence = {
            "id": "synthetic", "sqlSha256": core_profile.sha256("SELECT 1"),
            "tokens": [{"type": "Select", "text": "SELECT", "offset": 0}],
            "facts": [], "accepted": True, "errors": [], "batchCount": 1,
            "statements": [{"topLevel": True, "type": "UnreviewedStatement"}],
        }
        with self.assertRaisesRegex(ValueError, "Unreviewed statement"):
            core_profile.classify(case, evidence)
        evidence["sqlSha256"] = "different"
        with self.assertRaisesRegex(ValueError, "does not match"):
            core_profile.classify(case, evidence)

    def test_utf16_offsets_preserve_supplementary_unicode_and_reject_bad_bounds(self):
        text = "SELECT N'\U0001f642';\nSELECT N'caf\u00e9';"
        start = len("SELECT N'\U0001f642';\n".encode("utf-16-le")) // 2
        suffix = "SELECT N'caf\u00e9';"
        self.assertEqual(core_profile.utf16_slice(text, start, len(suffix)), suffix)
        with self.assertRaises(ValueError):
            core_profile.utf16_slice(text, -1, 1)
        with self.assertRaises(ValueError):
            core_profile.utf16_slice(text, 0, 1000)

    def test_reporting_never_hides_missing_core_or_changes_raw_membership(self):
        corpus = core_profile.campaign_corpus(self.fixtures)
        self.assertEqual(len(corpus["cases"]), 931)
        self.assertEqual(len([case for case in self.fixtures["cases"] if case["kind"] == "input"]), 720)
        self.assertEqual(set(corpus["cases"][0]), set(core_profile.SOURCE_FIELDS))
        report = {"results": [
            {"scope": "input", "caseId": identifier, "sql": self.cases[identifier]["sql"], "outcome": "parse-rejected"}
            for identifier in self.profile["coreIds"][:-1]
        ]}
        summary = core_profile.summarize(self.profile, report)
        self.assertEqual(summary["requiredCore"], 511)
        self.assertEqual(summary["missingCoreIds"], self.profile["coreIds"][-1:])
        self.assertEqual(summary["outcomes"], {"parse-rejected": 510})
        report["results"].append(report["results"][0])
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            core_profile.summarize(self.profile, report)
        report["results"].pop()
        report["results"][0]["sql"] = "SELECT changed input"
        with self.assertRaisesRegex(ValueError, "fixture hash"):
            core_profile.summarize(self.profile, report)

    def test_statement_results_cannot_substitute_for_required_full_input_results(self):
        report = {"results": [
            {"scope": "statement", "caseId": identifier, "sql": self.cases[identifier]["sql"],
             "outcome": "round-trip-stable"}
            for identifier in self.profile["coreIds"]
        ]}
        summary = core_profile.summarize(self.profile, report)
        self.assertEqual(summary["requiredCore"], 511)
        self.assertEqual(summary["missingCoreIds"], self.profile["coreIds"])
        self.assertEqual(summary["outcomes"], {})

    def test_implementation_outcomes_do_not_participate_in_classification(self):
        case = {"id": "synthetic", "sql": "SELECT 1"}
        evidence = {
            "id": "synthetic", "sqlSha256": core_profile.sha256(case["sql"]),
            "tokens": [{"type": "Select", "text": "SELECT", "offset": 0}],
            "facts": [{"type": "SelectStatement", "values": {}}],
            "accepted": True, "errors": [], "batchCount": 1,
            "statements": [{"topLevel": True, "type": "SelectStatement"}],
        }
        before = core_profile.classify(case, evidence)
        evidence.update(outcome="parse-rejected", cyqwelParsed=False, astMismatches=["anything"])
        self.assertEqual(core_profile.classify(case, evidence), before)


if __name__ == "__main__":
    unittest.main()

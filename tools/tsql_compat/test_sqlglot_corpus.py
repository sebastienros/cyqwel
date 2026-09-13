import ast
from pathlib import Path
import tempfile
import textwrap
import unittest
from unittest.mock import patch

if __package__:
    from . import sqlglot_corpus
else:
    import sqlglot_corpus


class ExtractTests(unittest.TestCase):
    def fixture(self, source):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        path = root / "tests/dialects/test_tsql.py"
        path.parent.mkdir(parents=True)
        path.write_text(textwrap.dedent(source), encoding="utf-8")
        return root

    def extract(self, body):
        return sqlglot_corpus.extract(self.fixture(body))

    def test_identity_preserves_literals_and_excludes_expected_output(self):
        source = r'''
            from tests.dialects.test_dialect import Validator
            class TestTSQL(Validator):
                dialect = "tsql"
                def test_literals(self):
                    self.validate_identity("select " + "[MiXeD], N'caf\u00e9'")
                    self.validate_identity("SELECT " "2", "select normalized")
                    self.validate_identity(sql="\nSELECT 3\n", write_sql="SELECT 3")
                    self.validate_identity("")
        '''
        candidates, exclusions = self.extract(source)
        self.assertEqual([c["sql"] for c in candidates], [
            "select [MiXeD], N'caf\u00e9'", "SELECT 2", "\nSELECT 3\n", "",
        ])
        self.assertEqual(len(exclusions), 2)
        self.assertTrue(all("generated output" in e["reason"] for e in exclusions))
        self.assertEqual(set(candidates[0]), {"id", "source", "path", "line", "sql", "context"})
        self.assertEqual(set(exclusions[0]), {"source", "path", "line", "context", "reason"})
        self.assertTrue(all(c["id"].startswith("sqlglot:") for c in candidates))
        lines = textwrap.dedent(source).splitlines()
        self.assertTrue(all("self.validate_identity" in lines[c["line"] - 1] for c in candidates))

    def test_read_write_direction_and_generation_errors(self):
        candidates, exclusions = self.extract('''
            from sqlglot.errors import UnsupportedError
            class TestTSQL:
                dialect = "tsql"
                def test_directions(self):
                    self.validate_all(
                        "SELECT TOP 1 a FROM t",
                        read={"tsql": "SELECT TOP (1) a FROM t", "spark": "SELECT a FROM t LIMIT 1"},
                        write={"tsql": "SELECT TOP 1 a FROM t", "spark": "SELECT a FROM t LIMIT 1"},
                    )
                    self.validate_all(sql="SPLIT_PART(x, ',', 1)", write={"tsql": UnsupportedError})
                    self.validate_all("SELECT 2", {"tsql": "select 2", "": "generic 2"}, {"duckdb": "duck 2"})
                    self.validate_transpile("SELECT 3", "other dialect output", "spark")
        ''')
        self.assertEqual([c["sql"] for c in candidates], [
            "SELECT TOP 1 a FROM t", "SELECT TOP (1) a FROM t", "SPLIT_PART(x, ',', 1)",
            "SELECT 2", "select 2", "SELECT 3",
        ])
        self.assertEqual(len(exclusions), 7)
        self.assertEqual(sum("generation error" in e["reason"] for e in exclusions), 1)
        self.assertEqual(sum("non-TSQL" in e["reason"] for e in exclusions), 2)
        self.assertIn("read['tsql']", candidates[1]["context"])

    def test_direct_parser_calls_use_input_dialect_not_output(self):
        candidates, exclusions = self.extract('''
            import sqlglot as sg
            from sqlglot import parse_one, parse_one as parse_sql, parse, transpile
            class TestTSQL:
                dialect = "tsql"
                def test_parsers(self):
                    self.parse_one("SELECT self")
                    parse_one("SELECT keyword", dialect="tsql")
                    parse_sql("SELECT positional", "tsql")
                    sg.parse_one("SELECT precedence", read="tsql", dialect="duckdb")
                    parse("SELECT 1; SELECT 2", read="tsql")
                    transpile("SELECT source", read="tsql", write="spark")
                    parse_one("generic input").sql("tsql")
                    parse_one("duckdb input", read="duckdb", dialect="tsql").sql("tsql")
                    transpile("spark input", read="spark", write="tsql")
        ''')
        self.assertEqual([c["sql"] for c in candidates], [
            "SELECT self", "SELECT keyword", "SELECT positional", "SELECT precedence",
            "SELECT 1; SELECT 2", "SELECT source",
        ])
        self.assertEqual(len(exclusions), 3)
        self.assertTrue(all("non-TSQL" in e["reason"] for e in exclusions))

    def test_duplicate_occurrences_have_stable_distinct_provenance(self):
        source = '''
            class TestTSQL:
                dialect = "tsql"
                def test_duplicates(self):
                    self.validate_identity("SELECT 1"); self.validate_identity("SELECT 1")
                    self.validate_all("SELECT 1", read={"tsql": "SELECT 1"})
                    for sql in ["SELECT 1", "SELECT 1"]:
                        self.validate_identity(sql)
        '''
        root = self.fixture(source)
        first = sqlglot_corpus.extract(root)
        self.assertEqual(first, sqlglot_corpus.extract(root))
        self.assertEqual(first, self.extract(source))
        candidates, exclusions = first
        self.assertEqual(len(candidates), 6)
        self.assertEqual(len({c["id"] for c in candidates}), 6)
        self.assertEqual(candidates[0]["line"], candidates[1]["line"])
        self.assertNotEqual(candidates[4]["context"], candidates[5]["context"])
        self.assertEqual(exclusions, [])

    def test_nested_literal_loops_and_mixed_symbolic_tuple_members(self):
        candidates, exclusions = self.extract('''
            from sqlglot import exp
            class TestTSQL:
                dialect = "tsql"
                def test_loops(self):
                    statements = ("SELECT 1", "SELECT 2")
                    options = ["RECOMPILE", "MAXDOP 1"]
                    for statement in statements:
                        for option in options:
                            query = f"{statement} OPTION({option})"
                            with self.subTest(query):
                                self.validate_identity(query)
                    for formats, canonical in [(("yy", "yyyy"), "YEAR"), (("mm",), "MONTH")]:
                        for fmt in formats:
                            self.validate_identity(f"DATEPART({fmt}, x)", f"DATEPART({canonical}, x)")
                    for value, cls in [("{d'2024-01-01'}", exp.Date), ("{t'12:00:00'}", exp.Time)]:
                        sql = f"INSERT INTO t VALUES ({value})"
                        expr = self.parse_one(sql)
                        self.assertIsInstance(expr, cls)
                    for i in range(1, 4):
                        self.validate_identity(f"SELECT PARSENAME('a.b.c', {4 - i})")
        ''')
        self.assertEqual([c["sql"] for c in candidates], [
            "SELECT 1 OPTION(RECOMPILE)", "SELECT 1 OPTION(MAXDOP 1)",
            "SELECT 2 OPTION(RECOMPILE)", "SELECT 2 OPTION(MAXDOP 1)",
            "DATEPART(yy, x)", "DATEPART(yyyy, x)", "DATEPART(mm, x)",
            "INSERT INTO t VALUES ({d'2024-01-01'})", "INSERT INTO t VALUES ({t'12:00:00'})",
            "SELECT PARSENAME('a.b.c', 3)", "SELECT PARSENAME('a.b.c', 2)",
            "SELECT PARSENAME('a.b.c', 1)",
        ])
        self.assertEqual(len(exclusions), 3)

    def test_procedure_list_bindings_and_parse_call_in_dynamic_iterator(self):
        candidates, exclusions = self.extract('''
            from sqlglot import parse_one
            class TestTSQL:
                dialect = "tsql"
                def test_procedures(self):
                    sqls = ["EXEC proc @p = 1", "\\nBEGIN\\n  SELECT 1;\\nEND\\n"]
                    for sql in sqls:
                        expression = parse_one(sql, read="tsql")
                        expected = " ".join(line.strip() for line in sql.splitlines())
                        self.assertEqual(expression.sql("tsql"), expected)
                    sql = "CREATE PROC p AS SELECT 1; SELECT 2"
                    expected_sqls = ["CREATE PROC p AS SELECT 1", "SELECT 2"]
                    for expr, expected_sql in zip(parse_one(sql, read="tsql").expressions, expected_sqls):
                        self.assertEqual(expr.sql("tsql"), expected_sql)
                    sql = "CREATE PROC q AS SELECT 3"
                    for expr, expected_sql in zip(parse_one(sql, read="tsql").expressions, expected_sqls):
                        self.assertEqual(expr.sql("tsql"), expected_sql)
        ''')
        self.assertEqual([c["sql"] for c in candidates], [
            "EXEC proc @p = 1", "\nBEGIN\n  SELECT 1;\nEND\n",
            "CREATE PROC p AS SELECT 1; SELECT 2", "CREATE PROC q AS SELECT 3",
        ])
        self.assertEqual(exclusions, [])

    def test_literal_dedent_strip_and_import_aliases(self):
        candidates, exclusions = self.extract('''
            import textwrap as tw
            from textwrap import dedent as clean
            SQL = "  SELECT 1  "
            class TestTSQL:
                dialect = "tsql"
                def test_whitespace(self):
                    self.validate_identity(SQL.strip())
                    self.validate_identity(tw.dedent("\\n    SELECT 2\\n      FROM t\\n"))
                    self.validate_identity(clean("   SELECT 3").strip())
                    self.validate_identity("xxxSELECT 4yyy".lstrip("x").rstrip("y"))
                    for index, sql in enumerate(("SELECT 5", "SELECT 6")):
                        self.validate_identity(sql)
                    for sql, suffix in zip(["SELECT 7"], [" AS n"]):
                        self.validate_identity(sql + suffix)
        ''')
        self.assertEqual([c["sql"] for c in candidates], [
            "SELECT 1", "\nSELECT 2\n  FROM t\n", "SELECT 3", "SELECT 4",
            "SELECT 5", "SELECT 6", "SELECT 7 AS n",
        ])
        self.assertEqual(exclusions, [])

    def test_negative_assertions_are_exclusions_not_candidates(self):
        candidates, exclusions = self.extract('''
            from sqlglot import parse_one
            from sqlglot.errors import ParseError
            class TestTSQL:
                dialect = "tsql"
                def test_errors(self):
                    with self.assertRaises(ParseError):
                        parse_one("SELECT begin", read="tsql")
                    for sql in ["SELECT OPTION BAD", "SELECT OPTION()"]:
                        with self.assertRaises(ParseError, msg=sql):
                            self.parse_one(sql)
                    self.assertRaises(ParseError, parse_one, "BROKEN", read="tsql")
                    self.validate_identity("SELECT positive")
        ''')
        self.assertEqual([c["sql"] for c in candidates], ["SELECT positive"])
        self.assertEqual(len(exclusions), 4)
        self.assertTrue(all("expected exception" in e["reason"] for e in exclusions))

    def test_regex_and_lambda_error_assertions_are_not_double_counted(self):
        candidates, exclusions = self.extract('''
            from sqlglot import parse_one
            from sqlglot.errors import ParseError
            class TestTSQL:
                dialect = "tsql"
                def test_errors(self):
                    with self.assertRaisesRegex(ParseError, "bad syntax"):
                        self.parse_one("BAD")
                    self.assertRaisesRegex(ParseError, "bad", parse_one, "BAD", read="tsql")
                    self.assertRaises(ParseError, lambda: self.parse_one("BAD"))
                    self.validate_identity("SELECT 1")
        ''')
        self.assertEqual([c["sql"] for c in candidates], ["SELECT 1"])
        self.assertEqual(len(exclusions), 3)
        self.assertTrue(all("expected exception" in e["reason"] for e in exclusions))

    def test_command_passthroughs_are_retained_and_marked_not_verified(self):
        candidates, exclusions = self.extract('''
            from sqlglot import exp
            class TestTSQL:
                dialect = "tsql"
                def test_commands(self):
                    self.validate_identity("GO").assert_is(exp.Command)
                    self.validate_identity("PRINT @v", check_command_warning=True)
                    self.validate_identity("SELECT 1")
        ''')
        self.assertEqual([c["sql"] for c in candidates], ["GO", "PRINT @v", "SELECT 1"])
        self.assertTrue(all("command-passthrough" in c["context"] for c in candidates[:2]))
        self.assertNotIn("command-passthrough", candidates[2]["context"])
        self.assertEqual(exclusions, [])

    def test_dynamic_expressions_and_unknown_helpers_are_reported_without_execution(self):
        candidates, exclusions = self.extract('''
            from nonexistent_upstream_package import fetch_sql
            raise RuntimeError("This module must never execute")
            class TestTSQL:
                dialect = "tsql"
                def test_dynamic(self):
                    self.validate_identity(fetch_sql())
                    self.validate_identity(f"SELECT {runtime_name}")
                    self.validate_identity("SELECT {}".format(runtime_name))
                    self.validate_identity(123)
                    self.validate_identity(f"SELECT {1:10000000}")
                    validate(True, "SQL with unknown validation semantics")
                    validate(False, "negative-looking but unknown helper")
                    for sql in fetch_sql():
                        self.validate_identity(sql)
                    for sql in ["SELECT static", fetch_sql()]:
                        self.validate_identity(sql)
                    self.validate_all("SELECT canonical", read={"tsql": fetch_sql(), "spark": "SELECT foreign"})
                    self.validate_all("SELECT another", read=fetch_sql(), write=fetch_sql())
        ''')
        self.assertEqual([c["sql"] for c in candidates], [
            "SELECT static", "SELECT canonical", "SELECT another",
        ])
        self.assertEqual(len(exclusions), 13)
        self.assertTrue(any("dynamic loop" in e["reason"] for e in exclusions))
        self.assertEqual(sum("unknown helper semantics" in e["reason"] for e in exclusions), 2)
        self.assertTrue(all(isinstance(e["line"], int) and e["line"] > 0 for e in exclusions))

    def test_dynamic_rebinding_mutation_and_branches_do_not_leak_stale_literals(self):
        candidates, exclusions = self.extract('''
            class TestTSQL:
                dialect = "tsql"
                def test_state(self):
                    sql = "SELECT stale"
                    sql = make_sql()
                    self.validate_identity(sql)
                    sqls = ["SELECT stale collection"]
                    alias = sqls
                    sqls.append(make_sql())
                    for sql in alias:
                        self.validate_identity(sql)
                    sql = "SELECT stale branch"
                    if runtime_condition:
                        sql = "SELECT alternate"
                        self.validate_identity("SELECT conditional")
                    self.validate_identity(sql)
                    if True:
                        self.validate_identity("SELECT reachable")
                    else:
                        self.validate_identity("SELECT unreachable")
        ''')
        self.assertEqual([c["sql"] for c in candidates], ["SELECT reachable"])
        self.assertEqual(len(exclusions), 5)
        self.assertTrue(any("mutation" in e["reason"] for e in exclusions))
        self.assertTrue(any("unreachable" in e["reason"] for e in exclusions))

    def test_dialect_bindings_and_helper_overrides(self):
        candidates, exclusions = self.extract('''
            class TestOther:
                dialect = "spark"
                def test_read(self):
                    self.validate_all("SELECT foreign", read={"tsql": "SELECT tsql"})
            class TestTSQL:
                dialect = "tsql"
                def test_override(self):
                    self.dialect = "spark"
                    self.validate_identity("SELECT foreign")
            class TestCustom:
                dialect = "tsql"
                def validate_identity(self, sql):
                    pass
                def test_unknown(self):
                    self.validate_identity("SELECT unknown")
        ''')
        self.assertEqual([c["sql"] for c in candidates], ["SELECT tsql"])
        self.assertEqual(len(exclusions), 3)

    def test_local_class_and_late_module_bindings_have_python_scope(self):
        candidates, exclusions = self.extract('''
            SQL = "SELECT global"
            class TestTSQL:
                dialect = "tsql"
                fixture = "SELECT class"
                def test_names(self):
                    self.validate_identity(SQL)
                    self.validate_identity(self.fixture)
                    self.fixture = "SELECT updated"
                    self.validate_identity(self.fixture)
                    self.validate_identity(self.SQL)
                    self.validate_identity(LATE)
                    def unused_helper():
                        SQL = "SELECT unused local"
                    self.validate_identity(SQL)
                def test_unbound_local(self):
                    self.validate_identity(SQL)
                    SQL = "SELECT local"
                    self.validate_identity(SQL)
                def test_replaced_helper(self):
                    self.validate_identity = unknown_helper
                    self.validate_identity("SELECT unknown")
            LATE = "SELECT late global"
        ''')
        self.assertEqual([c["sql"] for c in candidates], [
            "SELECT global", "SELECT class", "SELECT updated", "SELECT late global",
            "SELECT global", "SELECT local",
        ])
        self.assertEqual(len(exclusions), 3)

    def test_empty_loops_preserve_bindings_and_loop_exits_are_explicit(self):
        candidates, exclusions = self.extract('''
            class TestTSQL:
                dialect = "tsql"
                def test_empty(self):
                    sql = "SELECT before"
                    for sql in []:
                        self.validate_identity(sql)
                    else:
                        self.validate_identity(sql)
                    self.validate_identity(sql)
                def test_exit(self):
                    for sql in ["SELECT first", "SELECT never"]:
                        self.validate_identity(sql)
                        break
                    else:
                        self.validate_identity("SELECT else never")
                    self.validate_identity("SELECT after loop")
        ''')
        self.assertEqual([c["sql"] for c in candidates], [
            "SELECT before", "SELECT before", "SELECT after loop",
        ])
        self.assertEqual(len(exclusions), 3)
        self.assertTrue(any("empty static loop" in e["reason"] for e in exclusions))
        self.assertEqual(sum("loop control-flow" in e["reason"] for e in exclusions), 2)

    def test_returns_inside_branches_do_not_fabricate_later_inputs(self):
        candidates, exclusions = self.extract('''
            class TestTSQL:
                dialect = "tsql"
                def test_static_return(self):
                    self.validate_identity("SELECT first")
                    if True:
                        return
                    self.validate_identity("SELECT unreachable")
                def test_dynamic_return(self):
                    if runtime_condition:
                        return
                    self.validate_identity("SELECT conditional")
        ''')
        self.assertEqual([c["sql"] for c in candidates], ["SELECT first"])
        self.assertEqual(len(exclusions), 2)
        self.assertTrue(all("control-flow exit" in e["reason"] for e in exclusions))

    def test_mutation_during_iteration_and_nested_aliases_are_not_static(self):
        candidates, exclusions = self.extract('''
            class TestTSQL:
                dialect = "tsql"
                def test_mutation(self):
                    sqls = ["SELECT first", "SELECT potentially removed"]
                    self.assertEqual(sqls, ["SELECT first", "SELECT potentially removed"])
                    for sql in sqls:
                        self.validate_identity(sql)
                        sqls.pop()
                    sqls = ["SELECT old"]
                    nested_alias = (sqls,)
                    sqls.append(make_sql())
                    for sql in nested_alias[0]:
                        self.validate_identity(sql)
                    self.validate_identity("SELECT unaffected")
        ''')
        self.assertEqual([c["sql"] for c in candidates], ["SELECT first", "SELECT unaffected"])
        self.assertEqual(len(exclusions), 2)
        self.assertTrue(all("mutat" in e["reason"] for e in exclusions))

    def test_extraction_does_not_import_execute_or_follow_literal_urls(self):
        with tempfile.TemporaryDirectory() as directory:
            marker = Path(directory) / "must-not-be-created"
            candidates, exclusions = self.extract(f'''
                from pathlib import Path
                from textwrap import dedent
                Path({str(marker)!r}).write_text("executed module")
                class TestTSQL:
                    dialect = "tsql"
                    def test_no_execution(self):
                        self.validate_identity(Path({str(marker)!r}).write_text("executed expression"))
                        self.validate_identity("SELECT 'https://example.invalid/fixture.sql'")
                        dedent = arbitrary_function
                        self.validate_identity(dedent("SELECT custom function"))
            ''')
            self.assertFalse(marker.exists())
        self.assertEqual([c["sql"] for c in candidates], [
            "SELECT 'https://example.invalid/fixture.sql'",
        ])
        self.assertEqual(len(exclusions), 2)

    def test_dynamic_read_values_do_not_poison_known_tsql_inputs(self):
        candidates, exclusions = self.extract('''
            class TestTSQL:
                dialect = "tsql"
                def test_mappings(self):
                    reads = {"tsql": "SELECT source", "spark": dynamic_sql()}
                    self.validate_all("SELECT canonical", read=reads, write={"tsql": dynamic_sql()})
                    self.validate_all("SELECT kept", read={dynamic_dialect: "SELECT unknown dialect"})
                    self.validate_identity(*dynamic_args)
                    self.validate_identity("SELECT uncertain binding", **dynamic_options)
        ''')
        self.assertEqual([c["sql"] for c in candidates], [
            "SELECT canonical", "SELECT source", "SELECT kept",
        ])
        self.assertEqual(len(exclusions), 5)
        self.assertEqual(sum("runtime binding" in e["reason"] for e in exclusions), 2)

    def test_loop_and_expression_limits_report_exclusions(self):
        source = '''
            class TestTSQL:
                dialect = "tsql"
                def test_bounded(self):
                    for i in range(4):
                        self.validate_identity(f"SELECT {i}")
                    self.validate_identity("SELECT still reachable")
        '''
        with patch.object(sqlglot_corpus, "_MAX_ITERATIONS", 3):
            candidates, exclusions = self.extract(source)
        self.assertEqual([c["sql"] for c in candidates], ["SELECT still reachable"])
        self.assertEqual(len(exclusions), 1)
        self.assertIn("expansion", exclusions[0]["reason"])
        with patch.object(sqlglot_corpus, "_MAX_TEXT", 8):
            candidates, exclusions = self.extract('''
                class TestTSQL:
                    dialect = "tsql"
                    def test_large(self):
                        self.validate_identity("SELECT too long")
            ''')
        self.assertEqual(candidates, [])
        self.assertEqual(len(exclusions), 1)
        self.assertEqual(exclusions[0]["reason"], "literal text exceeds 8 characters")

    def test_nested_loop_budget_is_enforced_before_each_iteration(self):
        source = '''
            class TestTSQL:
                dialect = "tsql"
                def test_bounded(self):
                    for i in range(2):
                        self.validate_identity(f"SELECT outer {i}")
                        for j in range(2):
                            self.validate_identity(f"SELECT inner {i}, {j}")
                    self.validate_identity("SELECT final")
        '''
        with patch.object(sqlglot_corpus, "_MAX_ITERATIONS", 3):
            root = self.fixture(source)
            tree = ast.parse((root / "tests/dialects/test_tsql.py").read_text(encoding="utf-8"))
            extractor = sqlglot_corpus._Extractor(tree)
            candidates, exclusions = extractor.run()
            self.assertEqual(extractor.iterations_left, 0)
        self.assertEqual([c["sql"] for c in candidates], [
            "SELECT outer 0", "SELECT inner 0, 0", "SELECT inner 0, 1", "SELECT final",
        ])
        self.assertEqual(len(exclusions), 2)
        self.assertTrue(all("expansion exceeds" in e["reason"] for e in exclusions))

    def test_missing_or_invalid_sources_raise_instead_of_returning_empty_success(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):
                sqlglot_corpus.extract(Path(directory))
        with self.assertRaises(SyntaxError):
            self.extract("def invalid Python:")


if __name__ == "__main__":
    unittest.main()

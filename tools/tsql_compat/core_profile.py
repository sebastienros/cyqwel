"""Build the frozen, offline SQLGlot Core contract from reference-only evidence.

Generation is opt-in; normal tests read the checked-in profile and fixtures.
CoreReference uses only ScriptDom 180.107.0 / Sql180, never Cyqwel. Classification
uses ScriptDom node kinds/options and real tokens, not keywords inside SQL
strings, comments, quoted names, dynamic SQL arguments, or JSON paths.

Workflow:
  CoreReference corpus.json reference-originals.json
  core_profile.py prepare --corpus ... --reference ... --output requests.json
  CoreReference requests.json reference-all.json
  core_profile.py freeze --corpus ... --requests ... --reference ... --plan ...
      --license ... --profile ... --fixtures ...

All original occurrences remain present. Only independently revalidated,
unchanged statement slices from reference-valid parents may be derived.
Expression/identifier fixtures get separately attributed SELECT/WHERE probes;
incomplete BEGIN/END/GO fixtures get explicit block/batch probes. Invalid
statement-looking scripts are never repaired, split, or partially recovered.
"""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path


UPSTREAM_SHA = "5cfb5997a99010940138670adf3d6b34ac5a0a08"
ORIGINAL_COUNT = 720
FROZEN_PROFILE_CANONICAL_HASH = "485d5e640b7a5f1427fa5d00303f38e0e6367b5493b24976e922e313e30f46c6"
REFERENCE = {
    "package": "Microsoft.SqlServer.TransactSql.ScriptDom",
    "version": "180.107.0",
    "parser": "Sql180",
    "quotedIdentifiers": True,
    "offsetEncoding": "utf-16",
}
DISPOSITIONS = {"core", "deferred", "not-statement", "reference-rejected"}
SOURCE_FIELDS = ("id", "source", "path", "line", "sql", "context")
STATEMENT_STARTERS = {
    "SELECT", "WITH", "INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "ALTER",
    "DROP", "DECLARE", "SET", "EXEC", "EXECUTE", "IF", "WHILE", "BEGIN", "END",
    "RETURN", "COMMIT", "ROLLBACK", "GRANT", "REVOKE", "DENY", "TRUNCATE",
    "PRINT", "GO", "COPY", "USE", "BACKUP", "RESTORE", "DBCC", "BREAK",
    "CONTINUE", "SAVE", "THROW", "RAISERROR",
}
IGNORED_TOKEN_TYPES = {
    "AsciiStringLiteral", "UnicodeStringLiteral", "QuotedIdentifier",
    "AsciiStringOrQuotedIdentifier", "WhiteSpace", "SingleLineComment",
    "MultilineComment", "EndOfFile",
}
CORE_STATEMENTS = {
    "SelectStatement": "queries",
    "InsertStatement": "insert",
    "UpdateStatement": "update",
    "DeleteStatement": "delete",
    "MergeStatement": "merge",
    "CreateTableStatement": "table-ddl",
    "AlterTableDropTableElementStatement": "table-ddl",
    "AlterTableAddTableElementStatement": "table-ddl",
    "AlterTableAlterColumnStatement": "table-ddl",
    "DropTableStatement": "table-ddl",
    "CreateIndexStatement": "rowstore-indexes",
    "DropIndexStatement": "rowstore-indexes",
    "CreateViewStatement": "view-ddl",
    "AlterViewStatement": "view-ddl",
    "CreateOrAlterViewStatement": "view-ddl",
    "DropViewStatement": "view-ddl",
    "CreateSchemaStatement": "plain-schema-ddl",
    "CreateFunctionStatement": "inline-table-valued-functions",
    "CreateProcedureStatement": "procedure-definitions",
    "AlterProcedureStatement": "procedure-definitions",
    "CreateOrAlterProcedureStatement": "procedure-definitions",
    "ExecuteStatement": "procedure-calls",
    "DeclareVariableStatement": "scalar-declarations",
    "DeclareTableVariableStatement": "table-variables",
    "SetVariableStatement": "variable-assignments",
    "PredicateSetStatement": "application-set-options",
    "IfStatement": "control-flow",
    "WhileStatement": "control-flow",
    "BeginEndBlockStatement": "blocks",
    "ReturnStatement": "return-values",
    "BreakStatement": "control-flow",
    "ContinueStatement": "control-flow",
    "BeginTransactionStatement": "transactions",
    "CommitTransactionStatement": "transactions",
    "RollbackTransactionStatement": "transactions",
    "SaveTransactionStatement": "transactions",
    "PrintStatement": "print",
}
CORE_NODES = {
    "VariableReference": "variables",
    "VariableTableReference": "table-variables",
    "SelectSetVariable": "select-assignments",
    "SchemaObjectFunctionTableReference": "table-valued-functions",
    "OpenJsonTableReference": "openjson",
    "SchemaDeclarationItemOpenjson": "openjson",
    "PivotedTableReference": "pivot",
    "UnpivotedTableReference": "unpivot",
    "IdentityOptions": "identity",
    "UniqueConstraintDefinition": "key-constraints",
    "IndexDefinition": "rowstore-indexes",
    "OutputClause": "output",
    "OutputIntoClause": "output-into",
    "SelectFunctionReturnType": "inline-table-valued-functions",
    "TableSampleClause": "basic-table-sampling",
}
DEFERRED_NODES = {
    "GrantStatement": "security",
    "RevokeStatement": "security",
    "DenyStatement": "security",
    "ExecuteAsClause": "security",
    "ExecuteAsStatement": "security",
    "UpdateStatisticsStatement": "administration",
    "CreateTriggerStatement": "triggers",
    "AlterTriggerStatement": "triggers",
    "DeclareCursorStatement": "cursors",
    "CursorDefinition": "cursors",
    "OdbcLiteral": "odbc",
    "CreateColumnStoreIndexStatement": "columnstore",
    "SystemVersioningTableOption": "temporal-storage",
    "TemporalClause": "temporal-storage",
    "DataRetentionTableOption": "temporal-storage",
    "FileStreamOnTableOption": "file-storage",
    "FileGroupOrPartitionScheme": "filegroup-partition-storage",
    "CompressionPartitionRange": "filegroup-partition-storage",
    "TableDistributionOption": "distribution-storage",
    "TableIndexOption": "distribution-storage",
    "BrowseForClause": "for-browse",
    "ScalarFunctionReturnType": "scalar-multistatement-functions",
    "TableValuedFunctionReturnType": "scalar-multistatement-functions",
}
COMMON_HINTS = {"Recompile", "MaxDop", "MaxRecursion", "ForceOrder", "Fast", "OptimizeFor"}
SIMPLE_XML = {"Path", "Auto", "Raw", "Type", "Root"}
COMMON_JSON = {"Auto", "Path", "Root", "IncludeNullValues", "WithoutArrayWrapper"}
XML_INSTANCE_CASE = "sqlglot:tests/dialects/test_tsql.py:61:8:validate_identity.sql:0"


def sha256(value: str | bytes) -> str:
    return hashlib.sha256(value.encode("utf-8") if isinstance(value, str) else value).hexdigest()


def ids_hash(ids: list[str]) -> str:
    return sha256("".join(identifier + "\n" for identifier in sorted(ids)))


def canonical_hash(value: object) -> str:
    return sha256(json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")))


def utf16_slice(text: str, offset: int, length: int) -> str:
    encoded = text.encode("utf-16-le")
    if offset < 0 or length < 0 or (offset + length) * 2 > len(encoded):
        raise ValueError("Reference slice is outside its original source.")
    return encoded[offset * 2:(offset + length) * 2].decode("utf-16-le")


def words(tokens: list[dict], start: int = 0, length: int | None = None) -> list[str]:
    return [
        token["text"].upper() for token in tokens
        if token["type"] not in IGNORED_TOKEN_TYPES
        and token["offset"] >= start
        and (length is None or token["offset"] < start + length)
    ]


def features(case: dict, evidence: dict) -> tuple[list[str], list[str]]:
    required: set[str] = set()
    deferred: set[str] = set()
    tokens = evidence["tokens"]
    syntax = words(tokens)
    for fact in evidence["facts"]:
        kind, values = fact["type"], fact["values"]
        if kind in CORE_STATEMENTS:
            required.add(CORE_STATEMENTS[kind])
        if kind in CORE_NODES:
            required.add(CORE_NODES[kind])
        if kind in DEFERRED_NODES:
            deferred.add(DEFERRED_NODES[kind])
        if kind.endswith("OptimizerHint"):
            hint = values.get("HintKind")
            if hint in COMMON_HINTS and (hint != "OptimizeFor" or values.get("IsForUnknown") == "True"):
                required.add("option-" + hint.lower())
            else:
                deferred.add("advanced-option-" + str(hint).lower())
        if kind == "XmlForClauseOption":
            option = values["OptionKind"]
            (required if option in SIMPLE_XML else deferred).add("for-xml-" + option.lower())
        if kind == "JsonForClauseOption":
            option = values["OptionKind"]
            (required if option in COMMON_JSON else deferred).add("for-json-" + option.lower())
        if kind in {"ProcedureOption", "ViewOption", "ExecuteAsProcedureOption"}:
            deferred.add("routine-view-option-" + values["OptionKind"].lower())
        if values.get("JoinHint") == "Remote":
            deferred.add("remote-join")
        elif values.get("JoinHint") in {"Hash", "Loop", "Merge"}:
            required.add("local-join-hints")
        if kind == "UnqualifiedJoin" and values.get("UnqualifiedJoinType") in {"CrossApply", "OuterApply"}:
            required.add("apply")
        if kind.endswith("TableHint"):
            required.add("table-hints")
        if kind == "TableSampleClause" and "REPEATABLE" in words(tokens, fact["offset"], fact["length"]):
            deferred.add("advanced-sampling-repeatable")
        if kind == "PredicateSetStatement" and values.get("Options") not in {"NoCount", "XactAbort"}:
            deferred.add("administrative-set-options")
    if any(token["type"] == "DoubleColon" for token in tokens):
        deferred.add("clr-scope-resolution")
    if "AUTHORIZATION" in syntax and syntax[:2] == ["CREATE", "SCHEMA"]:
        deferred.add("schema-authorization")
    if case.get("sourceId", case["id"]) == XML_INSTANCE_CASE:
        deferred.add("xml-instance-methods")
    if "INTO" in syntax and "queries" in required:
        required.add("select-into")
    if any(token["type"] == "Variable" for token in tokens):
        required.add("variables")
    if any(token["type"] == "Identifier" and token["text"].startswith("#") for token in tokens):
        required.add("temporary-identifiers")
    required.update(deferred)
    return sorted(required), sorted(deferred)


def classify(case: dict, evidence: dict) -> dict:
    if case["id"] != evidence["id"] or sha256(case["sql"]) != evidence["sqlSha256"]:
        raise ValueError(f"Reference evidence does not match {case['id']}.")
    required, deferred = features(case, evidence)
    syntax = words(evidence["tokens"])
    first = syntax[0] if syntax else ""
    fragment = case.get("kind", "input") == "input" and (
        first not in STATEMENT_STARTERS or syntax in (["GO"], ["BEGIN"], ["END"])
    )
    if fragment:
        disposition = "not-statement"
        reason = "Expression/identifier or isolated block/batch token; implicit EXEC is not the statement-API contract."
        required = sorted(set(required) | {"expression-or-token-fixture"})
    elif not evidence["accepted"]:
        disposition = "reference-rejected"
        required = sorted(set(required) | {"reference-valid-script"})
        reason = "The complete, unchanged input is rejected by ScriptDom Sql180 with quoted identifiers on; no partial recovery."
    elif case.get("derivation", {}).get("contextStable") is False:
        disposition = "deferred"
        required = sorted(set(required) | {"reference-context-sensitive"})
        reason = "Isolating this source statement changes its reference AST/context; not a standalone core gate."
    elif deferred:
        disposition = "deferred"
        reason = "Approved scope defers: " + ", ".join(deferred) + "."
    else:
        roots = [statement["type"] for statement in evidence["statements"] if statement["topLevel"]]
        unknown = sorted(set(roots) - CORE_STATEMENTS.keys())
        if not roots or unknown:
            raise ValueError(f"Unreviewed statement shape for {case['id']}: {unknown or 'empty input'}")
        disposition = "core"
        reason = "All required syntax is ordinary application SQL or an explicitly bounded core extension."
    return {
        "id": case["id"],
        "sourceId": case.get("sourceId", case["id"]),
        "kind": case.get("kind", "input"),
        "sqlSha256": sha256(case["sql"]),
        "disposition": disposition,
        "requirements": required,
        "reason": reason,
        "referenceAccepted": evidence["accepted"],
        "referenceStatementTypes": [s["type"] for s in evidence["statements"] if s["topLevel"]],
        "referenceBatchCount": evidence["batchCount"],
        "referenceErrors": evidence["errors"],
    }


def originals(corpus: dict) -> list[dict]:
    source = next(source for source in corpus["sources"] if source["source"] == "sqlglot")
    cases = [case for case in corpus["cases"] if case["source"] == "sqlglot"]
    if source["revision"] != UPSTREAM_SHA or len(cases) != ORIGINAL_COUNT:
        raise ValueError("The core profile must use all 720 occurrences from the pinned SQLGlot source.")
    if len({case["id"] for case in cases}) != ORIGINAL_COUNT:
        raise ValueError("Duplicate original source IDs.")
    return cases


def reference_map(reference: dict) -> dict[str, dict]:
    if reference["reference"] != REFERENCE:
        raise ValueError("Unexpected reference parser contract.")
    rows = {case["id"]: case for case in reference["cases"]}
    if len(rows) != len(reference["cases"]):
        raise ValueError("Duplicate reference evidence IDs.")
    return rows


def prepare(corpus: dict, reference: dict) -> dict:
    cases = originals(corpus)
    references = reference_map(reference)
    prepared = [dict(case, kind="input", sourceId=case["id"]) for case in cases]
    for case in cases:
        evidence = references[case["id"]]
        decision = classify(case, evidence)
        if decision["disposition"] == "not-statement":
            syntax = words(evidence["tokens"])
            if syntax == ["GO"]:
                kind, prefix, suffix = "batch-probe", "SELECT 1\n", "\nSELECT 2"
            elif syntax == ["BEGIN"]:
                kind, prefix, suffix = "block-probe", "", " SELECT 1; END"
            elif syntax == ["END"]:
                kind, prefix, suffix = "block-probe", "BEGIN SELECT 1; ", ""
            elif evidence.get("expressionKind") == "predicate":
                kind, prefix, suffix = "predicate-probe", "SELECT 1 WHERE ", ""
            else:
                kind, prefix, suffix = "expression-probe", "SELECT ", ""
            prepared.append(dict(
                case, id=case["id"] + "/core/" + kind, sourceId=case["id"], kind=kind,
                sql=prefix + case["sql"] + suffix,
                context=case["context"] + "/core/" + kind,
                derivation={"prefix": prefix, "suffix": suffix, "originalSqlSha256": sha256(case["sql"])},
            ))
        elif evidence["accepted"]:
            top_count = sum(statement["topLevel"] for statement in evidence["statements"])
            for statement in evidence["statements"]:
                if statement["topLevel"]:
                    include = top_count > 1
                else:
                    include = decision["disposition"] == "deferred"
                if not include:
                    continue
                text = utf16_slice(case["sql"], statement["offset"], statement["length"])
                if text != statement["sql"] or sha256(text) != statement["sqlSha256"]:
                    raise ValueError("Reference statement is not an unchanged source slice.")
                suffix = f"/core/statement@{statement['offset']}+{statement['length']}"
                prepared.append(dict(
                    case, id=case["id"] + suffix, sourceId=case["id"], kind="statement-slice",
                    sql=text, context=case["context"] + suffix,
                    derivation={
                        "offset": statement["offset"], "length": statement["length"],
                        "offsetEncoding": "utf-16", "path": statement["path"],
                        "sourceStatementType": statement["type"],
                        "contextStable": statement["contextStable"],
                        "originalSqlSha256": sha256(case["sql"]),
                    },
                ))
    if len({case["id"] for case in prepared}) != len(prepared):
        raise ValueError("Duplicate derived case IDs.")
    return {"schemaVersion": 1, "sources": [source for source in corpus["sources"] if source["source"] == "sqlglot"], "cases": prepared}


def freeze(corpus: dict, requests: dict, reference: dict, plan: bytes, license_text: bytes) -> tuple[dict, dict]:
    raw = originals(corpus)
    references = reference_map(reference)
    cases = requests["cases"]
    if [case["id"] for case in cases if case["kind"] == "input"] != [case["id"] for case in raw]:
        raise ValueError("Original occurrence membership/order changed.")
    for expected, actual in zip(raw, cases):
        if {key: actual[key] for key in expected} != expected:
            raise ValueError("Original source/provenance was changed.")
    entries = [classify(case, references[case["id"]]) for case in cases]
    core_ids = [entry["id"] for entry in entries if entry["disposition"] == "core"]
    fixture_cases = [dict(case, sqlSha256=sha256(case["sql"])) for case in cases]
    source = {
        "source": "sqlglot", "repository": "tobymao/sqlglot", "revision": UPSTREAM_SHA,
        "license": "MIT", "licensePath": "licenses/SQLGlot.LICENSE",
        "upstreamLicensePath": "LICENSE", "licenseSha256": sha256(license_text),
        "path": "tests/dialects/test_tsql.py",
        "url": f"https://github.com/tobymao/sqlglot/blob/{UPSTREAM_SHA}/tests/dialects/test_tsql.py",
    }
    fixtures = {"schemaVersion": 1, "source": source, "cases": fixture_cases}
    profile = {
        "schemaVersion": 1,
        "profile": "sqlglot-core-v1",
        "source": source,
        "reference": REFERENCE,
        "approvedPlanSha256": sha256(plan),
        "classificationBasis": "Approved feature scope and ScriptDom-only syntax evidence; never Cyqwel implementation outcomes.",
        "originalCount": len(raw),
        "derivedCount": len(cases) - len(raw),
        "originalDispositions": dict(sorted(Counter(e["disposition"] for e in entries if e["kind"] == "input").items())),
        "derivedDispositions": dict(sorted(Counter(e["disposition"] for e in entries if e["kind"] != "input").items())),
        "coreCount": len(core_ids),
        "contextSensitiveCount": sum(case.get("derivation", {}).get("contextStable") is False for case in cases),
        "originalIdsSha256": ids_hash([case["id"] for case in raw]),
        "originalCorpusSha256": canonical_hash(raw),
        "allIdsSha256": ids_hash([case["id"] for case in cases]),
        "coreIdsSha256": ids_hash(core_ids),
        "coreIds": sorted(core_ids),
        "reviewNotes": [
            "Reviewed individual inputs, not method-name exclusions. Storage/native/security headers do not erase independently valid ordinary nested statement coverage.",
            "Strings, quoted names, comments and JSON paths never trigger feature deferrals. XML instance calls at test_tsql.py:61 are explicitly reviewed separately from qualified ordinary functions.",
            "Bare #x/##x/@x, scalar/predicate fragments and isolated GO/BEGIN/END are not implicit EXEC statement requirements. Separate attributed probes retain the original text unchanged.",
            "Named/variable transactions, WITH MARK and explicit commit durability options are treated as transaction syntax, not wholesale administration exclusions.",
            "Plain CREATE SCHEMA and PRINT are ordinary application statements. Schema AUTHORIZATION and actual security/admin operations remain deferred.",
            "Basic TABLESAMPLE ROWS/PERCENT is retained; its REPEATABLE extension is deferred as advanced sampling.",
            "FOR BROWSE, procedure-level WITH options, remote joins and unlisted optimizer/XML modes are outside the bounded query/output surface.",
            "Reference-invalid statement-looking inputs have no recovered or repaired derivatives. Mixed deferred inputs may contribute unchanged independently validated statement slices.",
        ],
        "entries": entries,
    }
    return profile, fixtures


def validate(profile: dict, fixtures: dict, license_text: bytes) -> None:
    """Validate the reviewed v1 contract, not a self-declared replacement."""
    if profile["schemaVersion"] != 1 or fixtures["schemaVersion"] != 1 or profile["reference"] != REFERENCE:
        raise ValueError("Unexpected frozen profile/reference schema.")
    if profile["source"] != fixtures["source"] or profile["source"]["revision"] != UPSTREAM_SHA:
        raise ValueError("Frozen source provenance changed.")
    if sha256(license_text) != profile["source"]["licenseSha256"]:
        raise ValueError("The pinned SQLGlot license notice changed.")
    entries = {entry["id"]: entry for entry in profile["entries"]}
    cases = {case["id"]: case for case in fixtures["cases"]}
    if len(entries) != len(profile["entries"]) or len(cases) != len(fixtures["cases"]):
        raise ValueError("Duplicate disposition or fixture IDs.")
    if entries.keys() != cases.keys():
        raise ValueError("Every fixture must have exactly one disposition.")
    raw = [case for case in fixtures["cases"] if case["kind"] == "input"]
    if len(raw) != ORIGINAL_COUNT or profile["originalCount"] != ORIGINAL_COUNT:
        raise ValueError("The original 720-occurrence denominator changed.")
    if len(cases) - len(raw) != profile["derivedCount"]:
        raise ValueError("Derived occurrence count changed.")
    if ids_hash([case["id"] for case in raw]) != profile["originalIdsSha256"]:
        raise ValueError("Original source IDs changed.")
    if canonical_hash([{field: case[field] for field in SOURCE_FIELDS} for case in raw]) != profile["originalCorpusSha256"]:
        raise ValueError("Original source text or provenance changed.")
    if ids_hash(list(cases)) != profile["allIdsSha256"]:
        raise ValueError("Profile fixture membership changed.")
    for identifier, entry in entries.items():
        case = cases[identifier]
        if entry["disposition"] not in DISPOSITIONS or not entry["reason"] or not entry["requirements"]:
            raise ValueError(f"Missing reviewed disposition/requirements for {identifier}.")
        if entry["kind"] != case["kind"] or entry["sourceId"] != case["sourceId"]:
            raise ValueError("Profile and fixture provenance differ.")
        if entry["sqlSha256"] != sha256(case["sql"]) or case["sqlSha256"] != entry["sqlSha256"]:
            raise ValueError(f"SQL changed for {identifier}.")
        if entry["disposition"] == "core" and not entry["referenceAccepted"]:
            raise ValueError("A required positive fixture is not reference-valid.")
        if case["kind"] == "input":
            if case["sourceId"] != identifier:
                raise ValueError("An original occurrence must retain its original source ID.")
            continue
        parent = cases.get(case["sourceId"])
        if parent is None or parent["kind"] != "input":
            raise ValueError("A derived fixture must identify its original input.")
        derivation = case["derivation"]
        if derivation["originalSqlSha256"] != parent["sqlSha256"]:
            raise ValueError("Derived fixture parent hash changed.")
        if any(case[field] != parent[field] for field in ("source", "path", "line")):
            raise ValueError("Derived fixture source coordinates changed.")
        parent_entry = entries[parent["id"]]
        if case["kind"] == "statement-slice":
            if not parent_entry["referenceAccepted"] or parent_entry["disposition"] in {"not-statement", "reference-rejected"}:
                raise ValueError("No partial recovery or implicit-EXEC slices are allowed.")
            if derivation["offsetEncoding"] != REFERENCE["offsetEncoding"]:
                raise ValueError("Statement slice offsets must retain their UTF-16 provenance.")
            if utf16_slice(parent["sql"], derivation["offset"], derivation["length"]) != case["sql"]:
                raise ValueError("A statement slice must be unchanged original text.")
            if entry["disposition"] == "core" and not derivation["contextStable"]:
                raise ValueError("A context-sensitive slice cannot silently enter the core gate.")
        else:
            if parent_entry["disposition"] != "not-statement":
                raise ValueError("Probes must not repair rejected statement-looking inputs.")
            if derivation["prefix"] + parent["sql"] + derivation["suffix"] != case["sql"]:
                raise ValueError("A probe must preserve its complete original fragment.")
    core = sorted(entry["id"] for entry in entries.values() if entry["disposition"] == "core")
    if core != profile["coreIds"] or len(core) != profile["coreCount"] or ids_hash(core) != profile["coreIdsSha256"]:
        raise ValueError("Frozen core membership changed.")
    for field, original in (("originalDispositions", True), ("derivedDispositions", False)):
        actual = dict(Counter(e["disposition"] for e in entries.values() if (e["kind"] == "input") == original))
        if actual != profile[field]:
            raise ValueError(f"{field} changed.")
    if profile["contextSensitiveCount"] != sum(case.get("derivation", {}).get("contextStable") is False for case in cases.values()):
        raise ValueError("Context-sensitive slice accounting changed.")
    if canonical_hash(profile) != FROZEN_PROFILE_CANONICAL_HASH:
        raise ValueError("Frozen profile content changed; recomputing its checksums does not authorize reclassification.")


def campaign_corpus(fixtures: dict) -> dict:
    """Build a separate evaluation corpus; never replace the raw campaign corpus."""
    return {
        "schemaVersion": 1,
        "sources": [fixtures["source"]],
        "cases": [{field: case[field] for field in SOURCE_FIELDS} for case in fixtures["cases"]],
        "exclusions": [],
    }


def summarize(profile: dict, report: dict) -> dict:
    """Report the frozen core separately, including missing cases rather than hiding them."""
    results = {}
    entries = {entry["id"]: entry for entry in profile["entries"]}
    for row in report["results"]:
        if row["scope"] != "input":
            continue
        if row["caseId"] in results:
            raise ValueError("Duplicate full-input campaign results.")
        entry = entries.get(row["caseId"])
        if entry is not None and (
            not isinstance(row.get("sql"), str) or sha256(row["sql"]) != entry["sqlSha256"]
        ):
            raise ValueError("Campaign input text does not match the frozen fixture hash.")
        results[row["caseId"]] = row
    required = profile["coreIds"]
    return {
        "profile": profile["profile"],
        "originalDispositions": profile["originalDispositions"],
        "derivedDispositions": profile["derivedDispositions"],
        "requiredCore": profile["coreCount"],
        "coreIdsSha256": profile["coreIdsSha256"],
        "missingCoreIds": [identifier for identifier in required if identifier not in results],
        "outcomes": dict(sorted(Counter(
            results[identifier]["outcome"] for identifier in required if identifier in results
        ).items())),
    }


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prepare_parser = commands.add_parser("prepare")
    freeze_parser = commands.add_parser("freeze")
    verify_parser = commands.add_parser("verify")
    export_parser = commands.add_parser("export")
    report_parser = commands.add_parser("report")
    for command in (prepare_parser, freeze_parser):
        command.add_argument("--corpus", type=Path, required=True)
        command.add_argument("--reference", type=Path, required=True)
    prepare_parser.add_argument("--output", type=Path, required=True)
    for name in ("requests", "plan", "license", "profile", "fixtures"):
        freeze_parser.add_argument("--" + name, type=Path, required=True)
    for command in (verify_parser, export_parser, report_parser):
        for name in ("profile", "fixtures", "license"):
            command.add_argument("--" + name, type=Path, required=True)
    for command in (export_parser, report_parser):
        command.add_argument("--output", type=Path, required=True)
    report_parser.add_argument("--results", type=Path, required=True)
    args = parser.parse_args()
    if args.command in {"verify", "export", "report"}:
        profile, fixtures = read_json(args.profile), read_json(args.fixtures)
        validate(profile, fixtures, args.license.read_bytes())
        if args.command == "verify":
            print("Frozen profile, denominator, derivations and license are consistent.")
        elif args.command == "export":
            write_json(args.output, campaign_corpus(fixtures))
            print(f"Exported a separate {len(fixtures['cases'])}-occurrence evaluation corpus.")
        else:
            summary = summarize(profile, read_json(args.results))
            write_json(args.output, summary)
            print(f"Required core: {summary['requiredCore']}; missing: {len(summary['missingCoreIds'])}.")
        return
    corpus, reference = read_json(args.corpus), read_json(args.reference)
    if args.command == "prepare":
        requests = prepare(corpus, reference)
        write_json(args.output, requests)
        decisions = [classify(case, reference_map(reference)[case["id"]]) for case in originals(corpus)]
        print("Original dispositions:", dict(Counter(e["disposition"] for e in decisions)))
        print("Prepared cases:", len(requests["cases"]))
    else:
        profile, fixtures = freeze(
            corpus, read_json(args.requests), reference, args.plan.read_bytes(), args.license.read_bytes(),
        )
        validate(profile, fixtures, args.license.read_bytes())
        write_json(args.profile, profile)
        write_json(args.fixtures, fixtures)
        print("Original dispositions:", profile["originalDispositions"])
        print("Derived dispositions:", profile["derivedDispositions"])
        print("Core count:", profile["coreCount"])
        print("Core IDs SHA256:", profile["coreIdsSha256"])


if __name__ == "__main__":
    main()

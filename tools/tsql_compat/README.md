# T-SQL compatibility campaign

This campaign measures Cyqwel against original SQLGlot T-SQL test inputs and
Microsoft ScriptDom parser fixtures. It does not restrict the corpus to cases
Cyqwel already accepts, and it does not execute SQL or downloaded test code.

## Run

From the repository root, with Python 3.10+ and the repository's .NET SDKs:

```sh
python3 tools/tsql_compat/run_campaign.py
```

The first run downloads the immutable revisions in `sources.json`, preserves
their license files, extracts inert test data, and restores the runner's .NET
dependencies. There is no Python package dependency. Downloads and reports go
under the ignored `artifacts/tsql-compat/` directory.

```sh
# Use an explicit artifact directory.
python3 tools/tsql_compat/run_campaign.py --output-dir /path/to/campaign

# Reuse the verified source cache without network acquisition.
python3 tools/tsql_compat/run_campaign.py --offline

# Inspect the complete corpus without running the evaluator.
python3 tools/tsql_compat/run_campaign.py --extract-only

# Reproduce one upstream test occurrence and its statement slices.
python3 tools/tsql_compat/run_campaign.py --offline --case 'caseId-field-from-results'
```

`--offline` applies to upstream acquisition; `dotnet run` may still need NuGet
restore if the .NET dependencies are not already available. For a fully offline
rerun after building, invoke the runner directly with `--no-build --no-restore`:

```sh
dotnet run --project tools/Cyqwel.TSqlCompatibility --configuration Release \
  --no-build --no-restore -- \
  --corpus artifacts/tsql-compat/corpus.json \
  --output artifacts/tsql-compat/results.json \
  --revision "$(git rev-parse HEAD)"
```

The source cache checks file hashes on every reuse. A modified or incomplete
cache is an error, not an excuse to silently download a moving branch. Use a
fresh output directory to reacquire the pinned data.

## Evidence and denominators

`corpus.json` records all extracted occurrences, immutable source revisions,
paths, source lines, and explicit extraction exclusions. Identical SQL at
different test locations remains separate evidence.

`results.json` contains every evaluated SQL unit, reference diagnostics,
Cyqwel parse diagnostics, generated SQL, round-trip output, and any AST
mismatches. `results.jsonl` is flushed after each unit so completed evidence
survives an interrupted run. `summary.json` and `summary.md` provide counts and
representative cases. Filtered runs are labeled and replace the reports in
their chosen output directory.
Use the result's `caseId`, not its statement-suffixed `id`, with `--case`.

Two denominators are kept separate:

- **Input:** each original test input or whole ScriptDom fixture, including
  `GO`, comments, and multiple statements.
- **Statement:** each top-level statement from a zero-error ScriptDom input,
  sliced using its original start offset and the next statement or real batch
  token boundary rather than generated SQL. Some ScriptDom fragment lengths
  omit closing tokens; trusting those lengths alone truncates valid inputs.
  Procedure bodies are not flattened into unrelated statements.

Do not add the two totals: they intentionally overlap. Every statement slice
is independently reference-parsed and compared with its original ScriptDom
context. A changed quoted-identifier interpretation is excluded explicitly.
Statements recovered from an invalid script are never silently admitted as
positive tests. This means a partly invalid fixture can contain valid
statements that this initial campaign does not assess separately.

The oracle is pinned `Microsoft.SqlServer.TransactSql.ScriptDom` 180.107.0,
using `Sql180` and initial `QUOTED_IDENTIFIER ON`. A zero-error reference parse
establishes grammar acceptance, not execution success, schema correctness, or
feature availability on a particular server. Legacy syntax, different quoting
modes, and fixtures newer than the NuGet release can be reference-rejected;
these are **not** counted as Cyqwel parsing gaps.

## Outcomes

| Outcome | Interpretation |
| --- | --- |
| `reference-rejected` | ScriptDom reports errors; excluded from the compatibility denominator. |
| `reference-empty` | No reference statements, such as a comment-only fixture. |
| `reference-context-changed` | An isolated statement changes meaning outside its original parsing context; excluded. |
| `parse-rejected` | ScriptDom accepts the original SQL but Cyqwel rejects it. |
| `generation-unsupported` | Cyqwel parses the input but explicitly cannot generate T-SQL. |
| `generated-reference-rejected` | ScriptDom accepts the input but rejects Cyqwel's generated SQL. |
| `reparse-rejected` | Cyqwel rejects its own generated SQL. |
| `round-trip-changed` | Cyqwel generation changes again after reparsing. |
| `ast-mismatch` | A targeted structural check disagrees with the reference AST. |
| `reference-changed` | ScriptDom's canonical representation changed; requires review, not automatically a semantic defect. |
| `round-trip-stable` | Generation and reference representation are stable under the implemented checks. |
| `exception` | An unexpected exception, with stage, type, and diagnostic details recorded. |

The explicit AST check covers top-level statement counts, plus projection aliases and variable
assignments in a single top-level `SELECT` query specification. It detects
cases such as `SELECT answer = 42` whose SQL can round-trip unchanged even
though Cyqwel models the alias assignment as an equality expression.

Canonical representation differences include harmless case, quoting,
parentheses, default ordering, and function normalization. They remain review
candidates. Conversely, stable SQL is not proof that the entire Cyqwel AST is
semantically correct; the targeted audit is deliberately labeled and bounded.

The command succeeds when measurement completes, even with known
compatibility gaps. Exit code 1 means a tooling/input failure; exit code 2
means unexpected per-case exceptions were recorded. Reports are still written
for the latter. This is a measurement campaign, not a claim of full SQL Server
support or an instruction to implement every administrative statement.

## Initial verified baseline

The first complete run used Cyqwel `5126fa82104611f4d9b664759c5bbf6dfae34558`
(updated `origin/main`, including national-string literal support), with the
immutable source revisions in `sources.json`.

| Source | Extracted inputs | Eligible inputs | Eligible statements | Statements parsed | Parsing rejections |
| --- | ---: | ---: | ---: | ---: | ---: |
| SQLGlot | 720 | 481 | 482 | 139 | 343 |
| ScriptDom | 583 | 466 | 4,125 | 479 | 3,646 |

Across the 618 parsed statement occurrences, the campaign found nine
reference-invalid generated outputs and three alias/assignment AST mismatch
occurrences. Two standalone `BREAK`/`CONTINUE` cases were explicitly rejected
by generation because they were outside a loop. There were 82 canonical
representation changes requiring review and 522 stable round trips. No
unexpected exceptions remained in the completed run.

These are fixture occurrences, not unique features or promises of support.
The large ScriptDom corpus includes administrative and advanced DDL syntax
outside Cyqwel's documented AST. Duplicate SQLGlot inputs retain their
separate test provenance.

High-signal findings include unquoted temporary tables and table variables,
omitted multipart-name components, table-valued functions/`OPENJSON`, table
and query hints, `SELECT INTO`, `FOR XML`/`FOR JSON`, compound assignments,
and optional T-SQL DML keywords. The targeted AST audit also demonstrates why
SQL round-trip checks alone missed `SELECT a = 1` and `SELECT @a = 1`.

Generation findings are reproducible with small inputs:

| Accepted input | Invalid generated construct |
| --- | --- |
| `ALTER TABLE dbo.t ADD c INT` | `ADD COLUMN` |
| `CREATE TABLE dbo.t (id INT IDENTITY)` | `GENERATED BY DEFAULT AS IDENTITY` |
| `SET IDENTITY_INSERT dbo.t ON` | `[ON]` |
| `SET STATISTICS TIME ON` | `[ON]` |

This table is historical evidence, not the current support matrix. The
implementation promotes these accepted-but-wrong cases into positive
regression tests and extends ordinary application SQL support. Administration
and advanced engine modes remain outside the implementation target. Keep the
baseline reports separate when measuring the current working tree.

## Implementation comparison

Re-evaluating the unchanged raw corpus after the scoped implementation gives:

| Source | Reference-valid statements | Baseline parsed | Implementation parsed |
| --- | ---: | ---: | ---: |
| SQLGlot | 482 | 139 | 317 |
| ScriptDom | 4,125 | 479 | 883 |

All previously parsed inputs and statements remain accepted. The refreshed
raw campaign records no invalid generated SQL, failed reparses, unstable
regeneration, targeted AST mismatches, or unexpected exceptions. The two
standalone `BREAK`/`CONTINUE` generation rejections remain explicit.
All 5,910 evidence rows agree between the JSON report and JSONL journal.

These raw totals are separate from the [frozen 511-case core gate](CORE_PROFILE.md),
which also includes attributed expression, identifier, predicate, batch, and
statement probes. Canonical-reference differences remain visible in the raw report;
they are not silently counted as identical SQL or full semantic proof.

## Maintaining the campaign

The extractors use the Python AST and original `.sql` fixtures, not regex
splitting at semicolons and not execution of upstream Python. SQLGlot supplies
test inputs, not an independently executed SQLGlot runtime oracle. Generated
ScriptDom baselines are not duplicate parser inputs.

SQLGlot scope is `tests/dialects/test_tsql.py`: all 473 input callsites at the
pin are accounted for, with no unresolved positive-input extraction.
Generated outputs, other-dialect inputs, and negative expectations are
explicit exclusions. ScriptDom scope includes `TestScripts` and
`PhaseOneTestScripts`; inline C# test strings are not yet included.
`CreateExternalTableStatementTests160.sql` has an invalid UTF-8 byte and is
explicitly excluded rather than decoded with replacement characters.

To refresh the corpus, deliberately update full commit hashes in
`sources.json`, reacquire into a fresh directory, and review extraction
exclusions and denominator changes before comparing rates. Keep provenance
and licenses with exported evidence. Changing the ScriptDom package or parser
mode also changes the oracle and requires a new baseline.
Pinned candidate/exclusion counts are checked so an extractor regression
cannot silently shrink the denominator; update them only after reviewing
an intentional source or extraction change.

The .NET campaign tests include positive AST assertions for corrected gaps
and explicit controls for unsupported syntax, not skipped tests claiming
support. When a gap is implemented, promote its characterization into a
positive AST assertion and refresh the campaign results. Normal CI exercises the extractor and evaluator tests
without downloading either corpus:

```sh
python3 -m unittest discover -s tools/tsql_compat -p 'test_*.py'
dotnet test --project tests/Cyqwel.Tests/Cyqwel.Tests.csproj \
  --configuration Release -- --filter-class Cyqwel.Tests.TSqlCompatibilityCampaignTests
```

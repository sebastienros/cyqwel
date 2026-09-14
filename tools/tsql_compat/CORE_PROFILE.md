# Frozen SQLGlot core gate

`core-profile.json` is the reviewed `sqlglot-core-v1` compatibility contract,
not a list of inputs the current parser happens to accept. Classification uses
the approved feature scope and ScriptDom-only syntax evidence. Implementation
results never select or remove required cases.

## Offline acceptance

From the repository root:

```sh
python3 -m unittest discover -s tools/tsql_compat -p 'test_core_profile.py'
dotnet run --project tests/Cyqwel.Tests -- \
  -class Cyqwel.Tests.TSqlCoreFixtureTests
```

The .NET command builds before running. Once dependencies are restored, a
network-free build and run use:

```sh
dotnet build tests/Cyqwel.Tests/Cyqwel.Tests.csproj --no-restore
dotnet run --project tests/Cyqwel.Tests --no-build --no-restore -- \
  -class Cyqwel.Tests.TSqlCoreFixtureTests
```

These tests read checked-in data; they never acquire upstream sources, execute
SQL, import downloaded Python, or fetch URLs contained in fixtures. SQLGlot
command passthrough is not evidence of valid T-SQL. The pinned ScriptDom parser
provides independent grammar acceptance, not server execution or schema
validation.

Every one of the **511 core IDs** must execute and pass. Contract tests assert
that the theory data enumerates exactly those IDs. A filtered run, a successful
characterization campaign, or an old test binary is not proof that this gate
passes. There are no temporary skips or known-failure allowances.

Each required input must:

- Be accepted by pinned ScriptDom and Cyqwel as a complete input.
- Generate T-SQL accepted by ScriptDom, preserving its reference AST.
- Pass the campaign's targeted Cyqwel AST checks.
- Retain the same generated SQL after Cyqwel reparse and no-op rewriting.

The reference-AST comparison permits narrowly controlled spelling and
structural equivalences, such as identifier quoting, documented datepart
aliases, transparent parentheses, and neutral `BEGIN`/`END` grouping.
`reference-changed` is not an automatic pass: this independent comparison must
also succeed. Loss-detection controls cover aliases versus assignments,
national strings, identity values, batch/procedure and `IF` ownership, table
sampling/hints, durability, JSON ordering/null clauses, and other typed data.
Ordinary `TRUE`/`FALSE` column references are not globally normalized into
integer constants. The seven explicitly attributed SQLGlot Boolean baselines
below resolve a documented difference in upstream intent, not a parser gap.

Normal checkout runs discover fixtures from the repository. For an isolated
test assembly outside the checkout, set `CYQWEL_TSQL_CORE_ROOT` to the checkout
containing `Cyqwel.slnx`. Missing files or changed hashes fail explicitly.

## Attributed SQLGlot Boolean baselines

SQLGlot explicitly interprets `TRUE`/`FALSE` as Boolean literals and expects
T-SQL `1`/`0` in seven frozen cases. ScriptDom instead parses these unquoted
words as column references. The pinned original test expectations, not Cyqwel's
current generated output, establish the intended values.

All IDs below begin with `sqlglot:tests/dialects/test_tsql.py:`.
They refer to the same immutable SQLGlot revision identified below.

| Frozen ID suffix | Explicit upstream expected T-SQL |
| --- | --- |
| `505:8:validate_identity.sql:0` | `SELECT val FROM (VALUES ((1), (0), (NULL))) AS t(val)` (`write_sql`) |
| `1112:8:validate_all.sql:0/core/predicate-probe` | `a = 1` (`write["tsql"]`) |
| `1114:8:validate_all.sql:0/core/predicate-probe` | `a <> 0` (`write["tsql"]`) |
| `1120:8:validate_all.sql:0/core/expression-probe` | `CASE WHEN a IN (1) THEN 'y' ELSE 'n' END` (`write["tsql"]`) |
| `1125:8:validate_all.sql:0/core/expression-probe` | `CASE WHEN NOT a IN (0) THEN 'y' ELSE 'n' END` (`write["tsql"]`) |
| `1130:8:validate_all.sql:0` | `SELECT 1, 0` (`write["tsql"]`) |
| `1132:8:validate_all.sql:0` | `SELECT 1 AS a, 0 AS b` (`write["tsql"]`) |

`TSqlCoreFixtureTests` retains those upstream expectations as attribution.
Only these exact IDs, with the original frozen SQL hash, may receive a
source-side semantic baseline. The baseline changes only the token spans of
unquoted, single-part `TRUE`/`FALSE` value references to `1`/`0`. Everything else
in the source is retained, including the original `NOT IN` spelling; this is
not wholesale substitution of SQLGlot's generated SQL. Derived probes use
their existing attributed wrappers.

The original corpus text, profile dispositions, initial reference parse,
Cyqwel parse, generated-SQL reference parse, targeted AST audits, reparse and
no-op rewrite assertions remain unchanged. Generated output is **never**
reinterpreted as Boolean intent. Unrelated IDs, ordinary column uses, quoted
names (`[TRUE]`, `"FALSE"`), multipart names (`t.TRUE`), strings and parameters
keep their existing meaning. Controls verify the pinned numeric expectations,
reject attaching an approved ID to changed source, and retain these boundaries.

## Frozen denominator and provenance

| Disposition | Original occurrences | Derived occurrences |
| --- | ---: | ---: |
| Core | 316 | 195 |
| Deferred | 160 | 8 |
| Not-statement | 185 | 0 |
| Reference-rejected | 59 | 8 |
| **Total** | **720** | **211** |

All **931 occurrences** have explicit reasons, requirements, reference facts,
source coordinates, and SQL hashes. Identical SQL at different upstream
callsites is not deduplicated. The original 720-input corpus remains unchanged.
The separate core gate is not substituted for the raw campaign denominator.

The 211 derivatives contain 179 expression probes, two predicate probes, one
batch probe, three block probes, and 26 unchanged statement slices. Probes
retain the complete original fragment with explicit prefix/suffix attribution.
Bare `#x`/`##x`/`@x` are not implicit-`EXEC` API requirements. Statement slices
identify the original input and exact UTF-16 offsets, and are independently
revalidated without changing their parsing context. Rejected full scripts are
never repaired or partially recovered into positive derivatives.

Basic `TABLESAMPLE ... ROWS/PERCENT`, ordinary `INDEX` hints, plain
`CREATE SCHEMA`, table variables, rowstore indexes, inline TVFs, and
transactions including explicit delayed durability remain core.
`TABLESAMPLE ... REPEATABLE`, `FOR BROWSE`, and advanced storage/admin/security
forms retain their frozen deferrals. Strings, comments, quoted names, JSON
paths, and dynamic SQL text do not trigger keyword-based deferrals.

The SQLGlot source is
[`tobymao/sqlglot` at `5cfb5997a99010940138670adf3d6b34ac5a0a08`](https://github.com/tobymao/sqlglot/blob/5cfb5997a99010940138670adf3d6b34ac5a0a08/tests/dialects/test_tsql.py).
Its MIT notice is preserved verbatim at
[`SQLGlot.LICENSE`](../../tests/Cyqwel.Tests/Fixtures/TSqlCore/licenses/SQLGlot.LICENSE).
Keep that notice with copied/exported fixture data. The reference dependency
is `Microsoft.SqlServer.TransactSql.ScriptDom` **180.107.0**, parser `Sql180`,
initial quoted identifiers enabled, as pinned in `Directory.Packages.props`.

| Protected artifact | SHA-256 |
| --- | --- |
| Profile file | `5dbea22035ebeecf9a623c98f123a7e1a723bf7f57a971df5d828525184c5253` |
| Fixture file | `bc06eab66a62fd8c566db938512e5447b8ed11ea04de37bd7d35a5353511eb52` |
| MIT notice | `b0bf909b472ddf9bd4eab30f6e6fd8b33bf3997ce038b882fe0663f291958d2d` |
| Core ID set | `ae0343164b731756a72fea0241b404481aab44cc285e5dba5a3bf9937ed285ab` |

ID-set hashes use ordinal-sorted IDs, each followed by a newline, encoded as
UTF-8. Original text/provenance and all fixture IDs have additional independent
digests in the profile. The verifier also pins the canonical complete profile,
so recomputing counts and checksums cannot silently authorize reclassification.
Repository attributes require LF checkouts for the byte-pinned profile,
fixtures, and license, including on Windows with `core.autocrlf=true`.
Keep those attributes with the data; verification intentionally checks the
original bytes rather than normalizing line endings before hashing.

## Verification, separate reports, and regeneration

```sh
python3 tools/tsql_compat/core_profile.py verify \
  --profile tools/tsql_compat/core-profile.json \
  --fixtures tests/Cyqwel.Tests/Fixtures/TSqlCore/sqlglot.json \
  --license tests/Cyqwel.Tests/Fixtures/TSqlCore/licenses/SQLGlot.LICENSE
```

`export` takes the same three inputs plus `--output` and creates a **separate**
931-occurrence corpus for the campaign runner. Never overwrite the original
raw corpus with it. `report` additionally takes `--results` and `--output`;
it uses frozen membership, rejects duplicate/mutated full-input results, and
explicitly lists missing required IDs. Statement rows cannot stand in for
required whole-input rows. Reporting does not make a measurement run into a
positive acceptance test.

The optional reference-only regeneration sequence is:

1. Run `CoreReference <raw-corpus.json> <reference-originals.json>`.
2. Run `core_profile.py prepare --corpus ... --reference ... --output requests.json`.
3. Run `CoreReference <requests.json> <reference-all.json>`.
4. Run `core_profile.py freeze --corpus ... --requests ... --reference ... --plan ... --license ... --profile ... --fixtures ...`.

`CoreReference` means `dotnet run --project tools/tsql_compat/CoreReference --`
followed by its two paths. Regeneration requires the original pinned corpus,
license and approved-plan bytes; it uses no Cyqwel result data. Generate into
new artifact paths and compare byte-for-byte before replacing any file.
The v1 verifier deliberately rejects an altered contract. A future scope or
oracle revision requires an explicitly reviewed, separately versioned profile;
it must not relax this gate to accommodate implementation failures.

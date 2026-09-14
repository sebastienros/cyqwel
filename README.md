![Cyqwel](https://raw.githubusercontent.com/sebastienros/cyqwel/main/assets/banner.png)

# Cyqwel

Cyqwel is a dialect-neutral SQL toolkit for .NET. It parses SQL into an immutable C# syntax tree that can be inspected, transformed, validated, generated for another dialect, or created with fluent builders.

## Features

- Parse SQL using Generic SQL, T-SQL, SQLite, PostgreSQL, MySQL, or Oracle syntax
- Inspect and transform SQL through a shared syntax tree
- Generate, transpile, and format dialect-aware SQL
- Validate SQL syntax, semantics, schemas, types, and relationships
- Build queries and data modification statements with a fluent API
- Define and register custom SQL dialects

T-SQL compatibility is tracked by a frozen SQLGlot Core profile, with
ScriptDom-backed grammar and structural checks. It targets ordinary
application SQL, not exhaustive SQL Server administration or advanced modes.
See the [compatibility campaign](tools/tsql_compat/README.md) for scope,
provenance, fixture gates, and the unfiltered baseline.

## Install

```bash
dotnet add package Cyqwel
```

Preview packages are published to [Feedz](https://f.feedz.io/sebastienros/cyqwel/nuget/index.json) after a successful Linux build and test run for each push to `main`. Versions use the `preview-<GitHub run number>` suffix.

```bash
dotnet add package Cyqwel --prerelease --source https://f.feedz.io/sebastienros/cyqwel/nuget/index.json
```

## Parse SQL

Select a dialect when the input uses dialect-specific syntax:

```csharp
using Cyqwel.Dialects;

var document = SqlDialects.TSql.Parse(
    "SELECT TOP 10 [display name] FROM [users]");

var statement = document.Statements[0];
```

`Parse` throws `SqlParseException` for invalid or incompatible SQL. Use `TryParse` when parse failures are expected:

```csharp
if (!SqlDialects.PostgreSql.TryParse(
    "SELECT * FROM users",
    out var document,
    out var error))
{
    Console.WriteLine($"{error!.Code}: {error.Message}");
}
```

### National string literals

Generic SQL, T-SQL, PostgreSQL, MySQL, and Oracle recognize adjacent `N'...'`
and `n'...'` prefixes and preserve them through `LiteralExpression.IsNational`.
MySQL also accepts double-quoted national strings. Generation keeps the prefix
for those dialects and emits ordinary strings for SQLite.

T-SQL and Generic SQL accept national strings as column aliases, with or without
`AS`, such as `SELECT 1 AS N'display name'`. Aliases are normalized to identifiers.
MySQL and Generic SQL accept expression-valued interval amounts, including
`DATE_ADD(created_at, INTERVAL N'1' DAY)` and `created_at + INTERVAL (N'1') DAY`.
Custom dialects inherit these capabilities and can configure them with
`SupportsNationalStringAliases` and `SupportsExpressionIntervalValues`.

PostgreSQL's typed-literal syntax still requires ordinary strings. When targeting
a literal-only interval grammar, generation removes the national prefix and
redundant parentheses from interval literals without changing the original AST.
Nonliteral interval expressions throw for such targets unless unsupported SQL is
explicitly allowed.

### T-SQL names and expressions

T-SQL preserves temporary names (`#work`, `##work`), table variables (`@rows`),
system variables (`@@ROWCOUNT`), omitted multipart components (`db..objects`),
and qualified user-defined function names. Table variables remain distinct
from physical tables during traversal and renaming.

`SELECT label = expression` produces a projection alias, while
`SELECT @total += amount` records a variable assignment with an explicit
target and operator. `CONVERT`/`TRY_CONVERT`, including optional styles, and
`VARCHAR`/`NVARCHAR`/`VARBINARY(MAX)` use typed nodes rather than treating type
arguments as column references. `ANY`, `ALL`, and `SOME` comparisons retain
their subqueries and participate in correlated-column validation.
Native T-SQL `||` is preserved as `BinaryOperator.AnsiConcatenate`
(`Sql.AnsiConcat`), distinct from dialect-normalized concatenation or `+`.
`JSON_ARRAYAGG` retains its internal ordering and `NULL ON NULL` /
`ABSENT ON NULL` behavior in `JsonArrayAggregateExpression` (`Sql.JsonArrayAgg`).

Builders expose the same distinctions through `Sql.TableVariable`,
`Sql.SystemVariable`, `Sql.SelectAssign`, `Sql.MaxLengthType`,
`Sql.Convert`, and `Sql.QuantifiedComparison`. Native constructs that cannot
be represented by another dialect fail generation by default.

### T-SQL query extensions

The T-SQL dialect supports derived-table column aliases, structured table-valued
function sources, `CROSS APPLY`/`OUTER APPLY`, and `OPENJSON` with paths and typed
`WITH` schemas (including `AS JSON`). `SELECT INTO`, basic `PIVOT`/`UNPIVOT`,
ordinary table hints, and local `HASH`/`LOOP`/`MERGE` join hints are represented
in the syntax tree rather than retained as SQL text.
`INDEX(name[, ...])` and `INDEX = name` table hints support named indexes and
numeric index IDs. `NamedTable.Hints` retains hint and index-list order through
typed `TSqlTableHint` and `TSqlIndexReference` nodes; index names are not treated
as table-column references.
Basic `TABLESAMPLE` supports row counts or percentages, optional `SYSTEM`, and
typed `NamedTable.Sample` metadata. Repeatable sampling remains outside this
bounded surface.
An `INSERT ... SELECT` can consume a nested `MERGE ... OUTPUT` through
`DerivedMutationTable`, preserving its output-column aliases and statement body.
Table-valued function names retain their identifier casing and quoting regardless
of `FunctionNameCase`; scalar functions in their arguments still follow that option.

Query tails support `FOR JSON AUTO/PATH` with `ROOT`, `INCLUDE_NULL_VALUES`,
and `WITHOUT_ARRAY_WRAPPER`, and `FOR XML AUTO/PATH/RAW` with `TYPE` and `ROOT`.
Bounded `OPTION` support includes `RECOMPILE`, `MAXDOP`, `MAXRECURSION`,
`OPTIMIZE FOR UNKNOWN`, `FORCE ORDER`, and `FAST`. Advanced XML modes, remote
join hints, and unlisted optimizer options are not accepted.

Use `SelectStatement.Into`, `SqlQuery.ResultFormat`, and
`SqlStatement.QueryOptions` to inspect these clauses. Sources use
`TableFunction`, `OpenJsonTable`, `PivotTable`, and `UnpivotTable`; their schema,
alias, and expression children participate in visitors and rewriters.
`SelectBuilder` provides `Into`, `CrossApply`, `OuterApply`, `ResultFormat`,
and `QueryOptions`; set-query builders also expose the two query-tail methods.
T-SQL extensions are enabled by `SupportsTSqlExtensions` (inherited by custom
T-SQL dialects, not enabled by Generic SQL). Generation to an unsupported
target throws by default instead of discarding the clauses.
Standard derived-table column alias lists use the separate
`SupportsDerivedTableColumnAliases` capability; Generic SQL, PostgreSQL, and
MySQL also support them, while SQLite and Oracle reject their generation.

## Inspect and transform SQL

Traversal helpers expose tables, columns, node types, and depth-first or breadth-first enumeration. Transforms return a new tree and leave the source unchanged.

```csharp
using Cyqwel;
using Cyqwel.Dialects;
using Cyqwel.Visitors;

var source = SqlDialects.PostgreSql.Parse(
    "SELECT u.id FROM users AS u");

var tables = source.GetTableNames();   // ["users"]
var columns = source.GetColumnNames(); // ["u.id"]

var transformed = source
    .RenameTable("users", "accounts")
    .RenameColumn("id", "account_id");

var sql = transformed.ToSql(SqlDialects.PostgreSql);
// SELECT u.account_id FROM accounts AS u
```

Derive from `SqlVisitor` for typed, read-only analysis or from `SqlRewriter` for custom non-mutating transformations. `FindAll<T>`, `DescendantsAndSelf`, and `BreadthFirst` support direct tree queries.

## Generate, transpile, and format SQL

Generate a syntax tree for any built-in dialect, or transpile directly from a known source dialect:

```csharp
using Cyqwel.Dialects;

var postgres = SqlDialects.TSql.Transpile(
    "SELECT TOP 10 [id] FROM [users]",
    SqlDialects.PostgreSql);

// SELECT "id" FROM "users" LIMIT 10
```

Use `SqlGenerationOptions` to produce formatted SQL:

```csharp
using Cyqwel.Generation;
using Cyqwel.Parsing;

var document = SqlParser.Parse(
    "select id, name from users where active = true");

var formatted = document.ToSql(options: new SqlGenerationOptions
{
    PrettyPrint = true,
    IndentSize = 2,
});

// SELECT
//   id, name
// FROM users
// WHERE active = TRUE
```

Generation uses the target dialect's identifier quoting, parameters, functions, and row-limiting syntax. Unsupported constructs throw by default.

## Validate SQL

`SqlValidator` returns diagnostics with a severity, code, message, and source location. Syntax and semantic validation work without a schema:

```csharp
using Cyqwel.Dialects;
using Cyqwel.Validation;

var result = SqlValidator.Validate(
    "SELECT * FROM users LIMIT 10",
    SqlDialects.PostgreSql,
    new SqlValidationOptions
    {
        StrictSyntax = true,
        Semantic = true,
    });

foreach (var diagnostic in result.Diagnostics)
{
    Console.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
}
```

Provide a catalog to validate table and column names, data types, and relationships:

```csharp
using Cyqwel.Validation;

var catalog = new SqlSchemaCatalog(
    new SqlTableSchema(
        "users",
        [
            new("id", "integer", IsPrimaryKey: true),
            new("age", "integer"),
        ],
        PrimaryKey: ["id"]));

var result = SqlValidator.Validate(
    "SELECT id FROM users WHERE age > 18",
    catalog,
    options: new SqlSchemaValidationOptions
    {
        CheckTypes = true,
        CheckReferences = true,
    });

if (!result.IsValid)
{
    // Handle validation errors.
}
```

Schema findings are errors by default. Set `SqlSchemaValidationOptions.Strict` to `false` to report them as warnings.

## Build SQL

Fluent builders create the same syntax tree types as the parser:

```csharp
using Cyqwel;
using Cyqwel.Dialects;

var query = Sql.Select("u.id", "u.name")
    .From("users", "u")
    .Where(Sql.Col("u.age").GreaterThan(Sql.Param("minimumAge")))
    .OrderBy(Sql.Col("u.name"))
    .Limit(10)
    .Build();

var sql = query.ToSql(SqlDialects.PostgreSql);
// SELECT u.id, u.name FROM users AS u
// WHERE u.age > @minimumAge ORDER BY u.name ASC LIMIT 10
```

Builders cover every statement represented by the syntax tree, including advanced queries,
data modification, and DDL. Query builders compose through their built syntax trees:

```csharp
var ranked = Sql.SelectItems(
        new SelectItem(
            Sql.Func("ROW_NUMBER").Over(
                partitionBy: [Sql.Col("region")],
                orderBy: [Sql.Order(Sql.Col("amount"), OrderDirection.Descending)]),
            "position"))
    .From("sales")
    .With("active_regions", Sql.Select("id").From("regions").Build())
    .Qualify(Sql.Col("position").LessThanOrEqualTo(Sql.Lit(3)))
    .Build();

var combined = ranked
    .ToSql(SqlDialects.PostgreSql);
```

The fluent API supports recursive and materialized CTEs, `UNION` / `INTERSECT` /
`EXCEPT`, `VALUES`, named and inline windows, `QUALIFY`, hierarchical queries,
advanced joins, `EXPLAIN`, `MERGE`, `UPDATE FROM`, `DELETE USING`,
`RETURNING INTO`, and all supported DDL statements.

T-SQL mutations additionally model `TOP`, joined `UPDATE`/`DELETE`, `DEFAULT VALUES`,
and a typed `TSqlOutputClause`. Its `Into` property is a destination table (including
table variables), not `ReturningInto` bind variables. Mutation builders expose
`With` for CTE prefixes, `Top`, `Output`, and bounded `Option` methods. Unsupported target dialects reject
these native semantics rather than silently dropping them.

Application DDL preserves identity seed/increment, named defaults and keys, rowstore
clustering and key directions, native `ALTER COLUMN`, `ALTER VIEW`/`CREATE OR ALTER VIEW`,
and structural inline table-valued function bodies. Plain T-SQL `CREATE SCHEMA name`
is modeled by `CreateSchemaStatement` and built with `Sql.CreateSchema(name)`;
authorization clauses and embedded schema elements are not supported. T-SQL identity generation uses
`IDENTITY`; SQL-standard `GENERATED ALWAYS` remains distinct and is not silently
converted to T-SQL's different identity semantics.

Builder helpers only expose forms that can be parsed by at least one built-in
dialect, so generated builder SQL can round-trip through the syntax tree.

### Current timestamps

Use `Sql.CurrentTimestamp()` for a dialect-neutral current timestamp:

```csharp
var query = Sql.SelectItems(new SelectItem(Sql.CurrentTimestamp())).Build();

query.ToSql(SqlDialects.TSql);       // SELECT GETDATE()
query.ToSql(SqlDialects.PostgreSql); // SELECT CURRENT_TIMESTAMP
query.ToSql(SqlDialects.Oracle);     // SELECT CURRENT_TIMESTAMP
```

Generic SQL, MySQL, and SQLite also generate bare `CURRENT_TIMESTAMP`. Parsing
normalizes bare `CURRENT_TIMESTAMP`, T-SQL `GETDATE()`, PostgreSQL/MySQL `NOW()`,
and Oracle `SYSDATE` into `CurrentTimestampExpression`. The generic parser accepts
all these forms. Empty-parenthesis `CURRENT_TIMESTAMP()` is also normalized for
MySQL and generic SQL.

`CurrentTimestampKind` selects `Default`, `SystemDate`, or `Utc`.
Oracle `SYSDATE` parses with `Kind = CurrentTimestampKind.SystemDate` and renders
back to `SYSDATE` for Oracle; other targets use their ordinary current timestamp.
The default kind and the no-argument builder keep the behavior shown above.

Request UTC explicitly with `Sql.CurrentTimestamp(CurrentTimestampKind.Utc)`:

| Target dialect | UTC expression |
|---|---|
| Generic, MySQL | `UTC_TIMESTAMP()` |
| T-SQL | `GETUTCDATE()` |
| PostgreSQL | `TIMEZONE('UTC', CURRENT_TIMESTAMP)` |
| Oracle | `SYS_EXTRACT_UTC(CURRENT_TIMESTAMP)` |
| SQLite | `DATETIME('now')` |

UTC mode returns UTC date/time fields without a timezone offset, not a
session-local display of a timezone-aware value. PostgreSQL and Oracle return
timestamps without timezone metadata; SQLite returns text. Native SQL types,
precision, and transaction/statement/wall-clock timing still vary by database.
No session timezone is changed. SQLite's default `CURRENT_TIMESTAMP` is already
UTC; `DATETIME('now')` lets the explicit UTC kind round-trip through the parser.

The UTC expressions above parse back into the UTC kind in their respective
dialects. MySQL also accepts bare `UTC_TIMESTAMP`, PostgreSQL recognizes
`TIMEZONE('UTC', NOW())`, and Oracle recognizes `SYS_EXTRACT_UTC(SYSTIMESTAMP)`.
The generic parser accepts all these spellings.

Only unquoted, unqualified built-ins without aggregate or window modifiers are
normalized. UTC wrappers are recognized only for the specific current-time
arguments above, not arbitrary timestamps or timezones. Precision-bearing calls
remain ordinary function expressions and retain their arguments; unsupported
timestamp argument forms throw during generation unless `UnsupportedBehavior`
is `Ignore`. Higher-precision variants such as `SYSUTCDATETIME()`, local-time,
date-only, and separate wall-clock functions are outside this normalization.

Oracle queries without `FROM` target Oracle 23+; generation does not insert
`FROM DUAL`.

## Stored procedures

Stored procedures can be parsed, inspected, transformed, validated, and generated for T-SQL, PostgreSQL, MySQL,
Oracle, or generic SQL. SQLite reports that it does not support procedures and
generation throws by default.

```csharp
var procedure = Sql.CreateProcedure("app.activate_user")
    .Parameter("user_id", "INTEGER")
    .Variable("changed", "INTEGER", Sql.Lit(0))
    .Statement(Sql.Update("users")
        .Set("active", true)
        .Where(Sql.Col("id").EqualTo(Sql.Local("user_id")))
        .Build())
    .While(
        Sql.Local("changed").GreaterThan(Sql.Lit(0)),
        [
            Sql.If(
                Sql.Local("changed").EqualTo(Sql.Lit(1)),
                [Sql.Break()]),
            Sql.Continue(),
        ])
    .Return()
    .Build();

var createSql = procedure.ToSql(SqlDialects.PostgreSql);
var callSql = Sql.CallProcedure("app.activate_user")
    .Argument(42)
    .ToSql(SqlDialects.PostgreSql);
```

The structured procedure subset includes typed `IN`, `OUT`, and `INOUT`
parameters, defaults, local variables, `IF` / `ELSE`, `WHILE`, `BREAK`,
`CONTINUE`, early `RETURN`, and the SQL statements already supported by Cyqwel.
`BREAK` and `CONTINUE` always target the innermost loop; Cyqwel generates the
labels required by MySQL and generic SQL automatically. `FOR` loops, cursors,
handlers, and arbitrary procedural bodies are outside this subset.

T-SQL scripts also support scalar and table `DECLARE`, compound `SET @variable`
assignments, `SET NOCOUNT` / `SET XACT_ABORT`, `PRINT`, procedure calls and
return-status capture, return values,
and ordinary `BEGIN TRANSACTION`, `COMMIT`, and `ROLLBACK` (including named
transactions, `WITH MARK`, and the nullable `DelayedDurability` ON/OFF option
on `COMMIT`). Semicolons are optional. Plain line-delimited
`GO` is represented by `SqlDocument.Batches`; `SqlDocument.Statements` remains
the flattened statement view. A procedure's `AS` statement list ends at its
batch boundary, and SQL text passed to `EXEC` or `EXEC(...)` remains argument
data without being parsed as a routine body. GO counts,
SQLCMD, distributed transactions, and other transaction options are not supported.
Declarations within a T-SQL body remain `DeclareStatement` or
`TableVariableDeclarationStatement` entries in source order. Builder
`Variable` calls after a statement likewise stay at that position; generating
such declarations for an unsupported dialect fails explicitly.
Schema validation infers computed table-variable column types, excludes these
columns from implicit insert targets, and rejects assignments to them.

Reusable procedural nodes use the `Procedural*` prefix (`ProceduralBlock`,
`ProceduralIfStatement`, `ProceduralWhileStatement`, and the loop-control nodes),
while declarations use `LocalVariable` and references use
`LocalVariableExpression`. The short builder helpers are `Sql.Local`, `Sql.If`,
`Sql.While`, `Sql.Break`, `Sql.Continue`, and `Sql.Return`.

The same nodes can be executed outside a procedure through an anonymous block:

```csharp
var block = Sql.Block()
    .Variable("attempt", "INTEGER", Sql.Lit(0))
    .While(
        Sql.Local("attempt").LessThan(Sql.Lit(3)),
        [Sql.If(Sql.Local("attempt").EqualTo(Sql.Lit(2)), [Sql.Break()])])
    .Return()
    .Build();

var sql = block.ToSql(SqlDialects.PostgreSql);
```

Anonymous blocks are supported by Generic SQL, T-SQL, PostgreSQL, and Oracle,
and exposed through `SupportsAnonymousProceduralBlocks`. MySQL and SQLite report
this capability as false, so generation throws by default or omits the complete
block with `UnsupportedBehavior.Ignore`. T-SQL additionally permits `IF`,
`WHILE`, and `RETURN` directly at batch level. Loop control must still be nested
inside a `WHILE`.

## Extend dialects

Create a dialect from an existing one and override only the behavior your application needs:

```csharp
using Cyqwel;
using Cyqwel.Dialects;

var warehouse = SqlDialectBuilder.Create("warehouse")
    .BasedOn(SqlDialects.PostgreSql)
    .WithFunctionNameTransform(name =>
        name.Equals("LEN", StringComparison.OrdinalIgnoreCase)
            ? "LENGTH"
            : name)
    .Register();

var sql = warehouse.Generate(Sql.Func("LEN", Sql.Col("name")));
// LENGTH(name)
```

Custom dialects can configure parsing, transform syntax nodes, and customize literal or function rendering. Registered dialects are available by name through `SqlDialectRegistry`.

New dialects start with stored procedures and anonymous blocks disabled. A
builder based on a built-in dialect inherits that dialect's routine grammar and
generation behavior; enabling either capability on a base without routines opts
into the Generic SQL structured subset.

## Supported dialects

Cyqwel includes Generic SQL, T-SQL, SQLite, PostgreSQL, MySQL, and Oracle dialects. The shared syntax tree covers common relational queries, data modification, and schema statements while dialects handle source compatibility and target-specific SQL generation.

### T-SQL compatibility campaign

The [T-SQL campaign](tools/tsql_compat/README.md) checks original SQLGlot and
Microsoft ScriptDom parser fixtures against Cyqwel, with pinned sources,
reference validation, explicit exclusions, and reproducible gap reports.
It distinguishes whole-script parsing, statement parsing, generation failures,
and targeted AST mismatches rather than measuring only a supported subset.

## Inspiration

Cyqwel was inspired by [SQLGlot](https://github.com/tobymao/sqlglot).

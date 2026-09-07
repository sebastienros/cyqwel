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

## Install

```bash
dotnet add package Cyqwel
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

Oracle `SYSDATE` retains `IsSystemDate = true` and renders back to `SYSDATE` for
Oracle; other targets use their ordinary current timestamp. This abstraction
does not guarantee identical clock, timezone, precision, or return-type semantics
across databases. Oracle queries without `FROM` target Oracle 23+; generation
does not insert `FROM DUAL`.

Only unquoted, unqualified, no-argument forms without aggregate or window modifiers
are normalized. Precision-bearing calls remain ordinary function expressions and
retain their arguments; unsupported timestamp argument forms throw during
generation unless `UnsupportedBehavior` is `Ignore`. UTC, local-time, date-only,
and wall-clock functions are not included in this normalization.

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

## Inspiration

Cyqwel was inspired by [SQLGlot](https://github.com/tobymao/sqlglot).

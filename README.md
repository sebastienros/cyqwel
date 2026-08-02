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

Builders are available for `SELECT`, set operations, `INSERT`, `UPDATE`, `DELETE`, `CASE`, and expressions.

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

## Supported dialects

Cyqwel includes Generic SQL, T-SQL, SQLite, PostgreSQL, MySQL, and Oracle dialects. The shared syntax tree covers common relational queries, data modification, and schema statements while dialects handle source compatibility and target-specific SQL generation.

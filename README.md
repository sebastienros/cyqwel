# Cyqwel

Cyqwel is a dialect-neutral SQL toolkit for .NET. It parses SQL into an immutable C# AST, traverses or rewrites that tree, generates SQL for another dialect, and builds queries without concatenating SQL strings.

## Features

- Shared AST for Generic SQL, T-SQL, SQLite, PostgreSQL, MySQL, and Oracle
- Reusable, compiled [Parlot](https://github.com/sebastienros/parlot) parser graphs cached by dialect configuration
- Strict dialect parsing with distinct quote, parameter, operator, clause, and DML rules
- Read-only visitor, non-mutating rewriter, depth-first and breadth-first traversal
- Dialect-aware generation and transpilation
- Window functions with `OVER`, `PARTITION BY`, and window ordering
- Window frames, named window definitions, aggregate `FILTER`, and `WITHIN GROUP`
- `VALUES` queries, `MERGE`, extended DML, and core relational DDL
- PostgreSQL `EXPLAIN` options and SQLite date, JSON aggregate, and scalar-function rewrites
- Oracle bind variables, `MINUS`, sequences, hierarchical queries, row limiting, and native type forms
- Public dialect base class, registry, and fluent custom dialect builder
- Syntax, semantic, schema, type, and relationship-aware SQL validation
- Custom literal and argument-aware function rendering hooks
- Fluent SELECT, set-operation, INSERT, UPDATE, DELETE, CASE, and expression builders
- Input and AST complexity guards
- Pooled SQL generation buffers

## Parse, inspect, and generate

```csharp
using Cyqwel.Dialects;
using Cyqwel.Visitors;

var document = SqlDialects.TSql.Parse(
    "SELECT TOP 10 [display name] FROM [users]");

var columns = document.GetColumnNames();
var postgres = SqlDialects.PostgreSql.Generate(document);
// SELECT "display name" FROM "users" LIMIT 10
```

`SqlParser.TryParse` returns a structured `SqlParseError` when input is invalid. `SqlParseOptions` controls maximum input length and AST node count.

Dialects can opt into application parameter defaults such as `@pageSize:10` with
`SqlDialectParserOptions.SupportsParameterDefaults`. It is disabled by default. When
enabled, `ParameterExpression.DefaultValue` exposes the literal default without emitting
it as part of the generated database command.

Dialect entry points enforce source compatibility:

```csharp
SqlDialects.TSql.Parse("SELECT TOP 10 [id] FROM [users]"); // accepted
SqlDialects.PostgreSql.Parse("SELECT TOP 10 id FROM users"); // throws SqlParseException
```

An otherwise recognized construct that is incompatible with the selected dialect returns `SqlParseErrorCode.DialectIncompatible`. Dialect selection also resolves ambiguous syntax in the source AST; for example, `||` becomes concatenation in PostgreSQL and SQLite but logical `OR` in MySQL.

## Validate SQL

`SqlValidator` returns stable diagnostics with a severity, code, message, and source location when the parser or AST provides one. Warnings do not make `IsValid` false.

```csharp
using Cyqwel.Validation;

var syntax = SqlValidator.Validate(
    "SELECT * FROM users LIMIT 10",
    options: new SqlValidationOptions
    {
        StrictSyntax = true,
        Semantic = true,
    });

var catalog = new SqlSchemaCatalog(
    new SqlTableSchema(
        "users",
        [
            new("id", "integer", IsPrimaryKey: true),
            new("name", "varchar"),
            new("age", "integer"),
        ],
        PrimaryKey: ["id"]));

var schema = SqlValidator.Validate(
    "SELECT id FROM users WHERE age > 18",
    catalog,
    options: new SqlSchemaValidationOptions
    {
        CheckTypes = true,
        CheckReferences = true,
    });
```

Semantic validation can flag `SELECT *`, mixed aggregate projections without `GROUP BY`, `DISTINCT` ordering concerns, and non-deterministic `LIMIT`/`OFFSET`. Schema validation resolves tables, aliases, joins, derived tables, and CTE projections. Type checks cover comparisons, arithmetic, predicates, supported function signatures, DML assignments, and set operations. Reference checks validate foreign-key metadata, ambiguous references, cartesian joins, and joins that bypass declared relationships.

Schema, type, and reference findings are errors by default. Set `SqlSchemaValidationOptions.Strict` to `false` to report them as warnings. Diagnostic code constants are available from `SqlValidationCodes`, and schema type names can be normalized with `SqlTypeFamilies.Classify`.

## Build SQL

```csharp
using Cyqwel;
using Cyqwel.Dialects;

var query = Sql.Select("u.id", "u.name")
    .From("users", "u")
    .Where(Sql.Col("u.age").GreaterThan(Sql.Lit(18)))
    .OrderBy(Sql.Col("u.name"))
    .Limit(10)
    .Build();

var sql = query.ToSql(SqlDialects.PostgreSql);
```

Builders produce the same AST types as the parser and contain no dialect-specific logic.

## Traverse and transform

Derive from `SqlVisitor` for read-only analysis or `SqlRewriter` for bottom-up, non-mutating transformations. Unchanged subtrees retain their original object references.

```csharp
using Cyqwel.Ast;
using Cyqwel.Visitors;

sealed class ParameterRewriter : SqlRewriter
{
    protected override SqlNode VisitLiteral(LiteralExpression node) =>
        node.Value is string ? new ParameterExpression("value") : node;
}

var rewritten = query.Accept(new ParameterRewriter());
```

`DescendantsAndSelf`, `BreadthFirst`, `FindAll<T>`, `GetTableNames`, and `GetColumnNames` provide efficient tree inspection. Convenience transforms include `AddWhere`, `SetLimit`, `RenameTable`, and `RenameColumn`.

## Extend dialects

Subclass `SqlDialect` for full control, or compose a dialect:

```csharp
var warehouse = SqlDialectBuilder.Create("warehouse")
    .BasedOn(SqlDialects.PostgreSql)
    .WithFunctionNameTransform(name => name == "LEN" ? "LENGTH" : name)
    .ConfigureParser(options => options with
    {
        IdentifierQuotes = options.IdentifierQuotes | SqlIdentifierQuoteStyle.Backtick,
    })
    .Register();
```

Custom dialects inherit the base dialect's parser configuration and can modify only the required rules. Built-in aliases such as `mssql`, `sqlserver`, and `postgres` are available through `SqlDialectRegistry`.

## Supported SQL surface

Cyqwel handles:

- Queries: `SELECT`, `VALUES`, joins with `ON` or `USING`, natural joins, predicates, grouping, ordering, row limits, recursive/materialized CTEs, named windows, window frames, and set operations.
- Expressions: functions and aggregate modifiers, `CASE`, `CAST`/`TRY_CAST`, rows, intervals, extraction, collation, sequences, subqueries, parameters, boolean tests, and common unary and binary operators.
- DML: `INSERT VALUES`/`SELECT`, `UPDATE ... FROM`, `DELETE ... USING`, `MERGE`, `RETURNING`, and Oracle `RETURNING ... INTO`.
- DDL: create/alter/drop/truncate tables, columns and common constraints, views (including MySQL `SQL SECURITY`), indexes, and sequences.
- PostgreSQL: parenthesized `EXPLAIN` options with `ANALYZE` preservation and common SQLite/T-SQL function rewrites.
- Oracle: colon bind variables, `MINUS`, `FETCH FIRST`, `NEXTVAL`/`CURRVAL`, `START WITH`/`CONNECT BY`, `PRIOR`, `ORDER SIBLINGS BY`, native numeric/character/temporal/interval type forms, and common function normalization.

The built-in scope targets standard relational databases. Warehouse-only statements and engine-specific administration commands remain outside the shared AST unless they overlap common RDBMS SQL.

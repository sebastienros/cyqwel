# Dialects

Defines the SQL dialects: parser behavior, generation rules and the dialect registry.

## SqlDialect.cs

Defines the style enums, the `SqlDialect` base class, the built-in dialects, the registry and the builder for custom dialects.

### SqlLimitStyle (enum)

Pagination style used by a dialect during generation.

| Value | Rendering |
|---|---|
| `LimitOffset` | `LIMIT n OFFSET m` (default) |
| `Top` | `SELECT TOP n` (T-SQL) |
| `OffsetFetch` | `OFFSET m ROWS FETCH NEXT n ROWS ONLY` |
| `LimitOffsetComma` | `LIMIT m, n` (MySQL) |
| `FetchFirst` | `FETCH FIRST n ROWS ONLY` (Oracle) |

### SqlConcatenationStyle (enum)

How string concatenation is emitted in generated SQL.

- `DoublePipe`: `a || b`
- `Plus`: `a + b` (T-SQL)
- `Function`: `CONCAT(a, b)` (MySQL)

### SqlDialect (class)

Base class describing the behavior of a SQL dialect, for both **parsing** (normalization) and **generation**.

**Construction**: `SqlDialect(name, identifierOpenQuote = '"', identifierCloseQuote = '"', limitStyle = LimitOffset)`. The name is required.

**Identity properties**: `Name`, `IdentifierOpenQuote`, `IdentifierCloseQuote`, `LimitStyle`.

**Capabilities (virtual, overridable)**:
- `SupportsILike`, `SupportsReturning`, `SupportsReturningInto`, `SupportsExplain`
- `RequiresOrderByForOffset`, `SupportsTableAliasAs`, `SupportsParenthesizedSetOperands`
- `SupportsStoredProcedures`, `SupportsAnonymousProceduralBlocks`, `UsesSqlSecurityForViews`
- `ConcatenationStyle`, `ParserOptions` (`SqlDialectParserOptions`, `Permissive` by default)
- `TrueLiteral` / `FalseLiteral` (`TRUE`/`FALSE` by default)
- Routine-related internal members: `RoutineRenderer`, `RoutineGrammar`, and the derived properties `SupportsProcedureParameterDefaults`, `SupportsNamedProcedureArguments`, `SupportsTopLevelProceduralControlFlow`.

**Extension points**:
- `Preprocess(node)`: AST transformation before generation.
- `TransformNode(node)`: node-by-node transformation (applied through `DialectRewriter`).
- `GetFunctionName(name)`: function renaming.
- `GetSetOperator(op)`: `UNION`, `INTERSECT`, `EXCEPT` (Oracle uses `MINUS`).
- `RenderLiteral`, `RenderFunction`, `RenderParameter`: custom rendering; returning `null` lets the default generator handle the node.
- `RenderCurrentTimestamp`: renders a `CurrentTimestampExpression` according to its `CurrentTimestampKind` (`Default`, `SystemDate`, `Utc`).
- `ShouldQuoteIdentifier(identifier)`: true when the identifier is already quoted, empty, not "safe" (letters, digits, `_`, `$`) or a reserved keyword.

**Public methods**:
- `Parse(sql, options?)`: returns a `SqlDocument` through `SqlParser`.
- `TryParse(sql, out document, out error, options?)`: exception-free version.
- `Generate(node, options?)`: preprocesses, transforms, then generates SQL with `SqlGenerator`.
- `Transpile(sql, target, options?)`: parses with this dialect and generates for `target`.

**Protected helpers**: `FormatTimestampFunctionName` (casing according to `FunctionNameCase`) and `RenderCurrentTimestampFunction` (handles `NOW()`, `CURRENT_TIMESTAMP`, etc.; throws `NotSupportedException` when arguments are unsupported and `UnsupportedBehavior` is `Throw`).

**Example**:
```csharp
var sql = SqlDialects.TSql.Transpile("SELECT TOP 10 * FROM users", SqlDialects.PostgreSql);
```

### DialectRewriter (class, private nested in SqlDialect)

Derives from `SqlRewriter`. Walks the AST and applies `dialect.TransformNode` to each node after its children are visited. Used only by `SqlDialect.Generate`.

### SqlDialects (static class)

Entry point to the built-in dialect singletons: `Generic`, `TSql`, `Sqlite`, `PostgreSql`, `MySql`, `Oracle`, plus the `BuiltIn` list.

### GenericDialect (class, private nested in SqlDialects)

`generic` dialect. Stored procedures and anonymous blocks supported, ANSI routine rendering, `Atomic` routine grammar.

### TSqlDialect (class, private nested in SqlDialects)

`tsql` (SQL Server) dialect: `[...]` identifiers, `TOP` pagination, `+` concatenation, `1`/`0` booleans, no `RETURNING`, `ORDER BY` required with `OFFSET`. Maps `CLOCK_TIMESTAMP`→`SYSDATETIME`, `LN`→`LOG`, `CHR`→`CHAR`, `REPEAT`→`REPLICATE`. Renders the current timestamp as `GETDATE()` / `GETUTCDATE()`.

### SqliteDialect (class, private nested in SqlDialects)

`sqlite` dialect: parenthesized set operands unsupported, `1`/`0` booleans, `LIMIT m, n` accepted. Maps `LEAST`→`MIN`, `GREATEST`→`MAX`, `JSON_AGG`→`JSON_GROUP_ARRAY`, etc. Rewrites some date functions (`DATE_TRUNC`, `DATE_PART`) to `STRFTIME`.

### PostgreSqlDialect (class, private nested in SqlDialects)

`postgresql` dialect: `ILIKE`, `RETURNING`, `$1` parameters, `NULLS FIRST/LAST` ordering, `EXPLAIN` options, procedures and `DO` blocks (dollar-quoted grammar). UTC is rendered as `TIMEZONE('UTC', CURRENT_TIMESTAMP)`.

### MySqlDialect (class, private nested in SqlDialects)

`mysql` dialect: backtick identifiers, `LIMIT m, n`, `CONCAT` concatenation, `||` interpreted as logical OR, no `RETURNING`, `?` parameters, `SQL SECURITY` for views.

### OracleDialect (class, private nested in SqlDialects)

`oracle` dialect: `FETCH FIRST`, table aliases without `AS`, `:name` parameters, `EXCEPT`→`MINUS`. Maps `IFNULL`→`NVL`, `SUBSTRING`→`SUBSTR`, `RANDOM`→`DBMS_RANDOM.VALUE`, two-argument `COALESCE`→`NVL`. Renders `SYSDATE` and `SYS_EXTRACT_UTC(...)`. `TransformNode` converts `TRY_CAST` to `CAST` and `ILIKE` to `LOWER(a) LIKE LOWER(b)`.

### SqlDialectRegistry (static class)

Global thread-safe, case-insensitive dialect registry (`ConcurrentDictionary`). Pre-registers the built-in dialects and the aliases `mssql`, `sqlserver` (→ T-SQL) and `postgres` (→ PostgreSQL).

- `All`: distinct registered dialects.
- `Get(name)`: throws `KeyNotFoundException` if missing.
- `TryGet(name, out dialect)`.
- `Register(dialect)`: throws `InvalidOperationException` if the name already exists.
- `Unregister(name)`: refuses to remove a built-in dialect.

### SqlDialectBuilder (sealed class)

Fluent builder for creating a custom dialect from a base dialect (`Generic` by default).

- `Create(name)`: entry point.
- `BasedOn(dialect)`: base dialect.
- `WithPreprocessor`, `WithNodeTransform`, `WithFunctionNameTransform`, `WithLiteralRenderer`, `WithFunctionRenderer`: inject behavior through delegates.
- `ConfigureParser(configure)`: adjusts the `SqlDialectParserOptions`.
- The resulting dialect is produced by an internal `DelegatingDialect` that delegates to the base dialect except for the configured behaviors.

**Example**:
```csharp
var custom = SqlDialectBuilder.Create("custom")
	.BasedOn(SqlDialects.PostgreSql)
	.WithFunctionNameTransform(n => n.Equals("NOW", StringComparison.OrdinalIgnoreCase) ? "CURRENT_TIMESTAMP" : n)
	.Build();
SqlDialectRegistry.Register(custom);
```

### DelegatingDialect (class, private nested in SqlDialectBuilder)

Internal dialect created by the builder; copies the base dialect's properties and calls the configured delegates.

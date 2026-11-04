# Builders / Sql.cs

Entry point of the fluent API.

## Sql (static class)

Static factory used to build expressions, table sources, statements and builders without handling AST records directly. Every builder returned exposes `Build()` (the AST node) and `ToSql(dialect?, options?)`.

```csharp
var sql = Sql.Select("id", "name")
	.From("users", "u")
	.Where(Sql.Col("u.age").GreaterThan(Sql.Lit(18)))
	.OrderBy(Sql.Col("name"))
	.Limit(10)
	.ToSql(SqlDialects.PostgreSql);
```

**Expressions**
- `Col(name)`, `Star()`, `Star(qualifier)`, `Lit(value)`, `Param(name, prefix)`, `SystemVariable(name)`, `Local(name)`, `Default()`.
- `Func(name, args)`, `Count(expr, distinct)`, `CountStar()`, `JsonArrayAgg(...)`.
- `Cast`, `TryCast`, `Convert`, `Extract`, `Interval`, `CurrentTimestamp`.
- `Exists`, `Subquery`, `Row`, `QuantifiedComparison`, `Parenthesize`, `AnsiConcat`.
- `NextValue(sequence)`, `CurrentValue(sequence)`.
- `Case(operand?)` returns a `CaseBuilder`.
- `MaxLengthType(name)`: data type with `MAX` length.

**Ordering and windows**
- `Order(...)`: `OrderByItem` factory.
- `UnboundedPreceding`, `Preceding`, `CurrentRow`, `Following`, `UnboundedFollowing`, `Frame(...)`: window frame parts.

**Table sources and assignments**
- `Table(name, alias)`, `TableVariable(name, alias)`, `Derived(query, alias)`.
- `Assign(column, value)`, `SelectAssign(...)` (T-SQL `@v = expr`).

**Query and DML builders**
- `Select(columns | expressions)`, `SelectItems(items)`, `Values(row)`, `Explain(query, analyze)`.
- `InsertInto`, `Update`, `DeleteFrom`, `MergeInto`.

**DDL builders**
- `CreateTable`, `AlterTable`, `Drop`, `DropTable`, `Truncate`, `CreateView`, `CreateInlineFunction`, `CreateSchema`, `CreateIndex`, `CreateSequence`, `AlterSequence`.
- Column and constraint factories: `DefineColumn`, `PrimaryKey`, `Unique`, `ForeignKey`, `Check`.

**Procedural builders**
- `CreateProcedure`, `ReplaceProcedure`, `DropProcedure`, `CallProcedure`, `Block()`.
- Statement factories: `If`, `While`, `Break`, `Continue`, `Return`.

**Other**
- `Document(statements)`: builds a `SqlDocument`.
- `Coerce(object?)` (internal): converts a CLR value (or an existing `SqlExpression`) into a literal expression; used by all builders that accept `object?` values.

# Ast / Expressions.cs

Expression nodes and the data type model.

## SqlIdentifier
Identifier `Value` with `IsQuoted` and `IsOmitted` (empty part, e.g. `db..table`). `ToString()` returns the value.

## ColumnExpression
Possibly qualified column reference (`Parts`). Constructors accept `"a.b"` or `(qualifier, name)`.

## StarExpression
`*` or `qualifier.*`.

## LiteralExpression
Constant `Value` (string, number, bool, `null`...). `IsNational` marks `N'...'` literals.

## CurrentTimestampKind (enum)
`Default`, `SystemDate` (Oracle `SYSDATE`), `Utc`.

## CurrentTimestampExpression
Current timestamp of the target dialect, selected by `CurrentTimestampKind`.

## TrimDirection (enum)
`Leading`, `Trailing`, `Both`.

## TrimExpression
`TRIM(direction character FROM source)`.

## TypedLiteralExpression
Typed literal such as `DATE '2024-01-01'`.

## HexLiteralExpression
Hexadecimal literal (`X'...'` / `0x...`).

## ParameterExpression
Parameter or variable (`@p`, `:p`, `$1`, `?`) with `Name`, `Prefix`, optional `DefaultValue` and `IsSystemVariable` (`@@ROWCOUNT`).

## ParenthesizedExpression
Preserves explicit parentheses around an expression.

## UnaryOperator (enum) / UnaryExpression
`Plus`, `Minus`, `Not`, `BitwiseNot`, `Prior`, `ConnectByRoot`. `UnaryExpression(Operator, Operand)`.

## BinaryOperator (enum) / BinaryExpression
Operators: `Or`, `And`, comparisons, `Like`/`NotLike`/`ILike`/`NotILike`, arithmetic, `Concatenate`, bitwise, `AnsiConcatenate` (`||`). `BinaryExpression(Left, Operator, Right)`.

## SqlQuantifier (enum) / QuantifiedComparisonExpression
`Any`, `All`, `Some`. `expr <op> ANY|ALL|SOME (subquery)`; only comparison operators are valid.

## ConvertExpression
T-SQL `CONVERT` / `TRY_CONVERT` with optional `Style` and `IsTry`.

## JsonNullHandling (enum) / JsonArrayAggregateExpression
`NullOnNull`, `AbsentOnNull`. `JSON_ARRAYAGG`-style aggregate with optional `OrderBy`.

## BetweenExpression
`expr [NOT] BETWEEN lower AND upper`.

## InExpression
`expr [NOT] IN (values)` or `IN (subquery)`; extra constructor for the subquery form.

## IsNullExpression
`IS [NOT] NULL`.

## BooleanTestKind (enum) / BooleanTestExpression
`True`, `False`, `Unknown`. `IS [NOT] TRUE|FALSE|UNKNOWN`.

## DistinctFromExpression
`IS [NOT] DISTINCT FROM`.

## RowExpression
Row value constructor `(a, b, ...)`.

## DefaultExpression
The `DEFAULT` keyword used as a value.

## CollateExpression
`expr COLLATE collation`.

## ExtractExpression
`EXTRACT(field FROM expr)`.

## IntervalExpression
`INTERVAL value unit`.

## SequenceValueKind (enum) / SequenceValueExpression
`Next`, `Current`. `NEXT VALUE FOR seq` / `seq.NEXTVAL` equivalents.

## FunctionCallExpression
Function call: `Name`, `Arguments`, `IsDistinct`, `Filter` (`FILTER (WHERE ...)`), `WithinGroup`, `Qualifiers` (schema prefix). Fluent helpers: `Distinct()`, `FilterWhere()`, `WithinGroupBy()`, `Over(...)` (inline or named window).

```csharp
var count = new FunctionCallExpression("COUNT", new StarExpression()).Over(partitionBy: [new ColumnExpression("dept")]);
```

## WindowExpression
`expr OVER (PARTITION BY ... ORDER BY ... frame)` or `OVER window_name`.

## ExistsExpression
`[NOT] EXISTS (subquery)`.

## SubqueryExpression
Scalar subquery used as an expression.

## WhenClause / CaseExpression
`WHEN condition THEN result`; `CASE [operand] WHEN ... [ELSE ...] END`.

## CastExpression / TryCastExpression
`CAST(expr AS type)` and `TRY_CAST(expr AS type)`.

## SqlDataTypeLengthUnit (enum)
`Unspecified`, `Byte`, `Char`.

## SqlDataTypeTimeZone (enum)
`Unspecified`, `WithTimeZone`, `WithLocalTimeZone`.

## SqlDataType
Data type: `Name`, numeric `Arguments` (length/precision/scale), `LengthUnit`, `TimeZone`, interval end field/arguments, `IsMaxLength` (`MAX`). Convenience constructor `new SqlDataType("VARCHAR", 50)`.



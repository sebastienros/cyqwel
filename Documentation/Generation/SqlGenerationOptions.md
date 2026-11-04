# Generation / SqlGenerationOptions.cs

## UnsupportedSqlBehavior (enum)
What to do when a dialect cannot render a construct: `Throw` (default, raises `NotSupportedException`) or `Ignore` (the construct is omitted).

## FunctionNameCase (enum)
Casing of function names: `Preserve`, `Upper` (default), `Lower`.

## SqlGenerationOptions (sealed record)
Immutable generation settings (`SqlGenerationOptions.Default` is the shared default instance).

| Property | Default | Meaning |
|---|---|---|
| `PrettyPrint` | `false` | Multi-line, indented output |
| `IndentSize` | `2` | Spaces per indentation level |
| `UppercaseKeywords` | `true` | Keyword casing |
| `FunctionNameCase` | `Upper` | Function name casing |
| `UnsupportedBehavior` | `Throw` | See above |

```csharp
var options = new SqlGenerationOptions { PrettyPrint = true, UppercaseKeywords = false };
var sql = statement.ToSql(SqlDialects.PostgreSql, options);
```

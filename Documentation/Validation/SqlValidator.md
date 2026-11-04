# Validation / SqlValidator.cs

## SqlValidator (public static class)

Public entry point. `Validate(...)` overloads parse the SQL (or take a `SqlDocument`), optionally check it against a `SqlSchemaCatalog`, and return a `SqlValidationResult`. Syntax failures are reported as `E000` diagnostics instead of exceptions.

```csharp
var result = SqlValidator.Validate(sql, SqlDialects.PostgreSql, catalog, new SqlValidationOptions { CheckTypes = true });
foreach (var d in result.Diagnostics) Console.WriteLine($"{d.Code} {d.Message}");
```

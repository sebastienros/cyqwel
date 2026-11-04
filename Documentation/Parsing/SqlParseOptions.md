# Parsing / SqlParseOptions.cs

## SqlParseOptions (sealed record)
Safety limits for parsing. `Default` is the shared instance.
- `MaximumInputLength` (default 16 MiB): longer input yields `InputTooLarge`.
- `MaximumAstNodes` (default 1,000,000): larger trees yield `AstTooLarge`.

## SqlParseErrorCode (enum)
`Syntax`, `DialectIncompatible`, `InputTooLarge`, `AstTooLarge`.

## SqlParseError (sealed record)
`(Message, Offset, Line, Column, Code = Syntax)`: location and reason of a parse failure.

## SqlParseException (sealed class : Exception)
Thrown by `SqlParser.Parse`; exposes the `Error` (`SqlParseError`).

```csharp
if (!SqlParser.TryParse(sql, SqlDialects.TSql, out var doc, out var error))
	Console.WriteLine($"{error.Line}:{error.Column} {error.Message}");
```

# Validation / SqlValidationModels.cs

Public models of the validation API.

- **SqlValidationSeverity** (enum): diagnostic severity (error, warning).
- **SqlValidationLocation** (record): `(Span, Line, Column)`.
- **SqlValidationDiagnostic** (record): code, severity, message and location.
- **SqlValidationResult** (record): `Diagnostics` list plus convenience information on validity.
- **SqlValidationOptions** (record): `Default`; options such as `StrictSyntax`, `Semantic`, `Strict` (true), `CheckTypes`, `CheckReferences` and `ParseOptions`. A schema-aware variant **SqlSchemaValidationOptions** exists with the same shape.
- **SqlSchemaCatalog** (record): list of `SqlTableSchema`; also built from `params`.
- **SqlTableSchema**, **SqlColumnSchema**, **SqlColumnReference** `(Table, Column, Schema?)`, **SqlForeignKey**, **SqlTableReference**: describe tables, columns, keys.
- **SqlTypeFamily** (enum) and **SqlTypeFamilies.Classify(dataType)**: map a data type name to a family (numeric, text, date/time, ...), used for type checks.
- **SqlValidationCodes** (static class): diagnostic code constants. `E0xx/E2xx` are errors (syntax `E000`, unknown table `E200`, unknown column `E201`, unknown function `E202`, type errors `E210-E217`, references `E220-E223`, procedural `E230-E235`) and `Wxxx` are warnings (`W001` SELECT *, `W002` aggregate without GROUP BY, `W003` DISTINCT with ORDER BY, `W004` LIMIT without ORDER BY, implicit casts `W210-W216`, `W220` cartesian join).

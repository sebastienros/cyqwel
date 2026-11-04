# Parsing / TSqlScriptParsers.cs

Parlot parsers used for T-SQL scripts.

## TSqlBatchSeparatorParser (internal sealed class : Parser&lt;bool&gt;)
Recognizes the `GO` batch separator on its own line. `Parse(context, ref result)` returns true when a separator was consumed.

## TSqlDocumentParser (internal sealed class : Parser&lt;SqlDocument&gt;)
Built from a `Parser<SqlBatch>`; splits a script into batches separated by `GO` and returns a `SqlDocument` with `Batches` populated.

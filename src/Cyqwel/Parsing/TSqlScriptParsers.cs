using Cyqwel.Ast;
using Parlot;
using Parlot.Fluent;

namespace Cyqwel.Parsing;

internal sealed class TSqlBatchSeparatorParser : Parser<bool>
{
    public override bool Parse(ParseContext context, ref ParseResult<bool> result)
    {
        var cursor = context.Scanner.Cursor;
        var original = cursor.Position;
        context.SkipWhiteSpace();
        var start = cursor.Offset;
        var text = cursor.Buffer;
        if (start + 2 > text.Length
            || !text.AsSpan(start, 2).Equals("GO", StringComparison.OrdinalIgnoreCase))
        {
            cursor.ResetPosition(original);
            return false;
        }

        for (var i = start - 1; i >= 0 && text[i] is not ('\r' or '\n'); i--)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                cursor.ResetPosition(original);
                return false;
            }
        }

        cursor.Advance(2);
        var afterKeyword = cursor.Position;
        context.SkipWhiteSpace();
        if (!cursor.Eof && text.AsSpan(start + 2, cursor.Offset - start - 2).IndexOfAny('\r', '\n') < 0)
        {
            cursor.ResetPosition(original);
            return false;
        }

        cursor.ResetPosition(afterKeyword);
        result.Set(start, cursor.Offset, true);
        return true;
    }
}

internal sealed class TSqlDocumentParser(Parser<SqlBatch> batchParser) : Parser<SqlDocument>
{
    public override bool Parse(ParseContext context, ref ParseResult<SqlDocument> result)
    {
        var cursor = context.Scanner.Cursor;
        var start = cursor.Position;
        List<SqlBatch> batches = [];
        while (true)
        {
            var position = cursor.Offset;
            ParseResult<SqlBatch> batch = default;
            if (!batchParser.Parse(context, ref batch))
            {
                cursor.ResetPosition(start);
                return false;
            }
            if (batch.Value.Statements.Count > 0 || batch.Value.IsTerminated)
            {
                batches.Add(batch.Value);
            }
            if (!batch.Value.IsTerminated || cursor.Offset == position) break;
        }

        var document = new SqlDocument(batches.SelectMany(static batch => batch.Statements).ToArray())
        {
            Batches = batches,
        };
        result.Set(start.Offset, cursor.Offset, document);
        return true;
    }
}

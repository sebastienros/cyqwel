using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Parsing;
using Cyqwel.Visitors;

namespace Cyqwel.Validation;

public static class SqlValidator
{
    private static readonly HashSet<string> AggregateFunctions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "AVG", "COUNT", "MAX", "MIN", "SUM",
            "ARRAY_AGG", "JSON_AGG", "JSONB_AGG", "STRING_AGG",
            "BOOL_AND", "BOOL_OR", "EVERY",
        };

    public static SqlValidationResult Validate(
        string sql,
        SqlDialect? dialect = null,
        SqlValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sql);
        options ??= SqlValidationOptions.Default;

        var diagnostics = Parse(
            sql,
            dialect ?? SqlDialects.Generic,
            options.StrictSyntax,
            options.ParseOptions,
            out var document);
        if (document is not null && options.Semantic)
        {
            AddSemanticDiagnostics(document, sql, diagnostics);
        }

        return new SqlValidationResult(diagnostics);
    }

    public static SqlValidationResult Validate(
        string sql,
        SqlSchemaCatalog catalog,
        SqlDialect? dialect = null,
        SqlSchemaValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(catalog);
        options ??= SqlSchemaValidationOptions.Default;

        var diagnostics = Parse(
            sql,
            dialect ?? SqlDialects.Generic,
            options.StrictSyntax,
            options.ParseOptions,
            out var document);
        if (document is null) return new SqlValidationResult(diagnostics);

        if (options.Semantic) AddSemanticDiagnostics(document, sql, diagnostics);
        new SchemaValidationEngine(sql, catalog, options, diagnostics).Validate(document);
        return new SqlValidationResult(diagnostics);
    }

    internal static bool IsAggregateFunction(string name) => AggregateFunctions.Contains(name);

    private static List<SqlValidationDiagnostic> Parse(
        string sql,
        SqlDialect dialect,
        bool strictSyntax,
        SqlParseOptions parseOptions,
        out SqlDocument? document)
    {
        ArgumentNullException.ThrowIfNull(parseOptions);
        var diagnostics = new List<SqlValidationDiagnostic>();

        if (strictSyntax && TryFindTrailingComma(sql, out var offset))
        {
            diagnostics.Add(new SqlValidationDiagnostic(
                SqlValidationSeverity.Error,
                SqlValidationCodes.StrictSyntax,
                "Trailing commas are not allowed in strict syntax mode.",
                CreateLocation(sql, offset, 1)));
            document = null;
            return diagnostics;
        }

        var parseSql = strictSyntax ? sql : RemoveTrailingCommas(sql);
        if (!SqlParser.TryParse(parseSql, dialect, out document, out var error, parseOptions))
        {
            diagnostics.Add(new SqlValidationDiagnostic(
                SqlValidationSeverity.Error,
                SqlValidationCodes.SyntaxError,
                error!.Message,
                new SqlValidationLocation(
                    new SqlTextSpan(error.Offset, 0),
                    error.Line,
                    error.Column)));
        }

        return diagnostics;
    }

    private static void AddSemanticDiagnostics(
        SqlDocument document,
        string sql,
        List<SqlValidationDiagnostic> diagnostics)
    {
        foreach (var select in document.FindAll<SelectStatement>())
        {
            foreach (var projection in select.Projections)
            {
                if (projection.Expression is StarExpression)
                {
                    diagnostics.Add(Warning(
                        SqlValidationCodes.SelectStar,
                        "SELECT * can make queries fragile when schemas change.",
                        sql,
                        projection.Expression));
                }
            }

            if (select.GroupBy is not { Count: > 0 }
                && select.Projections.Any(static projection =>
                    ContainsAggregate(projection.Expression))
                && select.Projections.Any(static projection =>
                    ContainsUngroupedColumn(projection.Expression)))
            {
                diagnostics.Add(Warning(
                    SqlValidationCodes.AggregateWithoutGroupBy,
                    "Aggregate and non-aggregate projections are mixed without GROUP BY.",
                    sql,
                    select));
            }

            if (select.IsDistinct && select.OrderBy is { Count: > 0 })
            {
                var projected = select.Projections
                    .Select(static projection => ExpressionKey(projection.Expression))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var aliases = select.Projections
                    .Where(static projection => projection.Alias is not null)
                    .Select(static projection => projection.Alias!.Value)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var orderBy in select.OrderBy)
                {
                    var isOrdinal = orderBy.Expression is LiteralExpression
                    {
                        Value: sbyte or byte or short or ushort or int or uint or long or ulong,
                    };
                    var isAlias = orderBy.Expression is ColumnExpression { Parts.Count: 1 } column
                        && aliases.Contains(column.Parts[0].Value);
                    if (!isOrdinal
                        && !isAlias
                        && !projected.Contains(ExpressionKey(orderBy.Expression)))
                    {
                        diagnostics.Add(Warning(
                            SqlValidationCodes.DistinctOrderBy,
                            "ORDER BY expression is not present in the DISTINCT projection.",
                            sql,
                            orderBy.Expression));
                    }
                }
            }

            if ((select.Limit is not null || select.Offset is not null)
                && select.OrderBy is not { Count: > 0 })
            {
                diagnostics.Add(Warning(
                    SqlValidationCodes.LimitWithoutOrderBy,
                    "LIMIT or OFFSET without ORDER BY does not produce deterministic rows.",
                    sql,
                    select.Limit ?? select.Offset!));
            }
        }

        foreach (var values in document.FindAll<ValuesStatement>())
        {
            if ((values.Limit is not null || values.Offset is not null)
                && values.OrderBy is not { Count: > 0 })
            {
                diagnostics.Add(Warning(
                    SqlValidationCodes.LimitWithoutOrderBy,
                    "LIMIT or OFFSET without ORDER BY does not produce deterministic rows.",
                    sql,
                    values.Limit ?? values.Offset!));
            }
        }

        foreach (var set in document.FindAll<SetOperationStatement>())
        {
            if ((set.Limit is not null || set.Offset is not null)
                && set.OrderBy is not { Count: > 0 })
            {
                diagnostics.Add(Warning(
                    SqlValidationCodes.LimitWithoutOrderBy,
                    "LIMIT or OFFSET without ORDER BY does not produce deterministic rows.",
                    sql,
                    set.Limit ?? set.Offset!));
            }
        }
    }

    private static bool ContainsAggregate(SqlExpression expression) => expression switch
    {
        WindowExpression => false,
        SubqueryExpression or ExistsExpression => false,
        FunctionCallExpression function when IsAggregateFunction(function.Name.Value) => true,
        FunctionCallExpression function => function.Arguments.Any(ContainsAggregate),
        ParenthesizedExpression value => ContainsAggregate(value.Expression),
        UnaryExpression value => ContainsAggregate(value.Operand),
        BinaryExpression value => ContainsAggregate(value.Left) || ContainsAggregate(value.Right),
        BetweenExpression value => ContainsAggregate(value.Expression)
            || ContainsAggregate(value.Lower)
            || ContainsAggregate(value.Upper),
        InExpression value => ContainsAggregate(value.Expression)
            || value.Values.Any(ContainsAggregate),
        IsNullExpression value => ContainsAggregate(value.Expression),
        BooleanTestExpression value => ContainsAggregate(value.Expression),
        DistinctFromExpression value => ContainsAggregate(value.Left)
            || ContainsAggregate(value.Right),
        RowExpression value => value.Values.Any(ContainsAggregate),
        CollateExpression value => ContainsAggregate(value.Expression),
        ExtractExpression value => ContainsAggregate(value.Expression),
        IntervalExpression value => ContainsAggregate(value.Value),
        CaseExpression value => (value.Operand is not null && ContainsAggregate(value.Operand))
            || value.Whens.Any(static when =>
                ContainsAggregate(when.Condition) || ContainsAggregate(when.Result))
            || (value.Else is not null && ContainsAggregate(value.Else)),
        CastExpression value => ContainsAggregate(value.Expression),
        TryCastExpression value => ContainsAggregate(value.Expression),
        _ => false,
    };

    private static bool ContainsUngroupedColumn(SqlExpression expression) => expression switch
    {
        ColumnExpression => true,
        WindowExpression or SubqueryExpression or ExistsExpression => false,
        FunctionCallExpression function when IsAggregateFunction(function.Name.Value) => false,
        FunctionCallExpression function => function.Arguments.Any(ContainsUngroupedColumn),
        ParenthesizedExpression value => ContainsUngroupedColumn(value.Expression),
        UnaryExpression value => ContainsUngroupedColumn(value.Operand),
        BinaryExpression value => ContainsUngroupedColumn(value.Left)
            || ContainsUngroupedColumn(value.Right),
        BetweenExpression value => ContainsUngroupedColumn(value.Expression)
            || ContainsUngroupedColumn(value.Lower)
            || ContainsUngroupedColumn(value.Upper),
        InExpression value => ContainsUngroupedColumn(value.Expression)
            || value.Values.Any(ContainsUngroupedColumn),
        IsNullExpression value => ContainsUngroupedColumn(value.Expression),
        BooleanTestExpression value => ContainsUngroupedColumn(value.Expression),
        DistinctFromExpression value => ContainsUngroupedColumn(value.Left)
            || ContainsUngroupedColumn(value.Right),
        RowExpression value => value.Values.Any(ContainsUngroupedColumn),
        CollateExpression value => ContainsUngroupedColumn(value.Expression),
        ExtractExpression value => ContainsUngroupedColumn(value.Expression),
        IntervalExpression value => ContainsUngroupedColumn(value.Value),
        CaseExpression value => (value.Operand is not null && ContainsUngroupedColumn(value.Operand))
            || value.Whens.Any(static when =>
                ContainsUngroupedColumn(when.Condition)
                || ContainsUngroupedColumn(when.Result))
            || (value.Else is not null && ContainsUngroupedColumn(value.Else)),
        CastExpression value => ContainsUngroupedColumn(value.Expression),
        TryCastExpression value => ContainsUngroupedColumn(value.Expression),
        _ => false,
    };

    private static string ExpressionKey(SqlExpression expression) =>
        expression.ToSql(SqlDialects.Generic);

    private static SqlValidationDiagnostic Warning(
        string code,
        string message,
        string sql,
        SqlNode node) =>
        new(
            SqlValidationSeverity.Warning,
            code,
            message,
            CreateLocation(sql, node));

    internal static SqlValidationLocation? CreateLocation(string sql, SqlNode node) =>
        node.Span.IsEmpty ? null : CreateLocation(sql, node.Span.Start, node.Span.Length);

    internal static SqlValidationLocation CreateLocation(string sql, int offset, int length)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < offset && i < sql.Length; i++)
        {
            if (sql[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new SqlValidationLocation(new SqlTextSpan(offset, length), line, column);
    }

    private static bool TryFindTrailingComma(string sql, out int offset)
    {
        var offsets = FindTrailingCommaOffsets(sql);
        if (offsets.Count > 0)
        {
            offset = offsets[0];
            return true;
        }

        offset = -1;
        return false;
    }

    private static string RemoveTrailingCommas(string sql)
    {
        var offsets = FindTrailingCommaOffsets(sql);
        if (offsets.Count == 0) return sql;

        var characters = sql.ToCharArray();
        foreach (var offset in offsets) characters[offset] = ' ';
        return new string(characters);
    }

    private static IReadOnlyList<int> FindTrailingCommaOffsets(string sql)
    {
        var offsets = new List<int>();
        for (var i = 0; i < sql.Length; i++)
        {
            if (TrySkipQuotedOrComment(sql, ref i)) continue;
            if (sql[i] != ',') continue;

            var next = i + 1;
            SkipTrivia(sql, ref next);
            if (next >= sql.Length || sql[next] is ')' or ';')
            {
                offsets.Add(i);
                continue;
            }

            if (!IsIdentifierStart(sql[next])) continue;
            var end = next + 1;
            while (end < sql.Length && IsIdentifierPart(sql[end])) end++;
            var keyword = sql[next..end];
            if (keyword.Equals("FROM", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("WHERE", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("GROUP", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("HAVING", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("ORDER", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("LIMIT", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("OFFSET", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("FETCH", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("RETURNING", StringComparison.OrdinalIgnoreCase))
            {
                offsets.Add(i);
            }
        }

        return offsets;
    }

    private static void SkipTrivia(string sql, ref int index)
    {
        while (index < sql.Length)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                index++;
                continue;
            }

            if (index + 1 < sql.Length && sql[index] == '-' && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] != '\n') index++;
                continue;
            }

            if (index + 1 < sql.Length && sql[index] == '/' && sql[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < sql.Length
                    && !(sql[index] == '*' && sql[index + 1] == '/'))
                {
                    index++;
                }

                index = Math.Min(index + 2, sql.Length);
                continue;
            }

            break;
        }
    }

    private static bool TrySkipQuotedOrComment(string sql, ref int index)
    {
        var current = sql[index];
        if (current is '\'' or '"' or '`')
        {
            var quote = current;
            while (++index < sql.Length)
            {
                if (sql[index] != quote) continue;
                if (index + 1 < sql.Length && sql[index + 1] == quote)
                {
                    index++;
                    continue;
                }

                return true;
            }

            return true;
        }

        if (current == '[')
        {
            while (++index < sql.Length)
            {
                if (sql[index] != ']') continue;
                if (index + 1 < sql.Length && sql[index + 1] == ']')
                {
                    index++;
                    continue;
                }

                return true;
            }

            return true;
        }

        if (index + 1 < sql.Length && current == '-' && sql[index + 1] == '-')
        {
            index += 2;
            while (index < sql.Length && sql[index] != '\n') index++;
            return true;
        }

        if (index + 1 < sql.Length && current == '/' && sql[index + 1] == '*')
        {
            index += 2;
            while (index + 1 < sql.Length
                && !(sql[index] == '*' && sql[index + 1] == '/'))
            {
                index++;
            }

            index = Math.Min(index + 1, sql.Length - 1);
            return true;
        }

        return false;
    }

    private static bool IsIdentifierStart(char value) => value == '_' || char.IsLetter(value);

    private static bool IsIdentifierPart(char value) =>
        value is '_' or '$' || char.IsLetterOrDigit(value);
}

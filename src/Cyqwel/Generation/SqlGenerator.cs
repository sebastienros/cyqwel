using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Cyqwel.Ast;
using Cyqwel.Dialects;

namespace Cyqwel.Generation;

public sealed class SqlGenerator
{
    private readonly SqlDialect _dialect;
    private readonly SqlGenerationOptions _options;
    private StringBuilder _builder = null!;
    private int _indent;

    public SqlGenerator(SqlDialect dialect, SqlGenerationOptions? options = null)
    {
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        _options = options ?? SqlGenerationOptions.Default;
    }

    public string Generate(SqlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _builder = StringBuilderPool.Rent();
        try
        {
            WriteNode(node);
            return _builder.ToString();
        }
        finally
        {
            StringBuilderPool.Return(_builder);
            _builder = null!;
            _indent = 0;
        }
    }

    private void WriteNode(SqlNode node)
    {
        switch (node)
        {
            case SqlDocument value: WriteDocument(value); break;
            case SelectStatement value: WriteSelect(value); break;
            case SetOperationStatement value: WriteSetOperation(value); break;
            case InsertStatement value: WriteInsert(value); break;
            case UpdateStatement value: WriteUpdate(value); break;
            case DeleteStatement value: WriteDelete(value); break;
            case SqlExpression value: WriteExpression(value); break;
            default: throw new NotSupportedException($"SQL generation does not support '{node.GetType().Name}' as a root node.");
        }
    }

    private void WriteDocument(SqlDocument document)
    {
        for (var i = 0; i < document.Statements.Count; i++)
        {
            if (i > 0)
            {
                _builder.Append(';');
                NewLine();
            }

            WriteNode(document.Statements[i]);
        }
    }

    private void WriteSelect(SelectStatement select)
    {
        WriteCommonTableExpressions(select.CommonTableExpressions);
        Keyword("SELECT");

        if (select.IsDistinct)
        {
            Space();
            Keyword("DISTINCT");
        }

        var semanticLimit = select.Limit ?? select.Top;
        var limitAsTop = _dialect.LimitStyle == SqlLimitStyle.Top
            && semanticLimit is not null
            && select.Offset is null;
        var top = limitAsTop ? semanticLimit : null;

        if ((select.IsTopPercent || select.WithTies) && !limitAsTop)
        {
            Unsupported($"{_dialect.Name} cannot represent TOP PERCENT or WITH TIES.");
        }

        if (top is not null)
        {
            Space();
            Keyword("TOP");
            _builder.Append(" (");
            WriteExpression(top);
            _builder.Append(')');
            if (select.IsTopPercent)
            {
                Space();
                Keyword("PERCENT");
            }

            if (select.WithTies)
            {
                Space();
                Keyword("WITH TIES");
            }
        }

        SpaceOrNewLine();
        WriteSeparated(select.Projections, WriteSelectItem);

        if (select.From is not null)
        {
            ClauseBreak();
            Keyword("FROM");
            Space();
            WriteTableSource(select.From);
        }

        if (select.Where is not null)
        {
            ClauseBreak();
            Keyword("WHERE");
            Space();
            WriteExpression(select.Where);
        }

        if (select.GroupBy is { Count: > 0 })
        {
            ClauseBreak();
            Keyword("GROUP BY");
            Space();
            WriteSeparated(select.GroupBy, expression => WriteExpression(expression));
        }

        if (select.Having is not null)
        {
            ClauseBreak();
            Keyword("HAVING");
            Space();
            WriteExpression(select.Having);
        }

        WriteOrderBy(select.OrderBy);
        if (!limitAsTop) WriteLimitOffset(semanticLimit, select.Offset, select.OrderBy);
    }

    private void WriteSetOperation(SetOperationStatement set)
    {
        WriteQueryOperand(set.Left);
        ClauseBreak();
        Keyword(set.Operator switch
        {
            SetOperator.Union => "UNION",
            SetOperator.Intersect => "INTERSECT",
            SetOperator.Except => "EXCEPT",
            _ => throw new ArgumentOutOfRangeException(nameof(set.Operator)),
        });

        if (set.IsAll)
        {
            Space();
            Keyword("ALL");
        }

        ClauseBreak();
        WriteQueryOperand(set.Right);
        WriteOrderBy(set.OrderBy);
        WriteLimitOffset(set.Limit, set.Offset, set.OrderBy);
    }

    private void WriteQueryOperand(SqlQuery query)
    {
        if (query is SetOperationStatement)
        {
            _builder.Append('(');
            WriteNode(query);
            _builder.Append(')');
        }
        else
        {
            WriteNode(query);
        }
    }

    private void WriteInsert(InsertStatement insert)
    {
        Keyword("INSERT INTO");
        Space();
        WriteTableName(insert.Target);

        if (insert.Columns is { Count: > 0 })
        {
            _builder.Append(" (");
            WriteSeparated(insert.Columns, WriteIdentifier);
            _builder.Append(')');
        }

        if (insert.Values is { Count: > 0 })
        {
            ClauseBreak();
            Keyword("VALUES");
            Space();
            WriteSeparated(insert.Values, row =>
            {
                _builder.Append('(');
                WriteSeparated(row, expression => WriteExpression(expression));
                _builder.Append(')');
            });
        }
        else if (insert.Source is not null)
        {
            ClauseBreak();
            WriteNode(insert.Source);
        }
        else
        {
            throw new InvalidOperationException("An INSERT statement requires VALUES or a source query.");
        }

        WriteReturning(insert.Returning);
    }

    private void WriteUpdate(UpdateStatement update)
    {
        Keyword("UPDATE");
        Space();
        WriteNamedTable(update.Target);
        ClauseBreak();
        Keyword("SET");
        Space();
        WriteSeparated(update.Assignments, assignment =>
        {
            WriteExpression(assignment.Column);
            _builder.Append(" = ");
            WriteExpression(assignment.Value);
        });

        if (update.Where is not null)
        {
            ClauseBreak();
            Keyword("WHERE");
            Space();
            WriteExpression(update.Where);
        }

        WriteReturning(update.Returning);
    }

    private void WriteDelete(DeleteStatement delete)
    {
        Keyword("DELETE FROM");
        Space();
        WriteNamedTable(delete.Target);

        if (delete.Where is not null)
        {
            ClauseBreak();
            Keyword("WHERE");
            Space();
            WriteExpression(delete.Where);
        }

        WriteReturning(delete.Returning);
    }

    private void WriteCommonTableExpressions(IReadOnlyList<CommonTableExpression>? expressions)
    {
        if (expressions is not { Count: > 0 }) return;

        Keyword("WITH");
        Space();
        WriteSeparated(expressions, expression =>
        {
            WriteIdentifier(expression.Name);
            if (expression.Columns is { Count: > 0 })
            {
                _builder.Append(" (");
                WriteSeparated(expression.Columns, WriteIdentifier);
                _builder.Append(')');
            }

            Space();
            Keyword("AS");
            _builder.Append(" (");
            WriteNode(expression.Query);
            _builder.Append(')');
        });
        ClauseBreak();
    }

    private void WriteOrderBy(IReadOnlyList<OrderByItem>? orderBy)
    {
        if (orderBy is not { Count: > 0 }) return;

        ClauseBreak();
        Keyword("ORDER BY");
        Space();
        WriteSeparated(orderBy, item =>
        {
            WriteExpression(item.Expression);
            Space();
            Keyword(item.Direction == OrderDirection.Descending ? "DESC" : "ASC");

            if (item.NullOrder != NullOrder.Unspecified)
            {
                Space();
                Keyword(item.NullOrder == NullOrder.First ? "NULLS FIRST" : "NULLS LAST");
            }
        });
    }

    private void WriteLimitOffset(
        SqlExpression? limit,
        SqlExpression? offset,
        IReadOnlyList<OrderByItem>? orderBy)
    {
        if (limit is null && offset is null) return;

        if (_dialect.RequiresOrderByForOffset && offset is not null && orderBy is not { Count: > 0 })
        {
            Unsupported($"{_dialect.Name} requires ORDER BY when OFFSET is used.");
        }

        switch (_dialect.LimitStyle)
        {
            case SqlLimitStyle.LimitOffset:
                if (limit is not null)
                {
                    ClauseBreak();
                    Keyword("LIMIT");
                    Space();
                    WriteExpression(limit);
                }

                if (offset is not null)
                {
                    ClauseBreak();
                    Keyword("OFFSET");
                    Space();
                    WriteExpression(offset);
                }

                break;

            case SqlLimitStyle.LimitOffsetComma:
                ClauseBreak();
                Keyword("LIMIT");
                Space();
                if (offset is not null)
                {
                    WriteExpression(offset);
                    _builder.Append(", ");
                }

                WriteExpression(limit ?? new LiteralExpression(long.MaxValue));
                break;

            case SqlLimitStyle.Top:
            case SqlLimitStyle.OffsetFetch:
                ClauseBreak();
                Keyword("OFFSET");
                Space();
                WriteExpression(offset ?? new LiteralExpression(0));
                Space();
                Keyword("ROWS");
                if (limit is not null)
                {
                    Space();
                    Keyword("FETCH NEXT");
                    Space();
                    WriteExpression(limit);
                    Space();
                    Keyword("ROWS ONLY");
                }

                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void WriteReturning(IReadOnlyList<SqlExpression>? returning)
    {
        if (returning is not { Count: > 0 }) return;
        if (!_dialect.SupportsReturning)
        {
            Unsupported($"{_dialect.Name} does not support RETURNING.");
            if (_options.UnsupportedBehavior == UnsupportedSqlBehavior.Ignore) return;
        }

        ClauseBreak();
        Keyword("RETURNING");
        Space();
        WriteSeparated(returning, expression => WriteExpression(expression));
    }

    private void WriteSelectItem(SelectItem item)
    {
        WriteExpression(item.Expression);
        if (item.Alias is null) return;
        Space();
        Keyword("AS");
        Space();
        WriteIdentifier(item.Alias);
    }

    private void WriteTableSource(TableSource table)
    {
        switch (table)
        {
            case NamedTable named:
                WriteNamedTable(named);
                break;
            case DerivedTable derived:
                _builder.Append('(');
                WriteNode(derived.Query);
                _builder.Append(") ");
                Keyword("AS");
                Space();
                WriteIdentifier(derived.Alias);
                break;
            case JoinTable join:
                WriteTableSource(join.Left);
                SpaceOrNewLine();
                Keyword(join.Kind switch
                {
                    JoinKind.Inner => "INNER JOIN",
                    JoinKind.Left => "LEFT JOIN",
                    JoinKind.Right => "RIGHT JOIN",
                    JoinKind.Full => "FULL JOIN",
                    JoinKind.Cross => "CROSS JOIN",
                    _ => throw new ArgumentOutOfRangeException(),
                });
                Space();
                WriteTableSource(join.Right);
                if (join.Condition is not null)
                {
                    Space();
                    Keyword("ON");
                    Space();
                    WriteExpression(join.Condition);
                }

                break;
            default:
                throw new NotSupportedException($"Unsupported table source '{table.GetType().Name}'.");
        }
    }

    private void WriteNamedTable(NamedTable table)
    {
        WriteTableName(table.Name);
        if (table.Alias is null) return;
        Space();
        Keyword("AS");
        Space();
        WriteIdentifier(table.Alias);
    }

    private void WriteTableName(TableName table) => WriteSeparated(table.Parts, WriteIdentifier, ".");

    private void WriteExpression(SqlExpression expression, int parentPrecedence = 0)
    {
        var precedence = GetPrecedence(expression);
        var parenthesize = precedence < parentPrecedence;
        if (parenthesize) _builder.Append('(');

        switch (expression)
        {
            case ColumnExpression column:
                WriteSeparated(column.Parts, WriteIdentifier, ".");
                break;
            case StarExpression star:
                if (star.Qualifier is { Count: > 0 })
                {
                    WriteSeparated(star.Qualifier, WriteIdentifier, ".");
                    _builder.Append('.');
                }

                _builder.Append('*');
                break;
            case LiteralExpression literal:
                WriteLiteral(literal.Value);
                break;
            case ParameterExpression parameter:
                WriteParameter(parameter);
                break;
            case UnaryExpression unary:
                WriteUnary(unary);
                break;
            case BinaryExpression binary:
                WriteBinary(binary, precedence);
                break;
            case BetweenExpression between:
                WriteExpression(between.Expression, precedence);
                Space();
                if (between.IsNegated) Keyword("NOT ");
                Keyword("BETWEEN");
                Space();
                WriteExpression(between.Lower, precedence + 1);
                Space();
                Keyword("AND");
                Space();
                WriteExpression(between.Upper, precedence + 1);
                break;
            case InExpression @in:
                WriteExpression(@in.Expression, precedence);
                Space();
                if (@in.IsNegated) Keyword("NOT ");
                Keyword("IN");
                _builder.Append(" (");
                if (@in.Query is not null) WriteNode(@in.Query);
                else WriteSeparated(@in.Values, expression => WriteExpression(expression));
                _builder.Append(')');
                break;
            case IsNullExpression isNull:
                WriteExpression(isNull.Expression, precedence);
                Space();
                Keyword(isNull.IsNegated ? "IS NOT NULL" : "IS NULL");
                break;
            case FunctionCallExpression function:
                WriteFunction(function);
                break;
            case ExistsExpression exists:
                if (exists.IsNegated) Keyword("NOT ");
                Keyword("EXISTS");
                _builder.Append(" (");
                WriteNode(exists.Query);
                _builder.Append(')');
                break;
            case SubqueryExpression subquery:
                _builder.Append('(');
                WriteNode(subquery.Query);
                _builder.Append(')');
                break;
            case CaseExpression @case:
                WriteCase(@case);
                break;
            case CastExpression cast:
                Keyword("CAST");
                _builder.Append('(');
                WriteExpression(cast.Expression);
                Space();
                Keyword("AS");
                Space();
                WriteDataType(cast.DataType);
                _builder.Append(')');
                break;
            default:
                throw new NotSupportedException($"Unsupported SQL expression '{expression.GetType().Name}'.");
        }

        if (parenthesize) _builder.Append(')');
    }

    private void WriteUnary(UnaryExpression unary)
    {
        _builder.Append(unary.Operator switch
        {
            UnaryOperator.Plus => "+",
            UnaryOperator.Minus => "-",
            UnaryOperator.BitwiseNot => "~",
            UnaryOperator.Not => KeywordText("NOT") + " ",
            _ => throw new ArgumentOutOfRangeException(),
        });
        WriteExpression(unary.Operand, GetPrecedence(unary));
    }

    private void WriteBinary(BinaryExpression binary, int precedence)
    {
        if (binary.Operator == BinaryOperator.Concatenate
            && _dialect.ConcatenationStyle == SqlConcatenationStyle.Function)
        {
            Keyword("CONCAT");
            _builder.Append('(');
            WriteExpression(binary.Left);
            _builder.Append(", ");
            WriteExpression(binary.Right);
            _builder.Append(')');
            return;
        }

        WriteExpression(binary.Left, precedence);
        Space();
        var sqlOperator = GetBinaryOperator(binary.Operator);
        if ((binary.Operator is BinaryOperator.ILike or BinaryOperator.NotILike) && !_dialect.SupportsILike)
        {
            Unsupported($"{_dialect.Name} does not support ILIKE.");
        }

        _builder.Append(sqlOperator);
        Space();
        WriteExpression(binary.Right, precedence + 1);
    }

    private string GetBinaryOperator(BinaryOperator value) => value switch
    {
        BinaryOperator.Or => KeywordText("OR"),
        BinaryOperator.And => KeywordText("AND"),
        BinaryOperator.Equal => "=",
        BinaryOperator.NotEqual => "<>",
        BinaryOperator.GreaterThan => ">",
        BinaryOperator.GreaterThanOrEqual => ">=",
        BinaryOperator.LessThan => "<",
        BinaryOperator.LessThanOrEqual => "<=",
        BinaryOperator.Like => KeywordText("LIKE"),
        BinaryOperator.NotLike => KeywordText("NOT LIKE"),
        BinaryOperator.ILike => KeywordText("ILIKE"),
        BinaryOperator.NotILike => KeywordText("NOT ILIKE"),
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Concatenate => _dialect.ConcatenationStyle switch
        {
            SqlConcatenationStyle.DoublePipe => "||",
            SqlConcatenationStyle.Plus => "+",
            SqlConcatenationStyle.Function => throw new InvalidOperationException("Function concatenation is emitted separately."),
            _ => throw new ArgumentOutOfRangeException(),
        },
        BinaryOperator.BitwiseAnd => "&",
        BinaryOperator.BitwiseOr => "|",
        BinaryOperator.BitwiseXor => "^",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private void WriteFunction(FunctionCallExpression function)
    {
        var name = _dialect.GetFunctionName(function.Name.Value);
        name = _options.FunctionNameCase switch
        {
            FunctionNameCase.Upper => name.ToUpperInvariant(),
            FunctionNameCase.Lower => name.ToLowerInvariant(),
            _ => name,
        };
        _builder.Append(name).Append('(');
        if (function.IsDistinct)
        {
            Keyword("DISTINCT");
            if (function.Arguments.Count > 0) Space();
        }

        WriteSeparated(function.Arguments, expression => WriteExpression(expression));
        _builder.Append(')');
    }

    private void WriteCase(CaseExpression @case)
    {
        Keyword("CASE");
        if (@case.Operand is not null)
        {
            Space();
            WriteExpression(@case.Operand);
        }

        foreach (var when in @case.Whens)
        {
            Space();
            Keyword("WHEN");
            Space();
            WriteExpression(when.Condition);
            Space();
            Keyword("THEN");
            Space();
            WriteExpression(when.Result);
        }

        if (@case.Else is not null)
        {
            Space();
            Keyword("ELSE");
            Space();
            WriteExpression(@case.Else);
        }

        Space();
        Keyword("END");
    }

    private void WriteDataType(SqlDataType dataType)
    {
        _builder.Append(dataType.Name.Value);
        if (dataType.Arguments is not { Count: > 0 }) return;
        _builder.Append('(');
        WriteSeparated(dataType.Arguments, value => _builder.Append(value.ToString(CultureInfo.InvariantCulture)));
        _builder.Append(')');
    }

    private void WriteIdentifier(SqlIdentifier identifier)
    {
        if (!_dialect.ShouldQuoteIdentifier(identifier))
        {
            _builder.Append(identifier.Value);
            return;
        }

        _builder.Append(_dialect.IdentifierOpenQuote);
        foreach (var character in identifier.Value)
        {
            if (character == _dialect.IdentifierCloseQuote) _builder.Append(character);
            _builder.Append(character);
        }

        _builder.Append(_dialect.IdentifierCloseQuote);
    }

    private void WriteParameter(ParameterExpression parameter)
    {
        if (parameter.Prefix == '?' && parameter.Name.Length == 0)
        {
            _builder.Append('?');
            return;
        }

        if (parameter.Prefix == '$'
            && parameter.Name.Length > 0
            && parameter.Name.All(char.IsDigit))
        {
            _builder.Append('$').Append(parameter.Name);
            return;
        }

        if (parameter.Name.Length == 0
            || !(parameter.Name[0] == '_' || char.IsLetter(parameter.Name[0])))
        {
            throw new InvalidOperationException($"Parameter name '{parameter.Name}' is invalid.");
        }

        for (var i = 1; i < parameter.Name.Length; i++)
        {
            if (parameter.Name[i] != '_' && !char.IsLetterOrDigit(parameter.Name[i]))
            {
                throw new InvalidOperationException($"Parameter name '{parameter.Name}' is invalid.");
            }
        }

        _builder.Append(parameter.Prefix).Append(parameter.Name);
    }

    private void WriteLiteral(object? value)
    {
        switch (value)
        {
            case null:
                Keyword("NULL");
                break;
            case bool boolean:
                _builder.Append(boolean ? _dialect.TrueLiteral : _dialect.FalseLiteral);
                break;
            case string text:
                _builder.Append('\'');
                foreach (var character in text)
                {
                    if (character == '\'') _builder.Append('\'');
                    _builder.Append(character);
                }

                _builder.Append('\'');
                break;
            case char character:
                _builder.Append('\'');
                if (character == '\'') _builder.Append('\'');
                _builder.Append(character).Append('\'');
                break;
            case DateTime dateTime:
                _builder.Append('\'').Append(dateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture)).Append('\'');
                break;
            case DateTimeOffset dateTimeOffset:
                _builder.Append('\'').Append(dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture)).Append('\'');
                break;
            case IFormattable formattable:
                _builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                throw new NotSupportedException($"Literal type '{value.GetType().Name}' is not supported.");
        }
    }

    private static int GetPrecedence(SqlExpression expression) => expression switch
    {
        BinaryExpression { Operator: BinaryOperator.Or } => 1,
        BinaryExpression { Operator: BinaryOperator.And } => 2,
        BetweenExpression or InExpression or IsNullExpression => 3,
        BinaryExpression { Operator: >= BinaryOperator.Equal and <= BinaryOperator.NotILike } => 3,
        BinaryExpression { Operator: BinaryOperator.BitwiseOr or BinaryOperator.BitwiseXor } => 4,
        BinaryExpression { Operator: BinaryOperator.BitwiseAnd } => 5,
        BinaryExpression { Operator: BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Concatenate } => 6,
        BinaryExpression { Operator: BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo } => 7,
        UnaryExpression => 8,
        _ => 9,
    };

    private void Unsupported(string message)
    {
        if (_options.UnsupportedBehavior == UnsupportedSqlBehavior.Throw)
        {
            throw new NotSupportedException(message);
        }
    }

    private void Keyword(string value) => _builder.Append(KeywordText(value));

    private string KeywordText(string value) => _options.UppercaseKeywords ? value : value.ToLowerInvariant();

    private void Space() => _builder.Append(' ');

    private void ClauseBreak()
    {
        if (_options.PrettyPrint) NewLine();
        else Space();
    }

    private void SpaceOrNewLine()
    {
        if (_options.PrettyPrint)
        {
            _indent++;
            NewLine();
            _indent--;
        }
        else
        {
            Space();
        }
    }

    private void NewLine()
    {
        _builder.AppendLine();
        _builder.Append(' ', _indent * _options.IndentSize);
    }

    private void WriteSeparated<T>(IReadOnlyList<T> values, Action<T> write, string separator = ", ")
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0) _builder.Append(separator);
            write(values[i]);
        }
    }

    private static class StringBuilderPool
    {
        private const int MaximumRetainedCapacity = 64 * 1024;
        private static readonly ConcurrentBag<StringBuilder> Pool = [];

        public static StringBuilder Rent() =>
            Pool.TryTake(out var builder) ? builder : new StringBuilder(256);

        public static void Return(StringBuilder builder)
        {
            if (builder.Capacity > MaximumRetainedCapacity) return;
            builder.Clear();
            Pool.Add(builder);
        }
    }
}

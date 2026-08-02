using System.Globalization;
using Cyqwel.Ast;

namespace Cyqwel;

/// <summary>
/// Creates dialect-neutral SQL expressions and fluent statement builders.
/// </summary>
public static class Sql
{
    public static ColumnExpression Col(string name) => new(name);

    public static StarExpression Star() => new();

    public static StarExpression Star(string qualifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifier);
        if (qualifier.Contains('.'))
        {
            throw new ArgumentException(
                "Qualified stars support one identifier part.",
                nameof(qualifier));
        }

        return new([new SqlIdentifier(qualifier)]);
    }

    public static LiteralExpression Lit(object? value)
    {
        if (!IsSupportedLiteralValue(value))
        {
            throw new ArgumentException(
                $"Literal type '{value!.GetType().Name}' is not supported by the SQL parser.",
                nameof(value));
        }

        if (IsUnsupportedFloatingPoint(value))
        {
            throw new ArgumentException(
                "Floating-point literals must be finite and use non-exponential notation.",
                nameof(value));
        }

        return new LiteralExpression(value);
    }

    public static ParameterExpression Param(string name, char prefix = '@') => new(name, prefix);

    public static SqlDocument Document(params SqlStatement[] statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        if (statements.Length == 0)
        {
            throw new ArgumentException("At least one statement is required.", nameof(statements));
        }

        return new SqlDocument(statements);
    }

    public static ParenthesizedExpression Parenthesize(SqlExpression expression) => new(expression);

    public static FunctionCallExpression Func(string name, params SqlExpression[] arguments) =>
        new(name, arguments);

    public static FunctionCallExpression Count(SqlExpression expression, bool distinct = false) =>
        new(new SqlIdentifier("COUNT"), [expression], distinct);

    public static FunctionCallExpression CountStar() => Count(Star());

    public static CastExpression Cast(SqlExpression expression, string dataType, params int[] arguments) =>
        new(expression, new SqlDataType(dataType, arguments));

    public static TryCastExpression TryCast(SqlExpression expression, string dataType, params int[] arguments) =>
        new(expression, new SqlDataType(dataType, arguments));

    public static ExistsExpression Exists(SqlQuery query, bool negated = false) => new(query, negated);

    public static SubqueryExpression Subquery(SqlQuery query) => new(query);

    public static RowExpression Row(params SqlExpression[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("At least one row value is required.", nameof(values));
        }

        return new RowExpression(values);
    }

    public static DefaultExpression Default() => new();

    public static ExtractExpression Extract(string field, SqlExpression expression) =>
        new(new SqlIdentifier(field), expression);

    public static IntervalExpression Interval(object value, string unit)
    {
        var expression = Coerce(value);
        if (expression is LiteralExpression literal && IsUnsupportedFloatingPoint(literal.Value))
        {
            throw new ArgumentException(
                "Floating-point interval values must be finite and use non-exponential notation.",
                nameof(value));
        }

        if (expression is not ParameterExpression
            && expression is not LiteralExpression { Value: string or char or DateTime or DateTimeOffset }
            && expression is not LiteralExpression { Value: byte or sbyte or short or ushort or int or uint
                or long or ulong or float or double or decimal })
        {
            throw new ArgumentException(
                "Interval values must be numeric, textual, temporal, or parameter expressions.",
                nameof(value));
        }

        return new IntervalExpression(expression, new SqlIdentifier(unit));
    }

    public static SequenceValueExpression NextValue(string sequence) =>
        new(new TableName(sequence), SequenceValueKind.Next);

    public static SequenceValueExpression CurrentValue(string sequence) =>
        new(new TableName(sequence), SequenceValueKind.Current);

    public static OrderByItem Order(
        SqlExpression expression,
        OrderDirection direction = OrderDirection.Ascending,
        NullOrder nullOrder = NullOrder.Unspecified) =>
        new(expression, direction, nullOrder);

    public static WindowFrameBound UnboundedPreceding() =>
        new(WindowFrameBoundKind.UnboundedPreceding);

    public static WindowFrameBound Preceding(object offset) =>
        new(WindowFrameBoundKind.Preceding, Coerce(offset));

    public static WindowFrameBound CurrentRow() =>
        new(WindowFrameBoundKind.CurrentRow);

    public static WindowFrameBound Following(object offset) =>
        new(WindowFrameBoundKind.Following, Coerce(offset));

    public static WindowFrameBound UnboundedFollowing() =>
        new(WindowFrameBoundKind.UnboundedFollowing);

    public static WindowFrame Frame(
        WindowFrameUnit unit,
        WindowFrameBound start,
        WindowFrameBound? end = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        ValidateWindowFrameBound(start, nameof(start));
        if (end is null)
        {
            if (start.Kind is WindowFrameBoundKind.Following
                or WindowFrameBoundKind.UnboundedFollowing)
            {
                throw new ArgumentException(
                    "A single window frame bound cannot use FOLLOWING.",
                    nameof(start));
            }

            return new WindowFrame(unit, start);
        }

        ValidateWindowFrameBound(end, nameof(end));
        if (start.Kind == WindowFrameBoundKind.UnboundedFollowing)
        {
            throw new ArgumentException(
                "A window frame cannot start with UNBOUNDED FOLLOWING.",
                nameof(start));
        }

        if (end.Kind == WindowFrameBoundKind.UnboundedPreceding)
        {
            throw new ArgumentException(
                "A window frame cannot end with UNBOUNDED PRECEDING.",
                nameof(end));
        }

        if (GetWindowFrameBoundRank(start.Kind) > GetWindowFrameBoundRank(end.Kind))
        {
            throw new ArgumentException(
                "The window frame start must not follow its end.",
                nameof(start));
        }

        return new WindowFrame(unit, start, end);
    }

    public static NamedTable Table(string name, string? alias = null) => new(name, alias);

    public static DerivedTable Derived(SqlQuery query, string alias) =>
        new(query, new SqlIdentifier(alias));

    public static Assignment Assign(string column, object? value) =>
        new(Col(column), Coerce(value));

    public static SelectBuilder Select(params string[] columns) =>
        new(columns.Select(static column => new SelectItem(Col(column))).ToArray());

    public static SelectBuilder Select(params SqlExpression[] expressions) =>
        new(expressions.Select(static expression => new SelectItem(expression)).ToArray());

    public static SelectBuilder SelectItems(params SelectItem[] items) => new(items);

    public static ValuesBuilder Values(params object?[] values) =>
        new(values.Select(Coerce).ToArray());

    public static ExplainBuilder Explain(SqlQuery query, bool analyze = false) => new(query, analyze);

    public static InsertBuilder InsertInto(string table) => new(table);

    public static UpdateBuilder Update(string table, string? alias = null) => new(table, alias);

    public static DeleteBuilder DeleteFrom(string table, string? alias = null) => new(table, alias);

    public static MergeBuilder MergeInto(string table, string? alias = null) => new(table, alias);

    public static CreateTableBuilder CreateTable(string table) => new(table);

    public static AlterTableBuilder AlterTable(string table) => new(table);

    public static DropBuilder Drop(SchemaObjectKind kind, string name) => new(kind, name);

    public static DropBuilder DropTable(string table) => Drop(SchemaObjectKind.Table, table);

    public static TruncateBuilder Truncate(string table) => new(table);

    public static CreateViewBuilder CreateView(string view) => new(view);

    public static CreateIndexBuilder CreateIndex(string index, string table) => new(index, table);

    public static CreateSequenceBuilder CreateSequence(string sequence) => new(sequence);

    public static AlterSequenceBuilder AlterSequence(string sequence) => new(sequence);

    public static ColumnDefinition DefineColumn(
        string name,
        SqlDataType dataType,
        Nullability nullability = Nullability.Unspecified,
        SqlExpression? defaultValue = null,
        SqlExpression? generatedExpression = null,
        GeneratedColumnKind generatedKind = GeneratedColumnKind.Virtual,
        IdentityGeneration identity = IdentityGeneration.None,
        bool primaryKey = false,
        bool unique = false)
    {
        ArgumentNullException.ThrowIfNull(dataType);
        var generatedClauseCount =
            (defaultValue is null ? 0 : 1)
            + (generatedExpression is null ? 0 : 1)
            + (identity == IdentityGeneration.None ? 0 : 1);
        if (generatedClauseCount > 1)
        {
            throw new ArgumentException(
                "DEFAULT, generated expressions, and identity generation are mutually exclusive.");
        }

        return new(
            new SqlIdentifier(name),
            dataType,
            nullability,
            defaultValue,
            generatedExpression,
            generatedKind,
            identity,
            primaryKey,
            unique);
    }

    public static PrimaryKeyConstraint PrimaryKey(
        IReadOnlyList<string> columns,
        string? name = null)
    {
        ValidateColumns(columns, nameof(columns));
        return new(
            columns.Select(static column => new SqlIdentifier(column)).ToArray(),
            name is null ? null : new SqlIdentifier(name));
    }

    public static UniqueConstraint Unique(
        IReadOnlyList<string> columns,
        string? name = null)
    {
        ValidateColumns(columns, nameof(columns));
        return new(
            columns.Select(static column => new SqlIdentifier(column)).ToArray(),
            name is null ? null : new SqlIdentifier(name));
    }

    public static ForeignKeyConstraint ForeignKey(
        IReadOnlyList<string> columns,
        string referencedTable,
        IReadOnlyList<string> referencedColumns,
        ReferentialAction onDelete = ReferentialAction.Unspecified,
        ReferentialAction onUpdate = ReferentialAction.Unspecified,
        string? name = null)
    {
        ValidateColumns(columns, nameof(columns));
        ValidateColumns(referencedColumns, nameof(referencedColumns));
        if (columns.Count != referencedColumns.Count)
        {
            throw new ArgumentException(
                "Foreign key and referenced column counts must match.",
                nameof(referencedColumns));
        }

        return new(
            columns.Select(static column => new SqlIdentifier(column)).ToArray(),
            new TableName(referencedTable),
            referencedColumns.Select(static column => new SqlIdentifier(column)).ToArray(),
            onDelete,
            onUpdate,
            name is null ? null : new SqlIdentifier(name));
    }

    public static CheckConstraint Check(SqlExpression condition, string? name = null) =>
        new(condition, name is null ? null : new SqlIdentifier(name));

    public static CaseBuilder Case(SqlExpression? operand = null) => new(operand);

    internal static SqlExpression Coerce(object? value) =>
        value as SqlExpression ?? Lit(value);

    private static void ValidateColumns(IReadOnlyList<string> columns, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(columns, parameterName);
        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one column is required.", parameterName);
        }
    }

    private static void ValidateWindowFrameBound(WindowFrameBound bound, string parameterName)
    {
        var requiresOffset = bound.Kind is WindowFrameBoundKind.Preceding
            or WindowFrameBoundKind.Following;
        if (requiresOffset != (bound.Offset is not null))
        {
            throw new ArgumentException(
                requiresOffset
                    ? "PRECEDING and FOLLOWING bounds require an offset."
                    : "Only PRECEDING and FOLLOWING bounds can have an offset.",
                parameterName);
        }
    }

    private static int GetWindowFrameBoundRank(WindowFrameBoundKind kind) =>
        kind switch
        {
            WindowFrameBoundKind.UnboundedPreceding => 0,
            WindowFrameBoundKind.Preceding => 1,
            WindowFrameBoundKind.CurrentRow => 2,
            WindowFrameBoundKind.Following => 3,
            WindowFrameBoundKind.UnboundedFollowing => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static bool IsUnsupportedFloatingPoint(object? value)
    {
        var text = value switch
        {
            float number when float.IsFinite(number) =>
                number.ToString(null, CultureInfo.InvariantCulture),
            double number when double.IsFinite(number) =>
                number.ToString(null, CultureInfo.InvariantCulture),
            float or double => null,
            _ => string.Empty,
        };
        return text is null || text.Contains('E') || text.Contains('e');
    }

    private static bool IsSupportedLiteralValue(object? value) =>
        value is null
            or bool
            or string
            or char
            or DateTime
            or DateTimeOffset
            or byte
            or sbyte
            or short
            or ushort
            or int
            or uint
            or long
            or ulong
            or float
            or double
            or decimal;
}

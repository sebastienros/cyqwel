using Cyqwel.Ast;

namespace Cyqwel;

/// <summary>
/// Creates dialect-neutral SQL expressions and fluent statement builders.
/// </summary>
public static class Sql
{
    public static ColumnExpression Col(string name) => new(name);

    public static StarExpression Star() => new();

    public static LiteralExpression Lit(object? value) => new(value);

    public static ParameterExpression Param(string name, char prefix = '@') => new(name, prefix);

    public static FunctionCallExpression Func(string name, params SqlExpression[] arguments) =>
        new(name, arguments);

    public static FunctionCallExpression Count(SqlExpression expression, bool distinct = false) =>
        new(new SqlIdentifier("COUNT"), [expression], distinct);

    public static FunctionCallExpression CountStar() => Count(Star());

    public static CastExpression Cast(SqlExpression expression, string dataType, params int[] arguments) =>
        new(expression, new SqlDataType(dataType, arguments));

    public static SelectBuilder Select(params string[] columns) =>
        new(columns.Select(static column => new SelectItem(Col(column))).ToArray());

    public static SelectBuilder Select(params SqlExpression[] expressions) =>
        new(expressions.Select(static expression => new SelectItem(expression)).ToArray());

    public static SelectBuilder SelectItems(params SelectItem[] items) => new(items);

    public static InsertBuilder InsertInto(string table) => new(table);

    public static UpdateBuilder Update(string table, string? alias = null) => new(table, alias);

    public static DeleteBuilder DeleteFrom(string table, string? alias = null) => new(table, alias);

    public static CaseBuilder Case(SqlExpression? operand = null) => new(operand);

    internal static SqlExpression Coerce(object? value) =>
        value as SqlExpression ?? Lit(value);
}

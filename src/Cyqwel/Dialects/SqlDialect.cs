using System.Collections.Concurrent;
using Cyqwel.Ast;
using Cyqwel.Generation;
using Cyqwel.Parsing;
using Cyqwel.Visitors;

namespace Cyqwel.Dialects;

public enum SqlLimitStyle
{
    LimitOffset,
    Top,
    OffsetFetch,
    LimitOffsetComma,
    FetchFirst,
}

public enum SqlConcatenationStyle
{
    DoublePipe,
    Plus,
    Function,
}

/// <summary>
/// Defines parser normalization and SQL generation behavior for a SQL dialect.
/// </summary>
public class SqlDialect
{
    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ALL", "AND", "AS", "ASC", "BETWEEN", "BY", "CASE", "CAST", "CROSS", "DELETE",
        "DESC", "DISTINCT", "ELSE", "END", "EXCEPT", "EXISTS", "FALSE", "FETCH", "FIRST",
        "FROM", "FULL", "GROUP", "HAVING", "ILIKE", "IN", "INNER", "INSERT", "INTERSECT",
        "INTO", "IS", "JOIN", "LAST", "LEFT", "LIKE", "LIMIT", "NEXT", "NOT", "NULL",
        "NULLS", "OFFSET", "ON", "ONLY", "OR", "ORDER", "OUTER", "RETURNING", "RIGHT",
        "ROW", "ROWS", "SELECT", "SET", "THEN", "TOP", "TRUE", "UNION", "UPDATE", "VALUES",
        "WHEN", "WHERE", "WITH", "MERGE", "CREATE", "ALTER", "DROP", "TRUNCATE", "TABLE",
        "VIEW", "INDEX", "SEQUENCE", "CONNECT", "PRIOR", "START", "SIBLINGS",
    };

    public SqlDialect(
        string name,
        char identifierOpenQuote = '"',
        char identifierCloseQuote = '"',
        SqlLimitStyle limitStyle = SqlLimitStyle.LimitOffset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        IdentifierOpenQuote = identifierOpenQuote;
        IdentifierCloseQuote = identifierCloseQuote;
        LimitStyle = limitStyle;
    }

    public string Name { get; }

    public char IdentifierOpenQuote { get; }

    public char IdentifierCloseQuote { get; }

    public SqlLimitStyle LimitStyle { get; }

    public virtual bool SupportsILike => false;

    public virtual bool SupportsReturning => true;

    public virtual bool SupportsReturningInto => ParserOptions.SupportsReturningInto;

    public virtual bool RequiresOrderByForOffset => false;

    public virtual bool SupportsTableAliasAs => true;

    public virtual bool SupportsExplain => ParserOptions.SupportsExplainOptions;

    public virtual bool SupportsParenthesizedSetOperands => true;

    public virtual bool UsesSqlSecurityForViews => false;

    public virtual SqlConcatenationStyle ConcatenationStyle => SqlConcatenationStyle.DoublePipe;

    public virtual SqlDialectParserOptions ParserOptions => SqlDialectParserOptions.Permissive;

    public virtual string TrueLiteral => "TRUE";

    public virtual string FalseLiteral => "FALSE";

    public virtual SqlNode Preprocess(SqlNode node) => node;

    public virtual SqlNode TransformNode(SqlNode node) => node;

    public virtual string GetFunctionName(string name) => name;

    public virtual string GetSetOperator(SetOperator value) => value switch
    {
        SetOperator.Union => "UNION",
        SetOperator.Intersect => "INTERSECT",
        SetOperator.Except => "EXCEPT",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public virtual string? RenderLiteral(
        LiteralExpression literal,
        SqlGenerationOptions options) => null;

    public virtual string? RenderFunction(
        FunctionCallExpression function,
        Func<SqlExpression, string> renderExpression,
        SqlGenerationOptions options) => null;

    public virtual string? RenderParameter(ParameterExpression parameter) => null;

    public virtual bool ShouldQuoteIdentifier(SqlIdentifier identifier) =>
        identifier.IsQuoted
        || identifier.Value.Length == 0
        || !IsSafeIdentifier(identifier.Value)
        || ReservedKeywords.Contains(identifier.Value);

    private static bool IsSafeIdentifier(string value)
    {
        if (value == "*") return true;
        if (value.Length == 0 || !(value[0] == '_' || char.IsLetter(value[0]))) return false;

        for (var i = 1; i < value.Length; i++)
        {
            if (!(value[i] is '_' or '$') && !char.IsLetterOrDigit(value[i])) return false;
        }

        return true;
    }

    public SqlDocument Parse(string sql, SqlParseOptions? options = null) =>
        SqlParser.Parse(sql, this, options);

    public bool TryParse(
        string sql,
        out SqlDocument? document,
        out SqlParseError? error,
        SqlParseOptions? options = null) =>
        SqlParser.TryParse(sql, this, out document, out error, options);

    public string Generate(SqlNode node, SqlGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(node);

        var preprocessed = Preprocess(node);
        var transformed = new DialectRewriter(this).Visit(preprocessed);
        return new SqlGenerator(this, options ?? SqlGenerationOptions.Default).Generate(transformed);
    }

    public string Transpile(string sql, SqlDialect target, SqlGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Generate(Parse(sql), options);
    }

    private sealed class DialectRewriter(SqlDialect dialect) : SqlRewriter
    {
        public override SqlNode Visit(SqlNode node) => dialect.TransformNode(base.Visit(node));
    }
}

public static class SqlDialects
{
    public static SqlDialect Generic { get; } = new GenericDialect();

    public static SqlDialect TSql { get; } = new TSqlDialect();

    public static SqlDialect Sqlite { get; } = new SqliteDialect();

    public static SqlDialect PostgreSql { get; } = new PostgreSqlDialect();

    public static SqlDialect MySql { get; } = new MySqlDialect();

    public static SqlDialect Oracle { get; } = new OracleDialect();

    public static IReadOnlyList<SqlDialect> BuiltIn { get; } =
        [Generic, TSql, Sqlite, PostgreSql, MySql, Oracle];

    private sealed class GenericDialect() : SqlDialect("generic");

    private sealed class TSqlDialect() : SqlDialect("tsql", '[', ']', SqlLimitStyle.Top)
    {
        public override bool SupportsReturning => false;
        public override bool RequiresOrderByForOffset => true;
        public override SqlConcatenationStyle ConcatenationStyle => SqlConcatenationStyle.Plus;
        public override SqlDialectParserOptions ParserOptions { get; } = new()
        {
            IdentifierQuotes = SqlIdentifierQuoteStyle.DoubleQuote | SqlIdentifierQuoteStyle.Brackets,
            ParameterStyles = SqlParameterStyle.AtNamed,
            SupportsTop = true,
            SupportsLimit = false,
            SupportsOffsetOnly = false,
            SupportsOffsetFetch = true,
            SupportsReturning = false,
            SupportsILike = false,
            SupportsNullOrdering = false,
            DoublePipeBehavior = SqlDoublePipeBehavior.Concatenate,
        };
        public override string TrueLiteral => "1";
        public override string FalseLiteral => "0";

        public override string GetFunctionName(string name) => name.ToUpperInvariant() switch
        {
            "CURRENT_TIMESTAMP" or "NOW" => "GETDATE",
            "CLOCK_TIMESTAMP" => "SYSDATETIME",
            "LN" => "LOG",
            "CHR" => "CHAR",
            "REPEAT" => "REPLICATE",
            _ => name,
        };

    }

    private sealed class SqliteDialect() : SqlDialect("sqlite")
    {
        public override bool SupportsParenthesizedSetOperands => false;

        public override SqlDialectParserOptions ParserOptions { get; } = new()
        {
            IdentifierQuotes = SqlIdentifierQuoteStyle.DoubleQuote
                | SqlIdentifierQuoteStyle.Backtick
                | SqlIdentifierQuoteStyle.Brackets,
            ParameterStyles = SqlParameterStyle.QuestionMark
                | SqlParameterStyle.AtNamed
                | SqlParameterStyle.ColonNamed
                | SqlParameterStyle.DollarNamed,
            SupportsLimit = true,
            SupportsLimitComma = true,
            SupportsOffsetOnly = false,
            SupportsReturning = true,
            SupportsILike = false,
            SupportsNullOrdering = true,
            DoublePipeBehavior = SqlDoublePipeBehavior.Concatenate,
        };
        public override string TrueLiteral => "1";
        public override string FalseLiteral => "0";

        public override string GetFunctionName(string name) => name.ToUpperInvariant() switch
        {
            "NOW" => "DATETIME",
            "LEAST" => "MIN",
            "GREATEST" => "MAX",
            "JSON_AGG" or "JSONB_AGG" => "JSON_GROUP_ARRAY",
            "JSON_OBJECT_AGG" => "JSON_GROUP_OBJECT",
            "JSON_BUILD_OBJECT" => "JSON_OBJECT",
            "JSON_BUILD_ARRAY" => "JSON_ARRAY",
            _ => name,
        };

        public override SqlNode TransformNode(SqlNode node) => node switch
        {
            ExtractExpression value when TryCreateDatePart(value.Field.Value, value.Expression, out var replacement) =>
                replacement,
            FunctionCallExpression value when TryTransformDateFunction(value, out var replacement) =>
                replacement,
            _ => node,
        };

        private static bool TryTransformDateFunction(
            FunctionCallExpression function,
            out SqlExpression replacement)
        {
            replacement = null!;
            if (function.Arguments.Count != 2
                || function.Arguments[0] is not LiteralExpression { Value: string part })
            {
                return false;
            }

            if (function.Name.Value.Equals("DATE_PART", StringComparison.OrdinalIgnoreCase))
            {
                return TryCreateDatePart(part, function.Arguments[1], out replacement);
            }

            if (!function.Name.Value.Equals("DATE_TRUNC", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            replacement = part.ToUpperInvariant() switch
            {
                "MONTH" => Strftime("%Y-%m-01", function.Arguments[1]),
                "HOUR" => Strftime("%Y-%m-%d %H:00:00", function.Arguments[1]),
                "MINUTE" => Strftime("%Y-%m-%d %H:%M:00", function.Arguments[1]),
                "SECOND" => Strftime("%Y-%m-%d %H:%M:%S", function.Arguments[1]),
                "DAY" => new FunctionCallExpression("DATE", function.Arguments[1]),
                _ => null!,
            };
            return replacement is not null;
        }

        private static bool TryCreateDatePart(
            string part,
            SqlExpression expression,
            out SqlExpression replacement)
        {
            var (format, dataType) = part.ToUpperInvariant() switch
            {
                "YEAR" => ("%Y", "INTEGER"),
                "HOUR" => ("%H", "INTEGER"),
                "MINUTE" => ("%M", "INTEGER"),
                "SECOND" => ("%f", "REAL"),
                "DOW" => ("%w", "INTEGER"),
                "DOY" => ("%j", "INTEGER"),
                "EPOCH" => ("%s", "REAL"),
                _ => (null, null),
            };
            replacement = format is null
                ? null!
                : new CastExpression(Strftime(format, expression), new SqlDataType(dataType!));
            return replacement is not null;
        }

        private static FunctionCallExpression Strftime(string format, SqlExpression expression) =>
            new("STRFTIME", new LiteralExpression(format), expression);
    }

    private sealed class PostgreSqlDialect() : SqlDialect("postgresql")
    {
        public override bool SupportsILike => true;
        public override SqlDialectParserOptions ParserOptions { get; } = new()
        {
            IdentifierQuotes = SqlIdentifierQuoteStyle.DoubleQuote,
            ParameterStyles = SqlParameterStyle.DollarNumbered,
            SupportsLimit = true,
            SupportsOffsetOnly = true,
            SupportsOffsetFetch = true,
            SupportsReturning = true,
            SupportsILike = true,
            SupportsNullOrdering = true,
            SupportsExplainOptions = true,
            DoublePipeBehavior = SqlDoublePipeBehavior.Concatenate,
        };
    }

    private sealed class MySqlDialect() : SqlDialect("mysql", '`', '`', SqlLimitStyle.LimitOffsetComma)
    {
        public override bool SupportsReturning => false;
        public override SqlConcatenationStyle ConcatenationStyle => SqlConcatenationStyle.Function;
        public override bool UsesSqlSecurityForViews => true;
        public override SqlDialectParserOptions ParserOptions { get; } = new()
        {
            IdentifierQuotes = SqlIdentifierQuoteStyle.Backtick,
            ParameterStyles = SqlParameterStyle.QuestionMark,
            SupportsDoubleQuotedStrings = true,
            SupportsBackslashStringEscapes = true,
            SupportsLimit = true,
            SupportsLimitComma = true,
            SupportsOffsetOnly = false,
            SupportsReturning = false,
            SupportsILike = false,
            SupportsNullOrdering = false,
            SupportsCreateViewSecurity = true,
            DoublePipeBehavior = SqlDoublePipeBehavior.LogicalOr,
        };

        public override string GetFunctionName(string name) =>
            name.Equals("COALESCE", StringComparison.OrdinalIgnoreCase) ? "COALESCE" : name;
    }

    private sealed class OracleDialect() : SqlDialect("oracle", limitStyle: SqlLimitStyle.FetchFirst)
    {
        public override bool SupportsTableAliasAs => false;
        public override SqlDialectParserOptions ParserOptions { get; } = new()
        {
            IdentifierQuotes = SqlIdentifierQuoteStyle.DoubleQuote,
            ParameterStyles = SqlParameterStyle.ColonNamed,
            SupportsLimit = false,
            SupportsOffsetOnly = false,
            SupportsOffsetFetch = true,
            SupportsReturning = true,
            SupportsReturningInto = true,
            SupportsILike = false,
            SupportsNullOrdering = true,
            SupportsMinus = true,
            SupportsRecursiveCte = false,
            SupportsHierarchicalQueries = true,
            SupportsTableAliasAs = false,
            SupportsOracleDataTypes = true,
            DoublePipeBehavior = SqlDoublePipeBehavior.Concatenate,
        };

        public override string TrueLiteral => "1";
        public override string FalseLiteral => "0";

        public override string GetFunctionName(string name) => name.ToUpperInvariant() switch
        {
            "IFNULL" => "NVL",
            "SUBSTRING" => "SUBSTR",
            "RANDOM" or "RAND" => "DBMS_RANDOM.VALUE",
            _ => name,
        };

        public override string GetSetOperator(SetOperator value) =>
            value == SetOperator.Except ? "MINUS" : base.GetSetOperator(value);

        public override string? RenderFunction(
            FunctionCallExpression function,
            Func<SqlExpression, string> renderExpression,
            SqlGenerationOptions options)
        {
            if (function.Name.Value.Equals("NOW", StringComparison.OrdinalIgnoreCase)
                || function.Name.Value.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase)
                    && function.Arguments.Count == 0)
            {
                return "SYSTIMESTAMP";
            }

            if (function.Name.Value.Equals("COALESCE", StringComparison.OrdinalIgnoreCase)
                && function.Arguments.Count == 2)
            {
                return $"NVL({renderExpression(function.Arguments[0])}, {renderExpression(function.Arguments[1])})";
            }

            return null;
        }

        public override string? RenderParameter(ParameterExpression parameter) =>
            parameter.Name.Length == 0 ? null : $":{parameter.Name}";

        public override SqlNode TransformNode(SqlNode node) => node switch
        {
            TryCastExpression value => new CastExpression(value.Expression, value.DataType)
            {
                Span = value.Span,
            },
            BinaryExpression { Operator: BinaryOperator.ILike or BinaryOperator.NotILike } value =>
                new BinaryExpression(
                    new FunctionCallExpression("LOWER", value.Left),
                    value.Operator == BinaryOperator.ILike ? BinaryOperator.Like : BinaryOperator.NotLike,
                    new FunctionCallExpression("LOWER", value.Right))
                {
                    Span = value.Span,
                },
            _ => node,
        };
    }
}

public static class SqlDialectRegistry
{
    private static readonly ConcurrentDictionary<string, SqlDialect> Dialects =
        new(StringComparer.OrdinalIgnoreCase);

    static SqlDialectRegistry()
    {
        foreach (var dialect in SqlDialects.BuiltIn)
        {
            Dialects.TryAdd(dialect.Name, dialect);
        }

        Dialects.TryAdd("mssql", SqlDialects.TSql);
        Dialects.TryAdd("sqlserver", SqlDialects.TSql);
        Dialects.TryAdd("postgres", SqlDialects.PostgreSql);
    }

    public static IEnumerable<SqlDialect> All => Dialects.Values.Distinct();

    public static SqlDialect Get(string name) =>
        TryGet(name, out var dialect)
            ? dialect
            : throw new KeyNotFoundException($"SQL dialect '{name}' is not registered.");

    public static bool TryGet(string name, out SqlDialect dialect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Dialects.TryGetValue(name, out dialect!);
    }

    public static void Register(SqlDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        if (!Dialects.TryAdd(dialect.Name, dialect))
        {
            throw new InvalidOperationException($"SQL dialect '{dialect.Name}' is already registered.");
        }
    }

    public static bool Unregister(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (SqlDialects.BuiltIn.Any(dialect => dialect.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Built-in SQL dialect '{name}' cannot be unregistered.");
        }

        return Dialects.TryRemove(name, out _);
    }
}

public sealed class SqlDialectBuilder
{
    private readonly string _name;
    private SqlDialect _baseDialect = SqlDialects.Generic;
    private Func<SqlNode, SqlNode>? _preprocess;
    private Func<SqlNode, SqlNode>? _transform;
    private Func<string, string>? _functionName;
    private Func<LiteralExpression, SqlGenerationOptions, string?>? _literalRenderer;
    private Func<FunctionCallExpression, Func<SqlExpression, string>, SqlGenerationOptions, string?>? _functionRenderer;
    private Func<SqlDialectParserOptions, SqlDialectParserOptions>? _parserOptions;

    private SqlDialectBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    public static SqlDialectBuilder Create(string name) => new(name);

    public SqlDialectBuilder BasedOn(SqlDialect dialect)
    {
        _baseDialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        return this;
    }

    public SqlDialectBuilder WithPreprocessor(Func<SqlNode, SqlNode> preprocess)
    {
        _preprocess = preprocess ?? throw new ArgumentNullException(nameof(preprocess));
        return this;
    }

    public SqlDialectBuilder WithNodeTransform(Func<SqlNode, SqlNode> transform)
    {
        _transform = transform ?? throw new ArgumentNullException(nameof(transform));
        return this;
    }

    public SqlDialectBuilder WithFunctionNameTransform(Func<string, string> transform)
    {
        _functionName = transform ?? throw new ArgumentNullException(nameof(transform));
        return this;
    }

    public SqlDialectBuilder WithLiteralRenderer(
        Func<LiteralExpression, SqlGenerationOptions, string?> renderer)
    {
        _literalRenderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        return this;
    }

    public SqlDialectBuilder WithFunctionRenderer(
        Func<FunctionCallExpression, Func<SqlExpression, string>, SqlGenerationOptions, string?> renderer)
    {
        _functionRenderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        return this;
    }

    public SqlDialectBuilder ConfigureParser(
        Func<SqlDialectParserOptions, SqlDialectParserOptions> configure)
    {
        _parserOptions = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    public SqlDialect Build() => new DelegatingDialect(
        _name,
        _baseDialect,
        _preprocess,
        _transform,
        _functionName,
        _literalRenderer,
        _functionRenderer,
        _parserOptions);

    public SqlDialect Register()
    {
        var dialect = Build();
        SqlDialectRegistry.Register(dialect);
        return dialect;
    }

    private sealed class DelegatingDialect(
        string name,
        SqlDialect baseDialect,
        Func<SqlNode, SqlNode>? preprocess,
        Func<SqlNode, SqlNode>? transform,
        Func<string, string>? functionName,
        Func<LiteralExpression, SqlGenerationOptions, string?>? literalRenderer,
        Func<FunctionCallExpression, Func<SqlExpression, string>, SqlGenerationOptions, string?>? functionRenderer,
        Func<SqlDialectParserOptions, SqlDialectParserOptions>? parserOptions)
        : SqlDialect(
            name,
            baseDialect.IdentifierOpenQuote,
            baseDialect.IdentifierCloseQuote,
            baseDialect.LimitStyle)
    {
        public override bool SupportsILike => baseDialect.SupportsILike;
        public override bool SupportsReturning => baseDialect.SupportsReturning;
        public override bool SupportsReturningInto => baseDialect.SupportsReturningInto;
        public override bool SupportsParenthesizedSetOperands => baseDialect.SupportsParenthesizedSetOperands;
        public override bool RequiresOrderByForOffset => baseDialect.RequiresOrderByForOffset;
        public override bool SupportsTableAliasAs => baseDialect.SupportsTableAliasAs;
        public override bool UsesSqlSecurityForViews => baseDialect.UsesSqlSecurityForViews;
        public override SqlConcatenationStyle ConcatenationStyle => baseDialect.ConcatenationStyle;
        public override SqlDialectParserOptions ParserOptions { get; } =
            parserOptions is null
                ? baseDialect.ParserOptions
                : parserOptions(baseDialect.ParserOptions);
        public override string TrueLiteral => baseDialect.TrueLiteral;
        public override string FalseLiteral => baseDialect.FalseLiteral;

        public override SqlNode Preprocess(SqlNode node) =>
            preprocess is null ? baseDialect.Preprocess(node) : preprocess(baseDialect.Preprocess(node));

        public override SqlNode TransformNode(SqlNode node) =>
            transform is null ? baseDialect.TransformNode(node) : transform(baseDialect.TransformNode(node));

        public override string GetFunctionName(string value) =>
            functionName is null
                ? baseDialect.GetFunctionName(value)
                : functionName(baseDialect.GetFunctionName(value));

        public override string GetSetOperator(SetOperator value) =>
            baseDialect.GetSetOperator(value);

        public override string? RenderLiteral(
            LiteralExpression literal,
            SqlGenerationOptions options) =>
            literalRenderer?.Invoke(literal, options)
            ?? baseDialect.RenderLiteral(literal, options);

        public override string? RenderFunction(
            FunctionCallExpression function,
            Func<SqlExpression, string> renderExpression,
            SqlGenerationOptions options) =>
            functionRenderer?.Invoke(function, renderExpression, options)
            ?? baseDialect.RenderFunction(function, renderExpression, options);

        public override string? RenderParameter(ParameterExpression parameter) =>
            baseDialect.RenderParameter(parameter);

        public override bool ShouldQuoteIdentifier(SqlIdentifier identifier) =>
            baseDialect.ShouldQuoteIdentifier(identifier);
    }
}

using Cyqwel.Ast;
using Cyqwel.Parsing;

namespace Cyqwel.Validation;

public enum SqlValidationSeverity
{
    Warning,
    Error,
}

public sealed record SqlValidationLocation(SqlTextSpan Span, int Line, int Column);

public sealed record SqlValidationDiagnostic(
    SqlValidationSeverity Severity,
    string Code,
    string Message,
    SqlValidationLocation? Location = null);

public sealed record SqlValidationResult(IReadOnlyList<SqlValidationDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(static diagnostic =>
        diagnostic.Severity != SqlValidationSeverity.Error);
}

public sealed record SqlValidationOptions
{
    public static SqlValidationOptions Default { get; } = new();

    public bool StrictSyntax { get; init; }

    public bool Semantic { get; init; }

    public SqlParseOptions ParseOptions { get; init; } = SqlParseOptions.Default;
}

public sealed record SqlSchemaValidationOptions
{
    public static SqlSchemaValidationOptions Default { get; } = new();

    public bool StrictSyntax { get; init; }

    public bool Semantic { get; init; }

    public bool Strict { get; init; } = true;

    public bool CheckTypes { get; init; }

    public bool CheckReferences { get; init; }

    public SqlParseOptions ParseOptions { get; init; } = SqlParseOptions.Default;
}

public sealed record SqlSchemaCatalog(IReadOnlyList<SqlTableSchema> Tables)
{
    public SqlSchemaCatalog(params SqlTableSchema[] tables)
        : this((IReadOnlyList<SqlTableSchema>)tables)
    {
    }
}

public sealed record SqlTableSchema(
    string Name,
    IReadOnlyList<SqlColumnSchema> Columns,
    string? Schema = null,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<string>? PrimaryKey = null,
    IReadOnlyList<IReadOnlyList<string>>? UniqueKeys = null,
    IReadOnlyList<SqlForeignKey>? ForeignKeys = null);

public sealed record SqlColumnSchema(
    string Name,
    string DataType,
    bool? IsNullable = null,
    bool IsPrimaryKey = false,
    bool IsUnique = false,
    SqlColumnReference? References = null);

public sealed record SqlColumnReference(string Table, string Column, string? Schema = null);

public sealed record SqlForeignKey(
    IReadOnlyList<string> Columns,
    SqlTableReference References,
    string? Name = null);

public sealed record SqlTableReference(
    string Table,
    IReadOnlyList<string> Columns,
    string? Schema = null);

public enum SqlTypeFamily
{
    Unknown,
    Boolean,
    Integer,
    Numeric,
    String,
    Binary,
    Date,
    Time,
    Timestamp,
    Interval,
    Json,
    Uuid,
    Array,
    Map,
    Struct,
}

public static class SqlValidationCodes
{
    public const string SyntaxError = "E000";
    public const string StrictSyntax = "E005";

    public const string SelectStar = "W001";
    public const string AggregateWithoutGroupBy = "W002";
    public const string DistinctOrderBy = "W003";
    public const string LimitWithoutOrderBy = "W004";

    public const string UnknownTable = "E200";
    public const string UnknownColumn = "E201";
    public const string UnknownFunction = "E202";
    public const string InvalidFunctionArity = "E203";
    public const string InvalidScalarSubquery = "E204";

    public const string TypeMismatch = "E210";
    public const string InvalidPredicateType = "E211";
    public const string InvalidArithmeticType = "E212";
    public const string InvalidFunctionArgumentType = "E213";
    public const string InvalidAssignmentType = "E214";
    public const string SetOperationTypeMismatch = "E215";
    public const string SetOperationArityMismatch = "E216";
    public const string IncompatibleComparisonTypes = "E217";

    public const string ImplicitComparisonCast = "W210";
    public const string ImplicitArithmeticCast = "W211";
    public const string ImplicitAssignmentCast = "W212";
    public const string SetOperationImplicitCoercion = "W214";
    public const string PredicateTypeConcern = "W215";
    public const string FunctionArgumentCoercion = "W216";

    public const string InvalidForeignKeyReference = "E220";
    public const string AmbiguousColumnReference = "E221";
    public const string UnresolvedReference = "E222";
    public const string CteColumnCountMismatch = "E223";

    public const string CartesianJoin = "W220";
    public const string JoinNotUsingDeclaredReference = "W221";
    public const string WeakReferenceIntegrity = "W222";
}

public static class SqlTypeFamilies
{
    public static SqlTypeFamily Classify(string dataType)
    {
        ArgumentNullException.ThrowIfNull(dataType);

        var normalized = dataType
            .Trim()
            .Trim('"', '\'', '`')
            .ToLowerInvariant();
        if (normalized.Length == 0) return SqlTypeFamily.Unknown;

        if (TryUnwrap(normalized, out var wrapper, out var inner))
        {
            if (wrapper is "nullable" or "lowcardinality") return Classify(inner);
            if (wrapper is "array" or "list") return SqlTypeFamily.Array;
            if (wrapper == "map") return SqlTypeFamily.Map;
            if (wrapper is "struct" or "row" or "record") return SqlTypeFamily.Struct;
        }

        if (normalized.EndsWith("[]", StringComparison.Ordinal)
            || normalized.StartsWith("array<", StringComparison.Ordinal)
            || normalized.StartsWith("list<", StringComparison.Ordinal))
        {
            return SqlTypeFamily.Array;
        }

        if (normalized.StartsWith("map<", StringComparison.Ordinal)) return SqlTypeFamily.Map;
        if (normalized.StartsWith("struct<", StringComparison.Ordinal)
            || normalized.StartsWith("row<", StringComparison.Ordinal)
            || normalized.StartsWith("record<", StringComparison.Ordinal)
            || normalized.StartsWith("object<", StringComparison.Ordinal))
        {
            return SqlTypeFamily.Struct;
        }

        var openParenthesis = normalized.IndexOf('(');
        if (openParenthesis >= 0) normalized = normalized[..openParenthesis].TrimEnd();
        normalized = normalized
            .Replace("unsigned ", "", StringComparison.Ordinal)
            .Replace(" unsigned", "", StringComparison.Ordinal);

        return normalized switch
        {
            "bool" or "boolean" => SqlTypeFamily.Boolean,
            "tinyint" or "smallint" or "int2" or "int" or "integer" or "int4" or "int8"
                or "bigint" or "serial" or "smallserial" or "bigserial" or "utinyint"
                or "usmallint" or "uinteger" or "ubigint" or "uint8" or "uint16" or "uint32"
                or "uint64" or "int16" or "int32" or "int64" => SqlTypeFamily.Integer,
            "numeric" or "decimal" or "dec" or "number" or "float" or "float4" or "float8"
                or "real" or "double" or "double precision" or "bfloat16" or "float16"
                or "float32" or "float64" => SqlTypeFamily.Numeric,
            "char" or "character" or "varchar" or "character varying" or "nchar" or "nvarchar"
                or "text" or "string" or "clob" => SqlTypeFamily.String,
            "binary" or "varbinary" or "blob" or "bytea" or "bytes" => SqlTypeFamily.Binary,
            "date" => SqlTypeFamily.Date,
            "time" => SqlTypeFamily.Time,
            "timestamp" or "timestamptz" or "datetime" or "datetime2" or "smalldatetime"
                or "timestamp with time zone" or "timestamp without time zone" =>
                SqlTypeFamily.Timestamp,
            "interval" => SqlTypeFamily.Interval,
            "json" or "jsonb" or "variant" => SqlTypeFamily.Json,
            "uuid" or "uniqueidentifier" => SqlTypeFamily.Uuid,
            "array" or "list" => SqlTypeFamily.Array,
            "map" => SqlTypeFamily.Map,
            "struct" or "row" or "record" or "object" => SqlTypeFamily.Struct,
            _ => SqlTypeFamily.Unknown,
        };
    }

    private static bool TryUnwrap(
        string dataType,
        out string wrapper,
        out string inner)
    {
        var openParenthesis = dataType.IndexOf('(');
        if (openParenthesis <= 0 || !dataType.EndsWith(')'))
        {
            wrapper = "";
            inner = "";
            return false;
        }

        wrapper = dataType[..openParenthesis].Trim();
        inner = dataType[(openParenthesis + 1)..^1].Trim();
        return inner.Length > 0;
    }
}

using System.Globalization;
using Cyqwel.Ast;

namespace Cyqwel.Generation;

public sealed partial class SqlGenerator
{
    private void WriteTSqlTableSample(TSqlTableSample sample)
    {
        RequireTSql("TABLESAMPLE");
        if (!sample.IsValid) throw new ArgumentException("The TABLESAMPLE amount or unit is invalid.", nameof(sample));
        Keyword("TABLESAMPLE");
        if (sample.IsSystem) { Space(); Keyword("SYSTEM"); }
        _builder.Append(" (");
        WriteExpression(sample.Amount);
        if (sample.Unit != TSqlTableSampleUnit.Unspecified)
        {
            Space();
            Keyword(sample.Unit == TSqlTableSampleUnit.Percent ? "PERCENT" : "ROWS");
        }
        _builder.Append(')');
    }

    private void WriteTSqlTableHint(TSqlTableHint hint)
    {
        RequireTSql("table hints");
        if (!Enum.IsDefined(hint.Kind)
            || (hint.Kind == TSqlTableHintKind.Index ? hint.Indexes is not { Count: > 0 } : hint.Indexes is not null))
            throw new ArgumentException("The table hint has incompatible or missing arguments.", nameof(hint));
        Keyword(hint.Kind.ToString().ToUpperInvariant());
        if (hint.Indexes is not null)
        {
            _builder.Append('(');
            WriteSeparated(hint.Indexes, WriteTSqlIndexReference);
            _builder.Append(')');
        }
    }

    private void WriteTSqlIndexReference(TSqlIndexReference index)
    {
        RequireTSql("index hints");
        if (!index.IsValid)
            throw new ArgumentException("An index reference requires either a name or a nonnegative ID.", nameof(index));
        if (index.Name is not null) WriteIdentifier(index.Name);
        else _builder.Append(index.Id!.Value.ToString(CultureInfo.InvariantCulture));
    }

    private void RequireTSql(string feature)
    {
        if (!_dialect.ParserOptions.SupportsTSqlExtensions)
            Unsupported($"{_dialect.Name} cannot represent T-SQL {feature}.");
    }

    private void WriteSourceAlias(SqlIdentifier? alias)
    {
        if (alias is null) return;
        Space();
        if (_dialect.SupportsTableAliasAs)
        {
            Keyword("AS");
            Space();
        }
        WriteIdentifier(alias);
    }

    private void WriteSourceColumns(IReadOnlyList<SqlIdentifier>? columns)
    {
        if (columns is not { Count: > 0 }) return;
        _builder.Append(" (");
        WriteSeparated(columns, WriteIdentifier);
        _builder.Append(')');
    }

    private void WriteOpenJson(OpenJsonTable json)
    {
        RequireTSql("OPENJSON");
        Keyword("OPENJSON");
        _builder.Append('(');
        WriteExpression(json.Expression);
        if (json.Path is not null)
        {
            _builder.Append(", ");
            WriteExpression(json.Path);
        }
        _builder.Append(')');
        if (json.Schema is { Count: > 0 })
        {
            Space();
            Keyword("WITH");
            _builder.Append(" (");
            WriteSeparated(json.Schema, WriteOpenJsonColumn);
            _builder.Append(')');
        }
        WriteSourceAlias(json.Alias);
    }

    private void WriteOpenJsonColumn(OpenJsonColumn column)
    {
        RequireTSql("OPENJSON schema");
        WriteIdentifier(column.Name);
        Space();
        WriteDataType(column.DataType);
        if (column.Path is not null)
        {
            Space();
            WriteExpression(column.Path);
        }
        if (column.AsJson)
        {
            Space();
            Keyword("AS JSON");
        }
    }

    private void WritePivot(PivotTable pivot)
    {
        RequireTSql("PIVOT");
        WriteTableSource(pivot.Source);
        Space();
        Keyword("PIVOT");
        _builder.Append(" (");
        WriteExpression(pivot.Aggregate);
        Space();
        Keyword("FOR");
        Space();
        WriteExpression(pivot.Column);
        Space();
        Keyword("IN");
        WriteSourceColumns(pivot.Values);
        _builder.Append(')');
        WriteSourceAlias(pivot.Alias);
    }

    private void WriteUnpivot(UnpivotTable unpivot)
    {
        RequireTSql("UNPIVOT");
        WriteTableSource(unpivot.Source);
        Space();
        Keyword("UNPIVOT");
        _builder.Append(" (");
        WriteIdentifier(unpivot.ValueColumn);
        Space();
        Keyword("FOR");
        Space();
        WriteIdentifier(unpivot.NameColumn);
        Space();
        Keyword("IN");
        WriteSourceColumns(unpivot.Columns);
        _builder.Append(')');
        WriteSourceAlias(unpivot.Alias);
    }

    private void WriteTSqlQueryOptions(IReadOnlyList<TSqlQueryOption>? options)
    {
        if (options is not { Count: > 0 }) return;
        if (options.Select(option => option.Kind).Distinct().Count() != options.Count)
            throw new ArgumentException("Duplicate query options are not allowed.", nameof(options));
        RequireTSql("OPTION");
        ClauseBreak();
        Keyword("OPTION");
        _builder.Append(" (");
        WriteSeparated(options, WriteTSqlQueryOption);
        _builder.Append(')');
    }

    private void WriteTSqlQueryOption(TSqlQueryOption option)
    {
        if (!option.IsValid) throw new ArgumentException("The query option has an invalid value.", nameof(option));
        RequireTSql("OPTION");
        Keyword(option.Kind switch
        {
            TSqlQueryOptionKind.Recompile => "RECOMPILE",
            TSqlQueryOptionKind.MaxDop => "MAXDOP",
            TSqlQueryOptionKind.MaxRecursion => "MAXRECURSION",
            TSqlQueryOptionKind.OptimizeForUnknown => "OPTIMIZE FOR UNKNOWN",
            TSqlQueryOptionKind.ForceOrder => "FORCE ORDER",
            TSqlQueryOptionKind.Fast => "FAST",
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        });
        if (option.Value is not null)
        {
            Space();
            _builder.Append(option.Value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void WriteTSqlResultFormat(TSqlResultFormat? format)
    {
        if (format is null) return;
        if (!format.IsValid) throw new ArgumentException("The result format contains incompatible modes or options.", nameof(format));
        RequireTSql("FOR JSON/XML");
        ClauseBreak();
        Keyword($"FOR {format.Kind.ToString().ToUpperInvariant()} {format.Mode.ToString().ToUpperInvariant()}");
        if (format.ElementName is not null) WriteFormatArgument(format.ElementName);
        if (format.HasRoot)
        {
            _builder.Append(", ");
            Keyword("ROOT");
            if (format.Root is not null) WriteFormatArgument(format.Root);
        }
        if (format.IncludeNullValues) { _builder.Append(", "); Keyword("INCLUDE_NULL_VALUES"); }
        if (format.WithoutArrayWrapper) { _builder.Append(", "); Keyword("WITHOUT_ARRAY_WRAPPER"); }
        if (format.Type) { _builder.Append(", "); Keyword("TYPE"); }
    }

    private void WriteFormatArgument(LiteralExpression value)
    {
        _builder.Append('(');
        WriteExpression(value);
        _builder.Append(')');
    }
}

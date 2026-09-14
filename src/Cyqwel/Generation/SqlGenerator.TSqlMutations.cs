using Cyqwel.Ast;
using Cyqwel.Visitors;

namespace Cyqwel.Generation;

public sealed partial class SqlGenerator
{
    private void RequireTSqlMutation(string feature)
    {
        if (!_dialect.ParserOptions.SupportsTSqlExtensions)
            Unsupported($"{_dialect.Name} cannot represent {feature}.");
    }

    private void WriteMutationTop(SqlExpression? top, bool percent)
    {
        if (top is null)
        {
            if (percent) throw new InvalidOperationException("TOP PERCENT requires an expression.");
            return;
        }
        RequireTSqlMutation("mutation TOP");
        Space();
        Keyword("TOP");
        _builder.Append(" (");
        WriteExpression(top);
        _builder.Append(')');
        if (percent)
        {
            Space();
            Keyword("PERCENT");
        }
    }

    private void WriteOutput(TSqlOutputClause? output, bool allowInserted = true, bool allowDeleted = true, bool allowAction = false)
    {
        if (output is null) return;
        RequireTSqlMutation("OUTPUT");
        if (output.Items.Count == 0) throw new InvalidOperationException("OUTPUT requires at least one item.");
        ClauseBreak();
        Keyword("OUTPUT");
        Space();
        WriteSeparated(output.Items, item =>
        {
            if (!allowAction && item.Expression.FindAll<MergeActionExpression>().Any())
                throw new InvalidOperationException("$action is only valid in MERGE OUTPUT.");
            foreach (var column in item.Expression.FindAll<ColumnExpression>())
            {
                if (column.Parts.Count > 1
                    && ((!allowInserted && column.Parts[0].Value.Equals("inserted", StringComparison.OrdinalIgnoreCase))
                        || (!allowDeleted && column.Parts[0].Value.Equals("deleted", StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidOperationException("The OUTPUT pseudo-table is not available for this mutation.");
            }
            if (item.Expression is StarExpression { Qualifier: { Count: 1 } qualifiers }
                && ((!allowInserted && qualifiers[0].Value.Equals("inserted", StringComparison.OrdinalIgnoreCase))
                    || (!allowDeleted && qualifiers[0].Value.Equals("deleted", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("The OUTPUT pseudo-table is not available for this mutation.");
            if (item.Expression is MergeActionExpression) _builder.Append("$action");
            else WriteExpression(item.Expression);
            if (item.Alias is not null)
            {
                Space();
                Keyword("AS");
                Space();
                WriteIdentifier(item.Alias);
            }
        });
        if (output.Into is not null)
        {
            Space();
            Keyword("INTO");
            Space();
            WriteTableName(output.Into);
            if (output.Columns is { Count: > 0 }) WriteIdentifierList(output.Columns);
        }
        else if (output.Columns is not null)
            throw new InvalidOperationException("OUTPUT destination columns require INTO.");
    }

    private void WriteClustering(IndexClustering clustering)
    {
        if (clustering == IndexClustering.Unspecified) return;
        RequireTSqlMutation("rowstore clustering");
        Space();
        Keyword(clustering == IndexClustering.Clustered ? "CLUSTERED" : "NONCLUSTERED");
    }

    private void WriteColumnConstraintName(SqlIdentifier? name)
    {
        if (name is null) return;
        Keyword("CONSTRAINT");
        Space();
        WriteIdentifier(name);
        Space();
    }

    private void WriteKeyColumns(IReadOnlyList<SqlIdentifier> columns, IReadOnlyList<OrderDirection>? directions)
    {
        if (directions is not null && directions.Count != columns.Count)
            throw new InvalidOperationException("Key directions must match the key columns.");
        _builder.Append(" (");
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) _builder.Append(", ");
            WriteIdentifier(columns[i]);
            if (directions?[i] is { } direction && direction != OrderDirection.Unspecified)
            {
                RequireTSqlMutation("ordered key constraints");
                Space();
                Keyword(direction == OrderDirection.Ascending ? "ASC" : "DESC");
            }
        }
        _builder.Append(')');
    }

    private void WriteCreateSchema(CreateSchemaStatement schema)
    {
        RequireTSqlMutation("CREATE SCHEMA");
        Keyword("CREATE SCHEMA");
        Space();
        WriteIdentifier(schema.Name);
    }

    private void WriteCreateInlineFunction(CreateInlineFunctionStatement function)
    {
        RequireTSqlMutation("inline table-valued functions");
        Keyword("CREATE FUNCTION");
        Space();
        WriteTableName(function.Name);
        _builder.Append('(');
        WriteSeparated(function.Parameters, parameter =>
        {
            WriteTSqlVariableName(parameter.Name);
            Space();
            WriteDataType(parameter.DataType);
            if (parameter.Default is not null)
            {
                _builder.Append(" = ");
                WriteExpression(parameter.Default);
            }
            if (parameter.Mode != ProcedureParameterMode.In)
                Unsupported("Inline function parameters cannot have output modes.");
        });
        _builder.Append(')');
        ClauseBreak();
        Keyword("RETURNS TABLE AS RETURN");
        Space();
        WriteNode(function.Query);
    }
}

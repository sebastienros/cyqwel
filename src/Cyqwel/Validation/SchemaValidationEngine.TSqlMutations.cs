using Cyqwel.Ast;
using Cyqwel.Visitors;

namespace Cyqwel.Validation;

internal sealed partial class SchemaValidationEngine
{
    private TableInfo? ResolveMutationTarget(NamedTable target, TableSource? from, IReadOnlyDictionary<string, Projection> ctes)
    {
        if (target.Name.Parts.Count == 1 && from is not null)
        {
            var name = target.Name.Parts[0].Value;
            var source = from.FindAll<NamedTable>().FirstOrDefault(table =>
                string.Equals(table.Alias?.Value, name, StringComparison.OrdinalIgnoreCase));
            if (source is not null) return ResolveMutationTable(source.Name, ctes);
        }
        return ResolveMutationTable(target.Name, ctes);
    }

    private TableInfo? ResolveMutationTable(TableName name, IReadOnlyDictionary<string, Projection> ctes)
    {
        if (!name.IsVariable && name.Parts.Count == 1 && ctes.TryGetValue(name.Parts[0].Value, out var projection))
        {
            var columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in projection.Columns)
            {
                if (column.Name is not null)
                    columns.TryAdd(column.Name, new ColumnInfo(column.Name, column.Type, new SqlColumnSchema(column.Name, column.Type.ToString())));
            }
            return new TableInfo
            {
                Key = name.Parts[0].Value,
                SimpleName = name.Parts[0].Value,
                DisplayName = name.Parts[0].Value,
                Model = new SqlTableSchema(name.Parts[0].Value, columns.Values.Select(column => column.Model).ToArray()),
                Columns = columns,
                ColumnOrder = columns.Keys.ToArray(),
            };
        }
        return ResolveTable(name);
    }

    private IReadOnlyDictionary<string, Projection> BindMutationCtes(
        IReadOnlyList<CommonTableExpression>? expressions,
        IReadOnlyDictionary<string, Projection> inherited)
    {
        if (expressions is null) return inherited;
        var ctes = new Dictionary<string, Projection>(inherited, StringComparer.OrdinalIgnoreCase);
        foreach (var cte in expressions)
        {
            ctes[cte.Name.Value] = cte.Columns is { Count: > 0 }
                ? new Projection(cte.Columns.Select(column => new ProjectedColumn(column.Value, SqlTypeFamily.Unknown)).ToArray())
                : InferRecursiveProjection(cte.Query);
            var projection = ValidateQuery(cte.Query, ctes, null);
            if (cte.Columns is { Count: > 0 })
            {
                if (cte.Columns.Count != projection.Columns.Count)
                    AddSchemaIssue(SqlValidationCodes.CteColumnCountMismatch,
                        $"CTE '{cte.Name.Value}' declares {cte.Columns.Count} columns but projects {projection.Columns.Count}.", cte);
                projection = new Projection(projection.Columns.Select((column, index) => index < cte.Columns.Count
                    ? column with { Name = cte.Columns[index].Value } : column).ToArray());
            }
            ctes[cte.Name.Value] = projection;
        }
        return ctes;
    }

    private Projection ValidateMutationOutput(
        TSqlOutputClause? output,
        TableInfo? target,
        Scope mutationScope,
        IReadOnlyDictionary<string, Projection> ctes,
        bool allowInserted,
        bool allowDeleted,
        bool allowAction = false)
    {
        if (output is null) return Projection.Empty;
        var scope = new Scope(mutationScope);
        if (target is not null)
        {
            var qualifiers = new List<string>();
            if (allowInserted) qualifiers.Add("inserted");
            if (allowDeleted) qualifiers.Add("deleted");
            scope.Add(SourceBinding.FromTable(target, "output", qualifiers));
        }

        var types = new List<SqlTypeFamily>();
        var projected = new List<ProjectedColumn>();
        foreach (var item in output.Items)
        {
            if (!allowAction && item.Expression.FindAll<MergeActionExpression>().Any())
                AddSchemaIssue(SqlValidationCodes.UnknownColumn, "$action is only available in MERGE OUTPUT.", item);
            if (item.Expression is StarExpression star && target is not null
                && star.Qualifier is { Count: 1 } parts && parts[0].Value is { } qualifier
                && ((allowInserted && qualifier.Equals("inserted", StringComparison.OrdinalIgnoreCase))
                    || (allowDeleted && qualifier.Equals("deleted", StringComparison.OrdinalIgnoreCase))))
            {
                types.AddRange(target.ColumnOrder.Select(column => target.Columns[column].Type));
                projected.AddRange(target.ColumnOrder.Select(column => new ProjectedColumn(column, target.Columns[column].Type)));
            }
            else
            {
                var type = ValidateExpression(item.Expression, scope, ctes);
                types.Add(type);
                projected.Add(new ProjectedColumn(item.Alias?.Value
                    ?? (item.Expression as ColumnExpression)?.Parts[^1].Value, type));
            }
        }

        var projection = new Projection(projected);
        if (output.Into is null) return projection;
        var destination = ResolveTable(output.Into);
        if (destination is null) return projection;
        var columns = ResolveColumns(destination, output.Columns, output);
        if (types.Count != columns.Count)
            AddTypeIssue(SqlValidationCodes.InvalidAssignmentType, SqlValidationCodes.ImplicitAssignmentCast,
                $"OUTPUT projects {types.Count} values for {columns.Count} destination columns.", output);
        for (var i = 0; i < Math.Min(types.Count, columns.Count); i++)
            CheckAssignment(columns[i], types[i], output);
        return projection;
    }

    private void ValidateInlineFunction(CreateInlineFunctionStatement function, IReadOnlyDictionary<string, Projection> ctes)
    {
        var outerLocals = _localVariables;
        var outerTables = _tableVariables;
        _localVariables = new(StringComparer.OrdinalIgnoreCase);
        _tableVariables = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var scope = new Scope(null);
            foreach (var parameter in function.Parameters)
            {
                _localVariables[parameter.Name.Value] = SqlTypeFamilies.Classify(parameter.DataType.Name.Value);
                if (parameter.Default is not null)
                    ValidateLocalAssignment(parameter.Name, SqlAssignmentOperator.Assign, parameter.Default, scope, ctes, parameter);
            }
            ValidateQuery(function.Query, ctes, null);
        }
        finally
        {
            _localVariables = outerLocals;
            _tableVariables = outerTables;
        }
    }

    private Projection ValidateTableDefinition(CreateTableStatement table, IReadOnlyDictionary<string, Projection> ctes)
    {
        if (table.AsQuery is not null) ValidateQuery(table.AsQuery, ctes, null);
        var columns = table.Elements.Select(element => element switch
        {
            ColumnDefinition column => new ProjectedColumn(column.Name.Value, SqlTypeFamilies.Classify(column.DataType.Name.Value)),
            ComputedColumnDefinition column => new ProjectedColumn(column.Name.Value, SqlTypeFamily.Unknown),
            _ => null,
        }).OfType<ProjectedColumn>().ToArray();
        var scope = new Scope(null);
        scope.Add(SourceBinding.FromProjection(table.Name.Parts[^1].Value, new Projection(columns), table.Name.Parts[^1].Value));
        var ordinaryNames = table.Elements.OfType<ColumnDefinition>()
            .Select(column => column.Name.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var computedScope = new Scope(null);
        computedScope.Add(SourceBinding.FromProjection(table.Name.Parts[^1].Value,
            new Projection(columns.Where(column => column.Name is not null && ordinaryNames.Contains(column.Name)).ToArray()),
            table.Name.Parts[^1].Value));
        var constantScope = new Scope(null);
        var computedTypes = new Dictionary<string, SqlTypeFamily>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in table.Elements)
        {
            if (element is ColumnDefinition column)
            {
                ValidateExpressionIfPresent(column.Default, constantScope, ctes);
                ValidateExpressionIfPresent(column.IdentitySeed, constantScope, ctes);
                ValidateExpressionIfPresent(column.IdentityIncrement, constantScope, ctes);
                ValidateExpressionIfPresent(column.GeneratedExpression, scope, ctes);
                if (column.Constraints is not null)
                    foreach (var constraint in column.Constraints) ValidateDefinitionConstraint(constraint, scope, ctes);
            }
            else if (element is ComputedColumnDefinition computed)
                computedTypes[computed.Name.Value] = ValidateExpression(computed.Expression, computedScope, ctes);
            else if (element is TableConstraint constraint)
                ValidateDefinitionConstraint(constraint, scope, ctes);
            else if (element is IndexTableElement index)
                foreach (var indexColumn in index.Columns) ValidateExpression(indexColumn.Expression, scope, ctes);
        }
        return new Projection(columns.Select(column => column.Name is not null
            && computedTypes.TryGetValue(column.Name, out var type)
                ? column with { Type = type } : column).ToArray());
    }

    private void ValidateDefinitionConstraint(TableConstraint constraint, Scope scope, IReadOnlyDictionary<string, Projection> ctes)
    {
        IReadOnlyList<SqlIdentifier>? columns = constraint switch
        {
            PrimaryKeyConstraint key => key.Columns,
            UniqueConstraint key => key.Columns,
            ForeignKeyConstraint key => key.Columns,
            DefaultConstraint @default => [@default.Column],
            _ => null,
        };
        if (columns is not null)
            foreach (var column in columns) ValidateExpression(new ColumnExpression([column]), scope, ctes);
        switch (constraint)
        {
            case CheckConstraint check:
                RequirePredicate(check.Condition, ValidateExpression(check.Condition, scope, ctes));
                break;
            case DefaultConstraint @default:
                ValidateExpression(@default.Value, new Scope(null), ctes);
                break;
            case ForeignKeyConstraint foreignKey:
                var reference = ResolveTable(foreignKey.ReferencedTable);
                if (reference is not null) ResolveColumns(reference, foreignKey.ReferencedColumns, foreignKey);
                break;
        }
    }
}

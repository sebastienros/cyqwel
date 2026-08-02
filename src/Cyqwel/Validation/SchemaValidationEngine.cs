using Cyqwel.Ast;

namespace Cyqwel.Validation;

internal sealed class SchemaValidationEngine
{
    private readonly string _sql;
    private readonly SqlSchemaValidationOptions _options;
    private readonly List<SqlValidationDiagnostic> _diagnostics;
    private readonly CatalogIndex _catalog;

    public SchemaValidationEngine(
        string sql,
        SqlSchemaCatalog catalog,
        SqlSchemaValidationOptions options,
        List<SqlValidationDiagnostic> diagnostics)
    {
        _sql = sql;
        _options = options;
        _diagnostics = diagnostics;
        _catalog = new CatalogIndex(catalog);
    }

    public void Validate(SqlDocument document)
    {
        if (_options.CheckReferences) ValidateForeignKeys();

        var ctes = new Dictionary<string, Projection>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in document.Statements)
        {
            switch (statement)
            {
                case SqlQuery query:
                    ValidateQuery(query, ctes, null);
                    break;
                case ExplainStatement explain:
                    ValidateQuery(explain.Query, ctes, null);
                    break;
                case InsertStatement insert:
                    ValidateInsert(insert, ctes);
                    break;
                case UpdateStatement update:
                    ValidateUpdate(update, ctes);
                    break;
                case DeleteStatement delete:
                    ValidateDelete(delete, ctes);
                    break;
                case MergeStatement merge:
                    ValidateMerge(merge, ctes);
                    break;
            }
        }
    }

    private Projection ValidateQuery(
        SqlQuery query,
        IReadOnlyDictionary<string, Projection> inheritedCtes,
        Scope? outerScope)
    {
        var ctes = new Dictionary<string, Projection>(
            inheritedCtes,
            StringComparer.OrdinalIgnoreCase);
        var queryCtes = query switch
        {
            SelectStatement select => select.CommonTableExpressions,
            ValuesStatement values => values.CommonTableExpressions,
            SetOperationStatement set => set.CommonTableExpressions,
            _ => null,
        };
        var recursive = query switch
        {
            SelectStatement select => select.IsRecursive,
            ValuesStatement values => values.IsRecursive,
            SetOperationStatement set => set.IsRecursive,
            _ => false,
        };

        if (queryCtes is not null)
        {
            foreach (var cte in queryCtes)
            {
                if (recursive)
                {
                    ctes[cte.Name.Value] = cte.Columns is { Count: > 0 }
                        ? new Projection(cte.Columns
                            .Select(static column =>
                                new ProjectedColumn(column.Value, SqlTypeFamily.Unknown))
                            .ToArray())
                        : InferRecursiveProjection(cte.Query);
                }

                var projection = ValidateQuery(cte.Query, ctes, outerScope);
                if (cte.Columns is { Count: > 0 })
                {
                    if (cte.Columns.Count != projection.Columns.Count)
                    {
                        AddSchemaIssue(
                            SqlValidationCodes.CteColumnCountMismatch,
                            $"CTE '{cte.Name.Value}' declares {cte.Columns.Count} columns but projects {projection.Columns.Count}.",
                            cte);
                    }

                    projection = new Projection(projection.Columns
                        .Select((column, index) => index < cte.Columns.Count
                            ? column with { Name = cte.Columns[index].Value }
                            : column)
                        .ToArray());
                }

                ctes[cte.Name.Value] = projection;
            }
        }

        return query switch
        {
            SelectStatement select => ValidateSelect(select, ctes, outerScope),
            ValuesStatement values => ValidateValues(values, ctes, outerScope),
            SetOperationStatement set => ValidateSetOperation(set, ctes, outerScope),
            _ => Projection.Empty,
        };
    }

    private Projection ValidateSelect(
        SelectStatement select,
        IReadOnlyDictionary<string, Projection> ctes,
        Scope? outerScope)
    {
        var scope = new Scope(outerScope);
        if (select.From is not null) BindTableSource(select.From, scope, ctes);

        var projectedColumns = new List<ProjectedColumn>();
        foreach (var item in select.Projections)
        {
            if (item.Expression is StarExpression star)
            {
                ExpandStar(star, scope, projectedColumns);
                continue;
            }

            var type = ValidateExpression(item.Expression, scope, ctes);
            projectedColumns.Add(new ProjectedColumn(
                item.Alias?.Value ?? InferProjectionName(item.Expression, projectedColumns.Count),
                type));
        }

        var projection = new Projection(projectedColumns);
        var aliases = BuildAliasMap(projection.Columns);

        if (select.Where is not null)
        {
            RequirePredicate(select.Where, ValidateExpression(select.Where, scope, ctes));
        }

        if (select.GroupBy is not null)
        {
            foreach (var expression in select.GroupBy)
            {
                if (!TryGetAliasType(expression, aliases, out _))
                {
                    ValidateExpression(expression, scope, ctes);
                }
            }
        }

        if (select.Having is not null)
        {
            RequirePredicate(select.Having, ValidateExpression(select.Having, scope, ctes));
        }

        if (select.Windows is not null)
        {
            foreach (var window in select.Windows)
            {
                ValidateExpressions(window.PartitionBy, scope, ctes);
                ValidateOrderBy(window.OrderBy, scope, ctes, aliases);
            }
        }

        if (select.Qualify is not null)
        {
            RequirePredicate(select.Qualify, ValidateExpression(select.Qualify, scope, ctes));
        }

        if (select.ConnectBy is not null)
        {
            if (select.ConnectBy.StartWith is not null)
            {
                RequirePredicate(
                    select.ConnectBy.StartWith,
                    ValidateExpression(select.ConnectBy.StartWith, scope, ctes));
            }

            RequirePredicate(
                select.ConnectBy.Condition,
                ValidateExpression(select.ConnectBy.Condition, scope, ctes));
        }

        ValidateOrderBy(select.OrderBy, scope, ctes, aliases);
        ValidateExpressionIfPresent(select.Top, scope, ctes);
        ValidateExpressionIfPresent(select.Limit, scope, ctes);
        ValidateExpressionIfPresent(select.Offset, scope, ctes);
        return projection;
    }

    private Projection ValidateValues(
        ValuesStatement values,
        IReadOnlyDictionary<string, Projection> ctes,
        Scope? outerScope)
    {
        var scope = new Scope(outerScope);
        var columns = new List<ProjectedColumn>();
        for (var rowIndex = 0; rowIndex < values.Rows.Count; rowIndex++)
        {
            var row = values.Rows[rowIndex];
            if (rowIndex > 0 && row.Count != columns.Count)
            {
                AddTypeIssue(
                    SqlValidationCodes.SetOperationArityMismatch,
                    SqlValidationCodes.SetOperationImplicitCoercion,
                    $"VALUES row {rowIndex + 1} has {row.Count} values; expected {columns.Count}.",
                    values);
            }

            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                var type = ValidateExpression(row[columnIndex], scope, ctes);
                if (rowIndex == 0)
                {
                    columns.Add(new ProjectedColumn($"column{columnIndex + 1}", type));
                }
                else if (columnIndex < columns.Count
                    && !TypesCompatible(columns[columnIndex].Type, type))
                {
                    AddTypeIssue(
                        SqlValidationCodes.SetOperationTypeMismatch,
                        SqlValidationCodes.SetOperationImplicitCoercion,
                        $"VALUES column {columnIndex + 1} mixes {TypeName(columns[columnIndex].Type)} and {TypeName(type)} values.",
                        row[columnIndex]);
                }
            }
        }

        ValidateOrderBy(values.OrderBy, scope, ctes, null);
        ValidateExpressionIfPresent(values.Limit, scope, ctes);
        ValidateExpressionIfPresent(values.Offset, scope, ctes);
        return new Projection(columns);
    }

    private Projection ValidateSetOperation(
        SetOperationStatement set,
        IReadOnlyDictionary<string, Projection> ctes,
        Scope? outerScope)
    {
        var left = ValidateQuery(set.Left, ctes, outerScope);
        var right = ValidateQuery(set.Right, ctes, outerScope);
        if (_options.CheckTypes)
        {
            if (left.Columns.Count != right.Columns.Count)
            {
                AddTypeIssue(
                    SqlValidationCodes.SetOperationArityMismatch,
                    SqlValidationCodes.SetOperationImplicitCoercion,
                    $"Set operation has {left.Columns.Count} columns on the left and {right.Columns.Count} on the right.",
                    set);
            }
            else
            {
                for (var i = 0; i < left.Columns.Count; i++)
                {
                    if (!TypesCompatible(left.Columns[i].Type, right.Columns[i].Type))
                    {
                        AddTypeIssue(
                            SqlValidationCodes.SetOperationTypeMismatch,
                            SqlValidationCodes.SetOperationImplicitCoercion,
                            $"Set operation column {i + 1} has incompatible {TypeName(left.Columns[i].Type)} and {TypeName(right.Columns[i].Type)} types.",
                            set);
                    }
                }
            }
        }

        var scope = new Scope(outerScope);
        var aliases = BuildAliasMap(left.Columns);
        ValidateOrderBy(set.OrderBy, scope, ctes, aliases);
        ValidateExpressionIfPresent(set.Limit, scope, ctes);
        ValidateExpressionIfPresent(set.Offset, scope, ctes);
        return left;
    }

    private IReadOnlyList<SourceBinding> BindTableSource(
        TableSource source,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        switch (source)
        {
            case NamedTable named:
            {
                var binding = BindNamedTable(named, ctes);
                if (!scope.Add(binding))
                {
                    AddSchemaIssue(
                        SqlValidationCodes.UnresolvedReference,
                        $"Duplicate table alias or qualifier '{binding.Name}'.",
                        named);
                }

                return [binding];
            }
            case DerivedTable derived:
            {
                var projection = ValidateQuery(derived.Query, ctes, scope.Parent);
                var binding = SourceBinding.FromProjection(
                    derived.Alias.Value,
                    projection,
                    derived.Alias.Value);
                if (!scope.Add(binding))
                {
                    AddSchemaIssue(
                        SqlValidationCodes.UnresolvedReference,
                        $"Duplicate table alias or qualifier '{binding.Name}'.",
                        derived);
                }

                return [binding];
            }
            case JoinTable join:
            {
                var left = BindTableSource(join.Left, scope, ctes);
                var right = BindTableSource(join.Right, scope, ctes);
                if (join.Condition is not null)
                {
                    RequirePredicate(
                        join.Condition,
                        ValidateExpression(join.Condition, scope, ctes));
                }

                if (join.Using is not null)
                {
                    foreach (var column in join.Using)
                    {
                        ValidateUsingColumn(column, left, right);
                    }
                }

                if (_options.CheckReferences)
                {
                    if (join.Kind == JoinKind.Cross || join.Syntax == JoinSyntax.Comma)
                    {
                        AddWarning(
                            SqlValidationCodes.CartesianJoin,
                            "Cartesian join may produce an unexpectedly large result.",
                            join);
                    }

                    CheckJoinRelationships(join, left, right, scope);
                }

                return [.. left, .. right];
            }
            default:
                return [];
        }
    }

    private SourceBinding BindNamedTable(
        NamedTable named,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var fullName = JoinParts(named.Name.Parts);
        var simpleName = named.Name.Parts[^1].Value;
        if (ctes.TryGetValue(fullName, out var cte)
            || named.Name.Parts.Count == 1
            && ctes.TryGetValue(simpleName, out cte))
        {
            return SourceBinding.FromProjection(
                named.Alias?.Value ?? simpleName,
                cte,
                fullName,
                named.Alias is null ? [simpleName, fullName] : [named.Alias.Value]);
        }

        var table = _catalog.Resolve(named.Name);
        if (table is null)
        {
            AddSchemaIssue(
                SqlValidationCodes.UnknownTable,
                $"Unknown table '{fullName}'.",
                named.Name);
            return SourceBinding.Unknown(
                named.Alias?.Value ?? simpleName,
                named.Alias is null ? [simpleName, fullName] : [named.Alias.Value]);
        }

        return SourceBinding.FromTable(
            table,
            named.Alias?.Value ?? simpleName,
            named.Alias is null ? [simpleName, fullName] : [named.Alias.Value]);
    }

    private void ExpandStar(
        StarExpression star,
        Scope scope,
        List<ProjectedColumn> projectedColumns)
    {
        if (star.Qualifier is null)
        {
            foreach (var source in scope.Sources)
            {
                projectedColumns.AddRange(source.ColumnOrder.Select(column =>
                    new ProjectedColumn(column, source.Columns[column])));
            }

            return;
        }

        var qualifier = JoinParts(star.Qualifier);
        var sourceBinding = scope.FindQualifier(qualifier);
        if (sourceBinding is null)
        {
            AddSchemaIssue(
                SqlValidationCodes.UnresolvedReference,
                $"Unknown table or alias '{qualifier}'.",
                star);
            return;
        }

        projectedColumns.AddRange(sourceBinding.ColumnOrder.Select(column =>
            new ProjectedColumn(column, sourceBinding.Columns[column])));
    }

    private void ValidateUsingColumn(
        SqlIdentifier column,
        IReadOnlyList<SourceBinding> left,
        IReadOnlyList<SourceBinding> right)
    {
        var leftMatches = left.Count(source => source.Columns.ContainsKey(column.Value));
        var rightMatches = right.Count(source => source.Columns.ContainsKey(column.Value));
        if (leftMatches == 0 || rightMatches == 0)
        {
            AddSchemaIssue(
                SqlValidationCodes.UnknownColumn,
                $"JOIN USING column '{column.Value}' must exist on both sides of the join.",
                column);
        }
    }

    private SqlTypeFamily ValidateExpression(
        SqlExpression expression,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        switch (expression)
        {
            case ColumnExpression column:
                return ResolveColumn(column, scope);
            case StarExpression:
                return SqlTypeFamily.Unknown;
            case LiteralExpression literal:
                return LiteralType(literal.Value);
            case ParameterExpression parameter:
                if (parameter.DefaultValue is not null)
                {
                    ValidateExpression(parameter.DefaultValue, scope, ctes);
                }

                return SqlTypeFamily.Unknown;
            case ParenthesizedExpression parenthesized:
                return ValidateExpression(parenthesized.Expression, scope, ctes);
            case UnaryExpression unary:
                return ValidateUnary(unary, scope, ctes);
            case BinaryExpression binary:
                return ValidateBinary(binary, scope, ctes);
            case BetweenExpression between:
                return ValidateBetween(between, scope, ctes);
            case InExpression @in:
                return ValidateIn(@in, scope, ctes);
            case IsNullExpression isNull:
                ValidateExpression(isNull.Expression, scope, ctes);
                return SqlTypeFamily.Boolean;
            case BooleanTestExpression booleanTest:
                RequirePredicate(
                    booleanTest.Expression,
                    ValidateExpression(booleanTest.Expression, scope, ctes));
                return SqlTypeFamily.Boolean;
            case DistinctFromExpression distinct:
            {
                var left = ValidateExpression(distinct.Left, scope, ctes);
                var right = ValidateExpression(distinct.Right, scope, ctes);
                CheckComparison(distinct, left, right);
                return SqlTypeFamily.Boolean;
            }
            case RowExpression row:
                ValidateExpressions(row.Values, scope, ctes);
                return SqlTypeFamily.Struct;
            case DefaultExpression:
                return SqlTypeFamily.Unknown;
            case CollateExpression collate:
                return ValidateExpression(collate.Expression, scope, ctes);
            case ExtractExpression extract:
                ValidateExpression(extract.Expression, scope, ctes);
                return SqlTypeFamily.Numeric;
            case IntervalExpression interval:
                ValidateExpression(interval.Value, scope, ctes);
                return SqlTypeFamily.Interval;
            case SequenceValueExpression:
                return SqlTypeFamily.Integer;
            case FunctionCallExpression function:
                return ValidateFunction(function, scope, ctes);
            case WindowExpression window:
                return ValidateWindow(window, scope, ctes);
            case ExistsExpression exists:
                ValidateQuery(exists.Query, ctes, scope);
                return SqlTypeFamily.Boolean;
            case SubqueryExpression subquery:
            {
                var projection = ValidateQuery(subquery.Query, ctes, scope);
                if (projection.Columns.Count == 1) return projection.Columns[0].Type;
                AddSchemaIssue(
                    SqlValidationCodes.InvalidScalarSubquery,
                    $"Scalar subquery projects {projection.Columns.Count} columns; expected one.",
                    subquery);
                return SqlTypeFamily.Unknown;
            }
            case CaseExpression @case:
                return ValidateCase(@case, scope, ctes);
            case CastExpression cast:
                ValidateExpression(cast.Expression, scope, ctes);
                return SqlTypeFamilies.Classify(cast.DataType.Name.Value);
            case TryCastExpression cast:
                ValidateExpression(cast.Expression, scope, ctes);
                return SqlTypeFamilies.Classify(cast.DataType.Name.Value);
            default:
                return SqlTypeFamily.Unknown;
        }
    }

    private SqlTypeFamily ResolveColumn(ColumnExpression column, Scope scope)
    {
        var name = column.Parts[^1].Value;
        if (column.Parts.Count > 1)
        {
            var qualifier = JoinParts(column.Parts.Take(column.Parts.Count - 1));
            var source = scope.FindQualifier(qualifier);
            if (source is null)
            {
                AddSchemaIssue(
                    SqlValidationCodes.UnresolvedReference,
                    $"Unknown table or alias '{qualifier}' in column reference '{JoinParts(column.Parts)}'.",
                    column);
                return SqlTypeFamily.Unknown;
            }

            if (source.Columns.TryGetValue(name, out var qualifiedType)) return qualifiedType;
            if (!source.IsUnknown)
            {
                AddSchemaIssue(
                    SqlValidationCodes.UnknownColumn,
                    $"Unknown column '{name}' on '{qualifier}'.",
                    column);
            }

            return SqlTypeFamily.Unknown;
        }

        var matches = scope.FindColumn(name);
        if (matches.Count == 0 && scope.Sources.Count == 0 && scope.Parent is null)
        {
            matches = _catalog.FindColumn(name)
                .Select(static match => new ColumnMatch(
                    SourceBinding.FromTable(match.Table, match.Table.SimpleName, [match.Table.SimpleName]),
                    match.Type))
                .ToArray();
        }

        if (matches.Count == 0)
        {
            if (!scope.HasUnknownSource)
            {
                AddSchemaIssue(
                    SqlValidationCodes.UnknownColumn,
                    $"Unknown column '{name}'.",
                    column);
            }

            return SqlTypeFamily.Unknown;
        }

        if (matches.Count > 1 && _options.CheckReferences)
        {
            AddSchemaIssue(
                SqlValidationCodes.AmbiguousColumnReference,
                $"Column reference '{name}' is ambiguous.",
                column);
        }

        return matches[0].Type;
    }

    private SqlTypeFamily ValidateUnary(
        UnaryExpression unary,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var operand = ValidateExpression(unary.Operand, scope, ctes);
        if (!_options.CheckTypes) return operand;

        if (unary.Operator == UnaryOperator.Not)
        {
            RequirePredicate(unary.Operand, operand);
            return SqlTypeFamily.Boolean;
        }

        if (unary.Operator is UnaryOperator.Plus or UnaryOperator.Minus
            && !IsNumericOrUnknown(operand))
        {
            AddTypeIssue(
                SqlValidationCodes.InvalidArithmeticType,
                SqlValidationCodes.ImplicitArithmeticCast,
                $"Unary {unary.Operator} requires a numeric operand, not {TypeName(operand)}.",
                unary);
        }

        return operand;
    }

    private SqlTypeFamily ValidateBinary(
        BinaryExpression binary,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var left = ValidateExpression(binary.Left, scope, ctes);
        var right = ValidateExpression(binary.Right, scope, ctes);

        if (binary.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            RequirePredicate(binary.Left, left);
            RequirePredicate(binary.Right, right);
            return SqlTypeFamily.Boolean;
        }

        if (binary.Operator is BinaryOperator.Equal
            or BinaryOperator.NotEqual
            or BinaryOperator.GreaterThan
            or BinaryOperator.GreaterThanOrEqual
            or BinaryOperator.LessThan
            or BinaryOperator.LessThanOrEqual
            or BinaryOperator.Like
            or BinaryOperator.NotLike
            or BinaryOperator.ILike
            or BinaryOperator.NotILike)
        {
            CheckComparison(binary, left, right);
            return SqlTypeFamily.Boolean;
        }

        if (!_options.CheckTypes) return CommonType(left, right);
        if (binary.Operator == BinaryOperator.Concatenate)
        {
            if (!IsStringOrBinaryOrUnknown(left) || !IsStringOrBinaryOrUnknown(right))
            {
                AddTypeIssue(
                    SqlValidationCodes.InvalidArithmeticType,
                    SqlValidationCodes.ImplicitArithmeticCast,
                    $"Concatenation requires string or binary operands, not {TypeName(left)} and {TypeName(right)}.",
                    binary);
            }

            return left == SqlTypeFamily.Binary && right == SqlTypeFamily.Binary
                ? SqlTypeFamily.Binary
                : SqlTypeFamily.String;
        }

        if (binary.Operator is BinaryOperator.Add or BinaryOperator.Subtract)
        {
            if (left == SqlTypeFamily.Interval && right == SqlTypeFamily.Interval)
            {
                return SqlTypeFamily.Interval;
            }

            if (IsTemporal(left) && right == SqlTypeFamily.Interval) return left;
            if (binary.Operator == BinaryOperator.Add
                && left == SqlTypeFamily.Interval
                && IsTemporal(right))
            {
                return right;
            }

            if (binary.Operator == BinaryOperator.Subtract
                && IsTemporal(left)
                && IsTemporal(right))
            {
                return SqlTypeFamily.Interval;
            }
        }

        if (!IsNumericOrUnknown(left) || !IsNumericOrUnknown(right))
        {
            AddTypeIssue(
                SqlValidationCodes.InvalidArithmeticType,
                SqlValidationCodes.ImplicitArithmeticCast,
                $"Arithmetic requires numeric operands, not {TypeName(left)} and {TypeName(right)}.",
                binary);
        }

        return CommonType(left, right);
    }

    private SqlTypeFamily ValidateBetween(
        BetweenExpression between,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var value = ValidateExpression(between.Expression, scope, ctes);
        var lower = ValidateExpression(between.Lower, scope, ctes);
        var upper = ValidateExpression(between.Upper, scope, ctes);
        CheckComparison(between, value, lower);
        CheckComparison(between, value, upper);
        return SqlTypeFamily.Boolean;
    }

    private SqlTypeFamily ValidateIn(
        InExpression @in,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var valueType = ValidateExpression(@in.Expression, scope, ctes);
        foreach (var value in @in.Values)
        {
            CheckComparison(@in, valueType, ValidateExpression(value, scope, ctes));
        }

        if (@in.Query is not null)
        {
            var projection = ValidateQuery(@in.Query, ctes, scope);
            if (projection.Columns.Count == 1)
            {
                CheckComparison(@in, valueType, projection.Columns[0].Type);
            }
            else if (_options.CheckTypes)
            {
                AddTypeIssue(
                    SqlValidationCodes.SetOperationArityMismatch,
                    SqlValidationCodes.SetOperationImplicitCoercion,
                    $"IN subquery projects {projection.Columns.Count} columns; expected one.",
                    @in);
            }
        }

        return SqlTypeFamily.Boolean;
    }

    private SqlTypeFamily ValidateFunction(
        FunctionCallExpression function,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var argumentTypes = function.Arguments
            .Select(argument => ValidateExpression(argument, scope, ctes))
            .ToArray();
        if (function.Filter is not null)
        {
            RequirePredicate(
                function.Filter,
                ValidateExpression(function.Filter, scope, ctes));
        }

        if (function.WithinGroup is not null)
        {
            ValidateOrderBy(function.WithinGroup, scope, ctes, null);
        }

        var name = function.Name.Value.ToUpperInvariant();
        return name switch
        {
            "COUNT" => SqlTypeFamily.Integer,
            "SUM" or "AVG" => ValidateFunctionArguments(
                function,
                argumentTypes,
                1,
                static type => IsNumericOrUnknown(type),
                SqlTypeFamily.Numeric),
            "MIN" or "MAX" => ValidateFunctionArguments(
                function,
                argumentTypes,
                1,
                static _ => true,
                argumentTypes.FirstOrDefault()),
            "ABS" => ValidateFunctionArguments(
                function,
                argumentTypes,
                1,
                static type => IsNumericOrUnknown(type),
                argumentTypes.FirstOrDefault()),
            "LOWER" or "UPPER" or "TRIM" or "LTRIM" or "RTRIM" => ValidateFunctionArguments(
                function,
                argumentTypes,
                1,
                static type => IsStringOrUnknown(type),
                SqlTypeFamily.String),
            "LENGTH" or "CHAR_LENGTH" => ValidateFunctionArguments(
                function,
                argumentTypes,
                1,
                static type => IsStringOrBinaryOrUnknown(type),
                SqlTypeFamily.Integer),
            "COALESCE" => ValidateCoalesce(function, argumentTypes),
            _ => SqlTypeFamily.Unknown,
        };
    }

    private SqlTypeFamily ValidateFunctionArguments(
        FunctionCallExpression function,
        IReadOnlyList<SqlTypeFamily> arguments,
        int expectedArity,
        Func<SqlTypeFamily, bool> accepts,
        SqlTypeFamily result)
    {
        if (!_options.CheckTypes) return result;
        if (arguments.Count != expectedArity)
        {
            AddTypeIssue(
                SqlValidationCodes.InvalidFunctionArity,
                SqlValidationCodes.FunctionArgumentCoercion,
                $"Function {function.Name.Value} expects {expectedArity} argument(s), not {arguments.Count}.",
                function);
            return result;
        }

        if (!accepts(arguments[0]))
        {
            AddTypeIssue(
                SqlValidationCodes.InvalidFunctionArgumentType,
                SqlValidationCodes.FunctionArgumentCoercion,
                $"Function {function.Name.Value} does not accept a {TypeName(arguments[0])} argument.",
                function.Arguments[0]);
        }

        return result;
    }

    private SqlTypeFamily ValidateCoalesce(
        FunctionCallExpression function,
        IReadOnlyList<SqlTypeFamily> arguments)
    {
        if (_options.CheckTypes && arguments.Count == 0)
        {
            AddTypeIssue(
                SqlValidationCodes.InvalidFunctionArity,
                SqlValidationCodes.FunctionArgumentCoercion,
                "Function COALESCE expects at least one argument.",
                function);
            return SqlTypeFamily.Unknown;
        }

        var result = arguments.FirstOrDefault(static type => type != SqlTypeFamily.Unknown);
        if (_options.CheckTypes)
        {
            foreach (var argument in arguments)
            {
                if (!TypesCompatible(result, argument))
                {
                    AddTypeIssue(
                        SqlValidationCodes.InvalidFunctionArgumentType,
                        SqlValidationCodes.FunctionArgumentCoercion,
                        $"COALESCE arguments have incompatible {TypeName(result)} and {TypeName(argument)} types.",
                        function);
                    break;
                }
            }
        }

        return result;
    }

    private SqlTypeFamily ValidateWindow(
        WindowExpression window,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var result = ValidateExpression(window.Expression, scope, ctes);
        ValidateExpressions(window.PartitionBy, scope, ctes);
        ValidateOrderBy(window.OrderBy, scope, ctes, null);
        if (window.Frame is not null)
        {
            ValidateExpressionIfPresent(window.Frame.Start.Offset, scope, ctes);
            ValidateExpressionIfPresent(window.Frame.End?.Offset, scope, ctes);
        }

        return result;
    }

    private SqlTypeFamily ValidateCase(
        CaseExpression @case,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var operandType = @case.Operand is null
            ? SqlTypeFamily.Unknown
            : ValidateExpression(@case.Operand, scope, ctes);
        var result = SqlTypeFamily.Unknown;
        foreach (var when in @case.Whens)
        {
            var condition = ValidateExpression(when.Condition, scope, ctes);
            if (@case.Operand is null)
            {
                RequirePredicate(when.Condition, condition);
            }
            else
            {
                CheckComparison(when.Condition, operandType, condition);
            }

            result = CommonType(result, ValidateExpression(when.Result, scope, ctes));
        }

        if (@case.Else is not null)
        {
            result = CommonType(result, ValidateExpression(@case.Else, scope, ctes));
        }

        return result;
    }

    private void CheckComparison(SqlNode node, SqlTypeFamily left, SqlTypeFamily right)
    {
        if (!_options.CheckTypes || TypesCompatible(left, right)) return;
        AddTypeIssue(
            SqlValidationCodes.IncompatibleComparisonTypes,
            SqlValidationCodes.ImplicitComparisonCast,
            $"Comparison has incompatible {TypeName(left)} and {TypeName(right)} operands.",
            node);
    }

    private void RequirePredicate(SqlNode node, SqlTypeFamily type)
    {
        if (!_options.CheckTypes
            || type is SqlTypeFamily.Boolean or SqlTypeFamily.Unknown)
        {
            return;
        }

        AddTypeIssue(
            SqlValidationCodes.InvalidPredicateType,
            SqlValidationCodes.PredicateTypeConcern,
            $"Predicate expression has type {TypeName(type)} instead of boolean.",
            node);
    }

    private void ValidateInsert(
        InsertStatement insert,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var target = ResolveTable(insert.Target);
        if (target is null) return;

        var targetColumns = ResolveInsertColumns(insert, target);
        var scope = new Scope(null);
        if (insert.Values is not null)
        {
            foreach (var row in insert.Values)
            {
                if (_options.CheckTypes && row.Count != targetColumns.Count)
                {
                    AddTypeIssue(
                        SqlValidationCodes.InvalidAssignmentType,
                        SqlValidationCodes.ImplicitAssignmentCast,
                        $"INSERT row has {row.Count} values for {targetColumns.Count} target columns.",
                        insert);
                }

                for (var i = 0; i < row.Count; i++)
                {
                    var valueType = ValidateExpression(row[i], scope, ctes);
                    if (i < targetColumns.Count)
                    {
                        CheckAssignment(targetColumns[i], valueType, row[i]);
                    }
                }
            }
        }

        if (insert.Source is not null)
        {
            var source = ValidateQuery(insert.Source, ctes, null);
            if (_options.CheckTypes && source.Columns.Count != targetColumns.Count)
            {
                AddTypeIssue(
                    SqlValidationCodes.InvalidAssignmentType,
                    SqlValidationCodes.ImplicitAssignmentCast,
                    $"INSERT source projects {source.Columns.Count} values for {targetColumns.Count} target columns.",
                    insert.Source);
            }

            for (var i = 0; i < source.Columns.Count && i < targetColumns.Count; i++)
            {
                CheckAssignment(targetColumns[i], source.Columns[i].Type, insert.Source);
            }
        }

        var returningScope = new Scope(null);
        returningScope.Add(SourceBinding.FromTable(target, target.SimpleName, [target.SimpleName]));
        ValidateExpressions(insert.Returning, returningScope, ctes);
        ValidateExpressions(insert.ReturningInto, returningScope, ctes);
    }

    private IReadOnlyList<ColumnInfo> ResolveInsertColumns(
        InsertStatement insert,
        TableInfo target) =>
        ResolveColumns(target, insert.Columns, insert);

    private IReadOnlyList<ColumnInfo> ResolveColumns(
        TableInfo target,
        IReadOnlyList<SqlIdentifier>? identifiers,
        SqlNode node)
    {
        if (identifiers is null) return target.ColumnOrder
            .Select(column => target.Columns[column])
            .ToArray();

        var columns = new List<ColumnInfo>();
        foreach (var identifier in identifiers)
        {
            if (target.Columns.TryGetValue(identifier.Value, out var column))
            {
                columns.Add(column);
            }
            else
            {
                AddSchemaIssue(
                    SqlValidationCodes.UnknownColumn,
                    $"Unknown target column '{identifier.Value}' on '{target.DisplayName}'.",
                    identifier.Span.IsEmpty ? node : identifier);
            }
        }

        return columns;
    }

    private void ValidateUpdate(
        UpdateStatement update,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var target = ResolveTable(update.Target.Name);
        var scope = new Scope(null);
        if (target is not null)
        {
            scope.Add(SourceBinding.FromTable(
                target,
                update.Target.Alias?.Value ?? target.SimpleName,
                update.Target.Alias is null
                    ? [target.SimpleName, target.DisplayName]
                    : [update.Target.Alias.Value]));
        }

        if (update.From is not null) BindTableSource(update.From, scope, ctes);
        foreach (var assignment in update.Assignments)
        {
            var valueType = ValidateExpression(assignment.Value, scope, ctes);
            if (target is null) continue;

            var targetName = assignment.Column.Parts[^1].Value;
            if (target.Columns.TryGetValue(targetName, out var targetColumn))
            {
                CheckAssignment(targetColumn, valueType, assignment);
            }
            else
            {
                AddSchemaIssue(
                    SqlValidationCodes.UnknownColumn,
                    $"Unknown target column '{targetName}' on '{target.DisplayName}'.",
                    assignment.Column);
            }
        }

        if (update.Where is not null)
        {
            RequirePredicate(update.Where, ValidateExpression(update.Where, scope, ctes));
        }

        ValidateExpressions(update.Returning, scope, ctes);
        ValidateExpressions(update.ReturningInto, scope, ctes);
    }

    private void ValidateDelete(
        DeleteStatement delete,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var target = ResolveTable(delete.Target.Name);
        var scope = new Scope(null);
        if (target is not null)
        {
            scope.Add(SourceBinding.FromTable(
                target,
                delete.Target.Alias?.Value ?? target.SimpleName,
                delete.Target.Alias is null
                    ? [target.SimpleName, target.DisplayName]
                    : [delete.Target.Alias.Value]));
        }

        if (delete.Using is not null) BindTableSource(delete.Using, scope, ctes);
        if (delete.Where is not null)
        {
            RequirePredicate(delete.Where, ValidateExpression(delete.Where, scope, ctes));
        }

        ValidateExpressions(delete.Returning, scope, ctes);
        ValidateExpressions(delete.ReturningInto, scope, ctes);
    }

    private void ValidateMerge(
        MergeStatement merge,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        var scope = new Scope(null);
        var target = ResolveTable(merge.Target.Name);
        if (target is not null)
        {
            scope.Add(SourceBinding.FromTable(
                target,
                merge.Target.Alias?.Value ?? target.SimpleName,
                merge.Target.Alias is null
                    ? [target.SimpleName, target.DisplayName]
                    : [merge.Target.Alias.Value]));
        }

        BindTableSource(merge.Source, scope, ctes);
        RequirePredicate(merge.Condition, ValidateExpression(merge.Condition, scope, ctes));
        foreach (var when in merge.WhenClauses)
        {
            if (when.Condition is not null)
            {
                RequirePredicate(
                    when.Condition,
                    ValidateExpression(when.Condition, scope, ctes));
            }

            if (when.Action is MergeUpdateAction update)
            {
                foreach (var assignment in update.Assignments)
                {
                    var valueType = ValidateExpression(assignment.Value, scope, ctes);
                    if (target is null) continue;

                    var targetName = assignment.Column.Parts[^1].Value;
                    if (target.Columns.TryGetValue(targetName, out var targetColumn))
                    {
                        CheckAssignment(targetColumn, valueType, assignment);
                    }
                    else
                    {
                        AddSchemaIssue(
                            SqlValidationCodes.UnknownColumn,
                            $"Unknown target column '{targetName}' on '{target.DisplayName}'.",
                            assignment.Column);
                    }
                }
            }
            else if (when.Action is MergeInsertAction insert)
            {
                if (target is null)
                {
                    ValidateExpressions(insert.Values, scope, ctes);
                    continue;
                }

                var targetColumns = ResolveColumns(
                    target,
                    insert.Columns,
                    insert);
                if (_options.CheckTypes && insert.Values.Count != targetColumns.Count)
                {
                    AddTypeIssue(
                        SqlValidationCodes.InvalidAssignmentType,
                        SqlValidationCodes.ImplicitAssignmentCast,
                        $"MERGE INSERT has {insert.Values.Count} values for {targetColumns.Count} target columns.",
                        insert);
                }

                for (var i = 0; i < insert.Values.Count; i++)
                {
                    var valueType = ValidateExpression(insert.Values[i], scope, ctes);
                    if (i < targetColumns.Count)
                    {
                        CheckAssignment(targetColumns[i], valueType, insert.Values[i]);
                    }
                }
            }
        }

        ValidateExpressions(merge.Returning, scope, ctes);
        ValidateExpressions(merge.ReturningInto, scope, ctes);
    }

    private TableInfo? ResolveTable(TableName name)
    {
        var table = _catalog.Resolve(name);
        if (table is null)
        {
            AddSchemaIssue(
                SqlValidationCodes.UnknownTable,
                $"Unknown table '{JoinParts(name.Parts)}'.",
                name);
        }

        return table;
    }

    private void CheckAssignment(ColumnInfo target, SqlTypeFamily source, SqlNode node)
    {
        if (!_options.CheckTypes || TypesCompatible(target.Type, source)) return;
        AddTypeIssue(
            SqlValidationCodes.InvalidAssignmentType,
            SqlValidationCodes.ImplicitAssignmentCast,
            $"Cannot assign {TypeName(source)} to {TypeName(target.Type)} column '{target.Name}'.",
            node);
    }

    private void ValidateForeignKeys()
    {
        foreach (var table in _catalog.Tables)
        {
            foreach (var column in table.Model.Columns)
            {
                if (column.References is null) continue;
                ValidateForeignKey(
                    table,
                    [column.Name],
                    column.References.Table,
                    column.References.Schema,
                    [column.References.Column],
                    column.Name);
            }

            if (table.Model.ForeignKeys is null) continue;
            foreach (var foreignKey in table.Model.ForeignKeys)
            {
                ValidateForeignKey(
                    table,
                    foreignKey.Columns,
                    foreignKey.References.Table,
                    foreignKey.References.Schema,
                    foreignKey.References.Columns,
                    foreignKey.Name ?? "<unnamed>");
            }
        }
    }

    private void ValidateForeignKey(
        TableInfo source,
        IReadOnlyList<string> sourceColumns,
        string targetTable,
        string? targetSchema,
        IReadOnlyList<string> targetColumns,
        string name)
    {
        if (sourceColumns.Count == 0
            || sourceColumns.Count != targetColumns.Count)
        {
            AddReferenceIssue(
                $"Foreign key '{name}' on '{source.DisplayName}' has mismatched source and target column counts.");
            return;
        }

        var target = _catalog.Resolve(targetTable, targetSchema);
        if (target is null)
        {
            AddReferenceIssue(
                $"Foreign key '{name}' on '{source.DisplayName}' references unknown table '{targetTable}'.");
            return;
        }

        for (var i = 0; i < sourceColumns.Count; i++)
        {
            if (!source.Columns.TryGetValue(sourceColumns[i], out var sourceColumn))
            {
                AddReferenceIssue(
                    $"Foreign key '{name}' references unknown source column '{source.DisplayName}.{sourceColumns[i]}'.");
                continue;
            }

            if (!target.Columns.TryGetValue(targetColumns[i], out var targetColumn))
            {
                AddReferenceIssue(
                    $"Foreign key '{name}' references unknown target column '{target.DisplayName}.{targetColumns[i]}'.");
                continue;
            }

            if (!TypesCompatible(sourceColumn.Type, targetColumn.Type))
            {
                AddReferenceIssue(
                    $"Foreign key '{name}' has incompatible {TypeName(sourceColumn.Type)} and {TypeName(targetColumn.Type)} column types.");
            }

        }

        if (!target.IsUniqueKey(targetColumns))
        {
            AddWarning(
                SqlValidationCodes.WeakReferenceIntegrity,
                $"Referenced columns on '{target.DisplayName}' are not marked as a primary or unique key.",
                null);
        }
    }

    private void CheckJoinRelationships(
        JoinTable join,
        IReadOnlyList<SourceBinding> left,
        IReadOnlyList<SourceBinding> right,
        Scope scope)
    {
        var relationships = _catalog.Relationships
            .Where(relationship =>
                ContainsTable(left, relationship.Source)
                && ContainsTable(right, relationship.Target)
                || ContainsTable(left, relationship.Target)
                && ContainsTable(right, relationship.Source))
            .ToArray();
        if (relationships.Length == 0 || join.Kind == JoinKind.Cross) return;

        var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (join.Condition is not null)
        {
            CollectEqualityPairs(join.Condition, scope, pairs);
        }

        if (join.Using is not null)
        {
            foreach (var column in join.Using)
            {
                foreach (var leftSource in left.Where(source =>
                    source.Table is not null && source.Columns.ContainsKey(column.Value)))
                {
                    foreach (var rightSource in right.Where(source =>
                        source.Table is not null && source.Columns.ContainsKey(column.Value)))
                    {
                        pairs.Add(PairKey(
                            leftSource.Table!,
                            column.Value,
                            rightSource.Table!,
                            column.Value));
                    }
                }
            }
        }

        var usesRelationship = relationships.Any(relationship =>
            relationship.SourceColumns
                .Zip(relationship.TargetColumns)
                .All(columns => pairs.Contains(PairKey(
                    relationship.Source,
                    columns.First,
                    relationship.Target,
                    columns.Second))));
        if (!usesRelationship)
        {
            AddWarning(
                SqlValidationCodes.JoinNotUsingDeclaredReference,
                "Join condition does not use the declared relationship between these tables.",
                join);
        }
    }

    private static bool ContainsTable(
        IReadOnlyList<SourceBinding> bindings,
        TableInfo table) =>
        bindings.Any(binding => ReferenceEquals(binding.Table, table));

    private void CollectEqualityPairs(
        SqlExpression expression,
        Scope scope,
        HashSet<string> pairs)
    {
        if (expression is BinaryExpression { Operator: BinaryOperator.And } and)
        {
            CollectEqualityPairs(and.Left, scope, pairs);
            CollectEqualityPairs(and.Right, scope, pairs);
            return;
        }

        if (expression is not BinaryExpression
            {
                Operator: BinaryOperator.Equal,
                Left: ColumnExpression left,
                Right: ColumnExpression right,
            })
        {
            return;
        }

        if (TryResolveColumnQuiet(left, scope, out var leftSource, out var leftName)
            && TryResolveColumnQuiet(right, scope, out var rightSource, out var rightName)
            && leftSource.Table is not null
            && rightSource.Table is not null)
        {
            pairs.Add(PairKey(leftSource.Table, leftName, rightSource.Table, rightName));
        }
    }

    private static bool TryResolveColumnQuiet(
        ColumnExpression column,
        Scope scope,
        out SourceBinding source,
        out string name)
    {
        name = column.Parts[^1].Value;
        if (column.Parts.Count > 1)
        {
            var qualifier = JoinParts(column.Parts.Take(column.Parts.Count - 1));
            source = scope.FindQualifier(qualifier)!;
            return source is not null && source.Columns.ContainsKey(name);
        }

        var matches = scope.FindColumn(name);
        if (matches.Count == 1)
        {
            source = matches[0].Source;
            return true;
        }

        source = null!;
        return false;
    }

    private static string PairKey(
        TableInfo firstTable,
        string firstColumn,
        TableInfo secondTable,
        string secondColumn)
    {
        var first = $"{firstTable.Key}.{firstColumn}";
        var second = $"{secondTable.Key}.{secondColumn}";
        return string.Compare(first, second, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{first}={second}"
            : $"{second}={first}";
    }

    private void ValidateOrderBy(
        IReadOnlyList<OrderByItem>? orderBy,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes,
        IReadOnlyDictionary<string, SqlTypeFamily>? aliases)
    {
        if (orderBy is null) return;
        foreach (var item in orderBy)
        {
            if (aliases is null || !TryGetAliasType(item.Expression, aliases, out _))
            {
                ValidateExpression(item.Expression, scope, ctes);
            }
        }
    }

    private static bool TryGetAliasType(
        SqlExpression expression,
        IReadOnlyDictionary<string, SqlTypeFamily> aliases,
        out SqlTypeFamily type)
    {
        if (expression is ColumnExpression { Parts.Count: 1 } column
            && aliases.TryGetValue(column.Parts[0].Value, out type))
        {
            return true;
        }

        type = SqlTypeFamily.Unknown;
        return false;
    }

    private static IReadOnlyDictionary<string, SqlTypeFamily> BuildAliasMap(
        IReadOnlyList<ProjectedColumn> columns)
    {
        var aliases = new Dictionary<string, SqlTypeFamily>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            if (column.Name is not null) aliases.TryAdd(column.Name, column.Type);
        }

        return aliases;
    }

    private void ValidateExpressions(
        IEnumerable<SqlExpression>? expressions,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        if (expressions is null) return;
        foreach (var expression in expressions)
        {
            ValidateExpression(expression, scope, ctes);
        }
    }

    private void ValidateExpressionIfPresent(
        SqlExpression? expression,
        Scope scope,
        IReadOnlyDictionary<string, Projection> ctes)
    {
        if (expression is not null) ValidateExpression(expression, scope, ctes);
    }

    private void AddSchemaIssue(string code, string message, SqlNode? node)
    {
        _diagnostics.Add(new SqlValidationDiagnostic(
            _options.Strict
                ? SqlValidationSeverity.Error
                : SqlValidationSeverity.Warning,
            code,
            message,
            node is null ? null : SqlValidator.CreateLocation(_sql, node)));
    }

    private void AddTypeIssue(
        string errorCode,
        string warningCode,
        string message,
        SqlNode node)
    {
        if (!_options.CheckTypes) return;
        _diagnostics.Add(new SqlValidationDiagnostic(
            _options.Strict
                ? SqlValidationSeverity.Error
                : SqlValidationSeverity.Warning,
            _options.Strict ? errorCode : warningCode,
            message,
            SqlValidator.CreateLocation(_sql, node)));
    }

    private void AddReferenceIssue(string message)
    {
        _diagnostics.Add(new SqlValidationDiagnostic(
            _options.Strict
                ? SqlValidationSeverity.Error
                : SqlValidationSeverity.Warning,
            _options.Strict
                ? SqlValidationCodes.InvalidForeignKeyReference
                : SqlValidationCodes.WeakReferenceIntegrity,
            message));
    }

    private void AddWarning(string code, string message, SqlNode? node)
    {
        _diagnostics.Add(new SqlValidationDiagnostic(
            SqlValidationSeverity.Warning,
            code,
            message,
            node is null ? null : SqlValidator.CreateLocation(_sql, node)));
    }

    private static string? InferProjectionName(SqlExpression expression, int index) =>
        expression switch
        {
            ColumnExpression column => column.Parts[^1].Value,
            FunctionCallExpression function => function.Name.Value,
            _ => $"column{index + 1}",
        };

    private static Projection InferRecursiveProjection(SqlQuery query)
    {
        var columns = query switch
        {
            SelectStatement select => select.Projections
                .Select((projection, index) => new ProjectedColumn(
                    projection.Alias?.Value
                        ?? InferProjectionName(projection.Expression, index),
                    SqlTypeFamily.Unknown))
                .ToArray(),
            ValuesStatement values when values.Rows.Count > 0 => values.Rows[0]
                .Select((_, index) =>
                    new ProjectedColumn($"column{index + 1}", SqlTypeFamily.Unknown))
                .ToArray(),
            SetOperationStatement set => InferRecursiveProjection(set.Left).Columns,
            _ => Array.Empty<ProjectedColumn>(),
        };
        return new Projection(columns);
    }

    private static string JoinParts(IEnumerable<SqlIdentifier> parts) =>
        string.Join('.', parts.Select(static part => part.Value));

    private static SqlTypeFamily LiteralType(object? value) => value switch
    {
        null => SqlTypeFamily.Unknown,
        bool => SqlTypeFamily.Boolean,
        sbyte or byte or short or ushort or int or uint or long or ulong =>
            SqlTypeFamily.Integer,
        float or double or decimal => SqlTypeFamily.Numeric,
        string or char => SqlTypeFamily.String,
        byte[] => SqlTypeFamily.Binary,
        DateTime or DateTimeOffset => SqlTypeFamily.Timestamp,
        DateOnly => SqlTypeFamily.Date,
        TimeOnly or TimeSpan => SqlTypeFamily.Time,
        Guid => SqlTypeFamily.Uuid,
        _ => SqlTypeFamily.Unknown,
    };

    private static bool TypesCompatible(SqlTypeFamily left, SqlTypeFamily right)
    {
        if (left == SqlTypeFamily.Unknown || right == SqlTypeFamily.Unknown) return true;
        if (left == right) return true;
        if (IsNumeric(left) && IsNumeric(right)) return true;
        if (IsTemporal(left) && IsTemporal(right)) return true;
        return false;
    }

    private static SqlTypeFamily CommonType(SqlTypeFamily left, SqlTypeFamily right)
    {
        if (left == SqlTypeFamily.Unknown) return right;
        if (right == SqlTypeFamily.Unknown) return left;
        if (left == right) return left;
        if (IsNumeric(left) && IsNumeric(right)) return SqlTypeFamily.Numeric;
        if (IsTemporal(left) && IsTemporal(right)) return SqlTypeFamily.Timestamp;
        return SqlTypeFamily.Unknown;
    }

    private static bool IsNumeric(SqlTypeFamily type) =>
        type is SqlTypeFamily.Integer or SqlTypeFamily.Numeric;

    private static bool IsTemporal(SqlTypeFamily type) =>
        type is SqlTypeFamily.Date
            or SqlTypeFamily.Time
            or SqlTypeFamily.Timestamp;

    private static bool IsNumericOrUnknown(SqlTypeFamily type) =>
        type == SqlTypeFamily.Unknown || IsNumeric(type);

    private static bool IsStringOrUnknown(SqlTypeFamily type) =>
        type is SqlTypeFamily.Unknown or SqlTypeFamily.String;

    private static bool IsStringOrBinaryOrUnknown(SqlTypeFamily type) =>
        type is SqlTypeFamily.Unknown or SqlTypeFamily.String or SqlTypeFamily.Binary;

    private static string TypeName(SqlTypeFamily type) => type.ToString().ToLowerInvariant();

    private sealed record ProjectedColumn(string? Name, SqlTypeFamily Type);

    private sealed record Projection(IReadOnlyList<ProjectedColumn> Columns)
    {
        public static Projection Empty { get; } = new(Array.Empty<ProjectedColumn>());
    }

    private sealed record ColumnMatch(SourceBinding Source, SqlTypeFamily Type);

    private sealed class Scope(Scope? parent)
    {
        private readonly Dictionary<string, SourceBinding> _qualifiers =
            new(StringComparer.OrdinalIgnoreCase);

        public Scope? Parent { get; } = parent;

        public List<SourceBinding> Sources { get; } = [];

        public bool HasUnknownSource =>
            Sources.Any(static source => source.IsUnknown)
            || Parent?.HasUnknownSource == true;

        public bool Add(SourceBinding source)
        {
            Sources.Add(source);
            var unique = true;
            foreach (var qualifier in source.Qualifiers)
            {
                unique &= _qualifiers.TryAdd(qualifier, source);
            }

            return unique;
        }

        public SourceBinding? FindQualifier(string qualifier) =>
            _qualifiers.GetValueOrDefault(qualifier)
            ?? Parent?.FindQualifier(qualifier);

        public IReadOnlyList<ColumnMatch> FindColumn(string name)
        {
            var local = Sources
                .Where(source => source.Columns.TryGetValue(name, out _))
                .Select(source => new ColumnMatch(source, source.Columns[name]))
                .ToArray();
            return local.Length > 0
                ? local
                : Parent?.FindColumn(name) ?? Array.Empty<ColumnMatch>();
        }
    }

    private sealed class SourceBinding
    {
        private SourceBinding(
            string name,
            IReadOnlyDictionary<string, SqlTypeFamily> columns,
            IReadOnlyList<string> columnOrder,
            IReadOnlyList<string> qualifiers,
            TableInfo? table,
            bool isUnknown)
        {
            Name = name;
            Columns = columns;
            ColumnOrder = columnOrder;
            Qualifiers = qualifiers
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Table = table;
            IsUnknown = isUnknown;
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, SqlTypeFamily> Columns { get; }

        public IReadOnlyList<string> ColumnOrder { get; }

        public IReadOnlyList<string> Qualifiers { get; }

        public TableInfo? Table { get; }

        public bool IsUnknown { get; }

        public static SourceBinding FromTable(
            TableInfo table,
            string name,
            IReadOnlyList<string> qualifiers) =>
            new(
                name,
                table.Columns.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.Type,
                    StringComparer.OrdinalIgnoreCase),
                table.ColumnOrder,
                qualifiers,
                table,
                false);

        public static SourceBinding FromProjection(
            string name,
            Projection projection,
            string fallbackQualifier,
            IReadOnlyList<string>? qualifiers = null)
        {
            var columns = new Dictionary<string, SqlTypeFamily>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var column in projection.Columns)
            {
                if (column.Name is null || !columns.TryAdd(column.Name, column.Type)) continue;
                order.Add(column.Name);
            }

            return new(
                name,
                columns,
                order,
                qualifiers ?? [fallbackQualifier],
                null,
                false);
        }

        public static SourceBinding Unknown(
            string name,
            IReadOnlyList<string> qualifiers) =>
            new(
                name,
                new Dictionary<string, SqlTypeFamily>(StringComparer.OrdinalIgnoreCase),
                [],
                qualifiers,
                null,
                true);
    }

    private sealed record ColumnInfo(string Name, SqlTypeFamily Type, SqlColumnSchema Model);

    private sealed class TableInfo
    {
        public required string Key { get; init; }

        public required string SimpleName { get; init; }

        public required string DisplayName { get; init; }

        public required SqlTableSchema Model { get; init; }

        public required IReadOnlyDictionary<string, ColumnInfo> Columns { get; init; }

        public required IReadOnlyList<string> ColumnOrder { get; init; }

        public bool IsUniqueKey(IReadOnlyList<string> columns)
        {
            if (columns.Count == 1
                && Columns.TryGetValue(columns[0], out var column)
                && (column.Model.IsPrimaryKey || column.Model.IsUnique))
            {
                return true;
            }

            if (Model.PrimaryKey is not null
                && SetEquals(Model.PrimaryKey, columns))
            {
                return true;
            }

            return Model.UniqueKeys?.Any(key => SetEquals(key, columns)) == true;
        }

        private static bool SetEquals(
            IReadOnlyList<string> first,
            IReadOnlyList<string> second) =>
            first.Count == second.Count
            && first.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(second);
    }

    private sealed record Relationship(
        TableInfo Source,
        IReadOnlyList<string> SourceColumns,
        TableInfo Target,
        IReadOnlyList<string> TargetColumns);

    private sealed class CatalogIndex
    {
        private readonly Dictionary<string, TableInfo> _tables =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ambiguousNames =
            new(StringComparer.OrdinalIgnoreCase);

        public CatalogIndex(SqlSchemaCatalog catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog.Tables);
            var tables = new List<TableInfo>();
            foreach (var model in catalog.Tables)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(model.Name);
                ArgumentNullException.ThrowIfNull(model.Columns);
                var displayName = model.Schema is null
                    ? model.Name
                    : $"{model.Schema}.{model.Name}";
                var columns = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
                var order = new List<string>();
                foreach (var column in model.Columns)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(column.Name);
                    ArgumentNullException.ThrowIfNull(column.DataType);
                    if (!columns.TryAdd(
                        column.Name,
                        new ColumnInfo(
                            column.Name,
                            SqlTypeFamilies.Classify(column.DataType),
                            column)))
                    {
                        throw new ArgumentException(
                            $"Table '{displayName}' contains duplicate column '{column.Name}'.",
                            nameof(catalog));
                    }

                    order.Add(column.Name);
                }

                var table = new TableInfo
                {
                    Key = displayName,
                    SimpleName = model.Name,
                    DisplayName = displayName,
                    Model = model,
                    Columns = columns,
                    ColumnOrder = order,
                };
                tables.Add(table);
                AddName(displayName, table, catalog);
                if (model.Schema is not null)
                {
                    AddPossiblyAmbiguousName(model.Name, table);
                }
                if (model.Aliases is not null)
                {
                    foreach (var alias in model.Aliases)
                    {
                        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
                        AddName(alias, table, catalog);
                    }
                }
            }

            Tables = tables;
            Relationships = BuildRelationships();
        }

        public IReadOnlyList<TableInfo> Tables { get; }

        public IReadOnlyList<Relationship> Relationships { get; }

        public TableInfo? Resolve(TableName name)
        {
            var fullName = JoinParts(name.Parts);
            return _tables.GetValueOrDefault(fullName);
        }

        public TableInfo? Resolve(string name, string? schema) =>
            schema is not null
                ? _tables.GetValueOrDefault($"{schema}.{name}")
                : _tables.GetValueOrDefault(name);

        public IReadOnlyList<(TableInfo Table, SqlTypeFamily Type)> FindColumn(string name) =>
            Tables
                .Where(table => table.Columns.ContainsKey(name))
                .Select(table => (table, table.Columns[name].Type))
                .ToArray();

        private void AddName(string name, TableInfo table, SqlSchemaCatalog catalog)
        {
            if (_tables.TryGetValue(name, out var existing)
                && !ReferenceEquals(existing, table))
            {
                throw new ArgumentException(
                    $"Catalog table name or alias '{name}' is ambiguous.",
                    nameof(catalog));
            }

            _tables[name] = table;
        }

        private void AddPossiblyAmbiguousName(string name, TableInfo table)
        {
            if (_ambiguousNames.Contains(name)) return;
            if (_tables.TryGetValue(name, out var existing)
                && !ReferenceEquals(existing, table))
            {
                _tables.Remove(name);
                _ambiguousNames.Add(name);
                return;
            }

            _tables[name] = table;
        }

        private IReadOnlyList<Relationship> BuildRelationships()
        {
            var relationships = new List<Relationship>();
            foreach (var source in Tables)
            {
                foreach (var column in source.Model.Columns)
                {
                    if (column.References is null) continue;
                    var target = Resolve(
                        column.References.Table,
                        column.References.Schema);
                    if (target is not null)
                    {
                        relationships.Add(new Relationship(
                            source,
                            [column.Name],
                            target,
                            [column.References.Column]));
                    }
                }

                if (source.Model.ForeignKeys is null) continue;
                foreach (var foreignKey in source.Model.ForeignKeys)
                {
                    var target = Resolve(
                        foreignKey.References.Table,
                        foreignKey.References.Schema);
                    if (target is not null)
                    {
                        relationships.Add(new Relationship(
                            source,
                            foreignKey.Columns,
                            target,
                            foreignKey.References.Columns));
                    }
                }
            }

            return relationships;
        }
    }
}

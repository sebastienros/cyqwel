using Cyqwel.Ast;

namespace Cyqwel.Visitors;

/// <summary>
/// Rewrites SQL trees bottom-up while preserving unchanged node instances.
/// </summary>
public abstract partial class SqlRewriter
{
    public virtual SqlNode Visit(SqlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var result = node switch
        {
            SqlDocument value => VisitDocument(value),
            SqlBatch value => VisitBatch(value),
            DeclareStatement value => VisitDeclare(value),
            TableVariableDeclarationStatement value => VisitTableVariableDeclaration(value),
            SetVariableStatement value => VisitSetVariable(value),
            PrintStatement value => VisitPrint(value),
            ExecuteSqlStatement value => VisitExecuteSql(value),
            TransactionStatement value => VisitTransaction(value),
            SelectStatement value => VisitSelect(value),
            ValuesStatement value => VisitValues(value),
            SetOperationStatement value => VisitSetOperation(value),
            ExplainStatement value => VisitExplain(value),
            InsertStatement value => VisitInsert(value),
            TSqlOutputClause value => VisitTSqlOutput(value),
            MergeActionExpression value => VisitMergeActionExpression(value),
            CreateInlineFunctionStatement value => VisitCreateInlineFunction(value),
            CreateSchemaStatement value => VisitCreateSchema(value),
            ComputedColumnDefinition value => VisitComputedColumnDefinition(value),
            DefaultConstraint value => VisitDefaultConstraint(value),
            AddTableElementAction value => VisitAddTableElement(value),
            UpdateStatement value => VisitUpdate(value),
            DeleteStatement value => VisitDelete(value),
            MergeStatement value => VisitMerge(value),
            GrantStatement value => VisitGrant(value),
            SetStatement value => VisitSet(value),
            CreateTableStatement value => VisitCreateTable(value),
            AlterTableStatement value => VisitAlterTable(value),
            DropStatement value => VisitDrop(value),
            TruncateStatement value => VisitTruncate(value),
            CreateViewStatement value => VisitCreateView(value),
            CreateIndexStatement value => VisitCreateIndex(value),
            CreateSequenceStatement value => VisitCreateSequence(value),
            AlterSequenceStatement value => VisitAlterSequence(value),
            CreateProcedureStatement value => VisitCreateProcedure(value),
            ReplaceProcedureStatement value => VisitReplaceProcedure(value),
            DropProcedureStatement value => VisitDropProcedure(value),
            CallProcedureStatement value => VisitCallProcedure(value),
            ProceduralIfStatement value => VisitProceduralIf(value),
            ProceduralWhileStatement value => VisitProceduralWhile(value),
            ProceduralBreakStatement value => VisitProceduralBreak(value),
            ProceduralContinueStatement value => VisitProceduralContinue(value),
            ProceduralReturnStatement value => VisitProceduralReturn(value),
            SqlIdentifier value => VisitIdentifier(value),
            ColumnExpression value => VisitColumn(value),
            StarExpression value => VisitStar(value),
            LiteralExpression value => VisitLiteral(value),
            CurrentTimestampExpression value => VisitCurrentTimestamp(value),
            TrimExpression value => VisitTrim(value),
            TypedLiteralExpression value => VisitTypedLiteral(value),
            HexLiteralExpression value => VisitHexLiteral(value),
            ParameterExpression value => VisitParameter(value),
            LocalVariableExpression value => VisitLocalVariableExpression(value),
            ParenthesizedExpression value => VisitParenthesized(value),
            UnaryExpression value => VisitUnary(value),
            BinaryExpression value => VisitBinary(value),
            ConvertExpression value => VisitConvert(value),
            JsonArrayAggregateExpression value => VisitJsonArrayAggregate(value),
            QuantifiedComparisonExpression value => VisitQuantifiedComparison(value),
            BetweenExpression value => VisitBetween(value),
            InExpression value => VisitIn(value),
            IsNullExpression value => VisitIsNull(value),
            BooleanTestExpression value => VisitBooleanTest(value),
            DistinctFromExpression value => VisitDistinctFrom(value),
            RowExpression value => VisitRow(value),
            DefaultExpression value => VisitDefault(value),
            CollateExpression value => VisitCollate(value),
            ExtractExpression value => VisitExtract(value),
            IntervalExpression value => VisitInterval(value),
            SequenceValueExpression value => VisitSequenceValue(value),
            FunctionCallExpression value => VisitFunctionCall(value),
            WindowExpression value => VisitWindow(value),
            ExistsExpression value => VisitExists(value),
            SubqueryExpression value => VisitSubquery(value),
            WhenClause value => VisitWhen(value),
            CaseExpression value => VisitCase(value),
            CastExpression value => VisitCast(value),
            TryCastExpression value => VisitTryCast(value),
            SqlDataType value => VisitDataType(value),
            TableName value => VisitTableName(value),
            NamedTable value => VisitNamedTable(value),
            TSqlTableHint value => VisitTSqlTableHint(value),
            TSqlIndexReference value => VisitTSqlIndexReference(value),
            TSqlTableSample value => VisitTSqlTableSample(value),
            DerivedTable value => VisitDerivedTable(value),
            TableFunction value => VisitTableFunction(value),
            ParenthesizedTable value => VisitParenthesizedTable(value),
            DerivedMutationTable value => VisitDerivedMutationTable(value),
            OpenJsonTable value => VisitOpenJsonTable(value),
            OpenJsonColumn value => VisitOpenJsonColumn(value),
            PivotTable value => VisitPivotTable(value),
            UnpivotTable value => VisitUnpivotTable(value),
            TSqlResultFormat value => VisitTSqlResultFormat(value),
            TSqlQueryOption value => VisitTSqlQueryOption(value),
            JoinTable value => VisitJoin(value),
            SelectItem value => VisitSelectItem(value),
            OrderByItem value => VisitOrderByItem(value),
            CommonTableExpression value => VisitCommonTableExpression(value),
            Assignment value => VisitAssignment(value),
            WindowDefinition value => VisitWindowDefinition(value),
            WindowFrame value => VisitWindowFrame(value),
            WindowFrameBound value => VisitWindowFrameBound(value),
            ConnectByClause value => VisitConnectBy(value),
            MergeWhenClause value => VisitMergeWhen(value),
            MergeUpdateAction value => VisitMergeUpdate(value),
            MergeInsertAction value => VisitMergeInsert(value),
            MergeDeleteAction value => VisitMergeDelete(value),
            ColumnDefinition value => VisitColumnDefinition(value),
            IndexTableElement value => VisitIndexTableElement(value),
            PrimaryKeyConstraint value => VisitPrimaryKeyConstraint(value),
            UniqueConstraint value => VisitUniqueConstraint(value),
            ForeignKeyConstraint value => VisitForeignKeyConstraint(value),
            CheckConstraint value => VisitCheckConstraint(value),
            AddColumnAction value => VisitAddColumn(value),
            DropColumnAction value => VisitDropColumn(value),
            AlterColumnAction value => VisitAlterColumn(value),
            AddConstraintAction value => VisitAddConstraint(value),
            DropConstraintAction value => VisitDropConstraint(value),
            RenameColumnAction value => VisitRenameColumn(value),
            RenameTableAction value => VisitRenameTable(value),
            IndexColumn value => VisitIndexColumn(value),
            SequenceOptions value => VisitSequenceOptions(value),
            ProcedureParameter value => VisitProcedureParameter(value),
            LocalVariable value => VisitLocalVariable(value),
            ProceduralBlock value => VisitProceduralBlock(value),
            ProcedureArgument value => VisitProcedureArgument(value),
            _ => throw new NotSupportedException($"Unsupported SQL node type '{node.GetType().Name}'."),
        };

        if (result is SqlQuery query)
        {
            var format = VisitOptional(query.ResultFormat);
            if (!ReferenceEquals(format, query.ResultFormat)) result = query with { ResultFormat = format };
        }
        if (result is SqlStatement statement)
        {
            var options = VisitOptionalList(statement.QueryOptions);
            if (!ReferenceEquals(options, statement.QueryOptions)) result = statement with { QueryOptions = options };
        }
        return result;
    }

    public T Visit<T>(T node) where T : SqlNode => (T)Visit((SqlNode)node);

    protected virtual SqlNode VisitCurrentTimestamp(CurrentTimestampExpression node) => node;

    protected virtual SqlNode VisitDocument(SqlDocument node)
    {
        if (node.Batches is null)
            return Update(node, VisitList(node.Statements), node.Statements, static (n, statements) => n with { Statements = statements });
        var batches = VisitList(node.Batches);
        return ReferenceEquals(batches, node.Batches) ? node : node with
        {
            Batches = batches,
            Statements = batches.SelectMany(static batch => batch.Statements).ToArray(),
        };
    }

    protected virtual SqlNode VisitExplain(ExplainStatement node) =>
        Update(node, Visit(node.Query), node.Query, static (n, query) => n with { Query = query });

    protected virtual SqlNode VisitSelect(SelectStatement node)
    {
        var projections = VisitList(node.Projections);
        var from = VisitOptional(node.From);
        var where = VisitOptional(node.Where);
        var groupBy = VisitOptionalList(node.GroupBy);
        var having = VisitOptional(node.Having);
        var orderBy = VisitOptionalList(node.OrderBy);
        var limit = VisitOptional(node.Limit);
        var offset = VisitOptional(node.Offset);
        var ctes = VisitOptionalList(node.CommonTableExpressions);
        var top = VisitOptional(node.Top);
        var windows = VisitOptionalList(node.Windows);
        var qualify = VisitOptional(node.Qualify);
        var connectBy = VisitOptional(node.ConnectBy);
        var into = VisitOptional(node.Into);

        return ReferenceEquals(projections, node.Projections)
            && ReferenceEquals(from, node.From)
            && ReferenceEquals(where, node.Where)
            && ReferenceEquals(groupBy, node.GroupBy)
            && ReferenceEquals(having, node.Having)
            && ReferenceEquals(orderBy, node.OrderBy)
            && ReferenceEquals(limit, node.Limit)
            && ReferenceEquals(offset, node.Offset)
            && ReferenceEquals(ctes, node.CommonTableExpressions)
            && ReferenceEquals(top, node.Top)
            && ReferenceEquals(windows, node.Windows)
            && ReferenceEquals(qualify, node.Qualify)
            && ReferenceEquals(connectBy, node.ConnectBy)
            && ReferenceEquals(into, node.Into)
                ? node
                : node with
                {
                    Projections = projections,
                    From = from,
                    Where = where,
                    GroupBy = groupBy,
                    Having = having,
                    OrderBy = orderBy,
                    Limit = limit,
                    Offset = offset,
                    CommonTableExpressions = ctes,
                    Top = top,
                    Windows = windows,
                    Qualify = qualify,
                    ConnectBy = connectBy,
                    Into = into,
                };
    }

    protected virtual SqlNode VisitSetOperation(SetOperationStatement node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);
        var orderBy = VisitOptionalList(node.OrderBy);
        var limit = VisitOptional(node.Limit);
        var offset = VisitOptional(node.Offset);
        var ctes = VisitOptionalList(node.CommonTableExpressions);

        return ReferenceEquals(left, node.Left)
            && ReferenceEquals(right, node.Right)
            && ReferenceEquals(orderBy, node.OrderBy)
            && ReferenceEquals(limit, node.Limit)
            && ReferenceEquals(offset, node.Offset)
            && ReferenceEquals(ctes, node.CommonTableExpressions)
                ? node
                : node with
                {
                    Left = left,
                    Right = right,
                    OrderBy = orderBy,
                    Limit = limit,
                    Offset = offset,
                    CommonTableExpressions = ctes,
                };
    }

    protected virtual SqlNode VisitInsert(InsertStatement node)
    {
        var ctes = VisitOptionalList(node.CommonTableExpressions);
        var top = VisitOptional(node.Top);
        var output = VisitOptional(node.Output);
        var target = Visit(node.Target);
        var columns = VisitOptionalList(node.Columns);
        var values = VisitRows(node.Values);
        var source = VisitOptional(node.Source);
        var returning = VisitOptionalList(node.Returning);
        var returningInto = VisitOptionalList(node.ReturningInto);

        return ReferenceEquals(ctes, node.CommonTableExpressions) && ReferenceEquals(top, node.Top) && ReferenceEquals(output, node.Output)
            && ReferenceEquals(target, node.Target)
            && ReferenceEquals(columns, node.Columns)
            && ReferenceEquals(values, node.Values)
            && ReferenceEquals(source, node.Source)
            && ReferenceEquals(returning, node.Returning)
            && ReferenceEquals(returningInto, node.ReturningInto)
                ? node
                : node with
                {
                    Target = target,
                    Top = top, Output = output,
                    CommonTableExpressions = ctes,
                    Columns = columns,
                    Values = values,
                    Source = source,
                    Returning = returning,
                    ReturningInto = returningInto,
                };
    }

    protected virtual SqlNode VisitUpdate(UpdateStatement node)
    {
        var ctes = VisitOptionalList(node.CommonTableExpressions);
        var top = VisitOptional(node.Top);
        var output = VisitOptional(node.Output);
        var target = Visit(node.Target);
        var assignments = VisitList(node.Assignments);
        var where = VisitOptional(node.Where);
        var returning = VisitOptionalList(node.Returning);
        var returningInto = VisitOptionalList(node.ReturningInto);
        var from = VisitOptional(node.From);

        return ReferenceEquals(ctes, node.CommonTableExpressions) && ReferenceEquals(top, node.Top) && ReferenceEquals(output, node.Output)
            && ReferenceEquals(target, node.Target)
            && ReferenceEquals(assignments, node.Assignments)
            && ReferenceEquals(where, node.Where)
            && ReferenceEquals(returning, node.Returning)
            && ReferenceEquals(returningInto, node.ReturningInto)
            && ReferenceEquals(from, node.From)
                ? node
                : node with
                {
                    Target = target,
                    Top = top, Output = output,
                    CommonTableExpressions = ctes,
                    Assignments = assignments,
                    Where = where,
                    Returning = returning,
                    ReturningInto = returningInto,
                    From = from,
                };
    }

    protected virtual SqlNode VisitDelete(DeleteStatement node)
    {
        var ctes = VisitOptionalList(node.CommonTableExpressions);
        var top = VisitOptional(node.Top);
        var output = VisitOptional(node.Output);
        var from = VisitOptional(node.From);
        var target = Visit(node.Target);
        var where = VisitOptional(node.Where);
        var returning = VisitOptionalList(node.Returning);
        var returningInto = VisitOptionalList(node.ReturningInto);
        var usingSource = VisitOptional(node.Using);

        return ReferenceEquals(ctes, node.CommonTableExpressions) && ReferenceEquals(top, node.Top) && ReferenceEquals(output, node.Output)
            && ReferenceEquals(from, node.From) && ReferenceEquals(target, node.Target)
            && ReferenceEquals(where, node.Where)
            && ReferenceEquals(returning, node.Returning)
            && ReferenceEquals(returningInto, node.ReturningInto)
            && ReferenceEquals(usingSource, node.Using)
                ? node
                : node with
                {
                    Target = target,
                    Top = top, Output = output, From = from,
                    CommonTableExpressions = ctes,
                    Where = where,
                    Returning = returning,
                    ReturningInto = returningInto,
                    Using = usingSource,
                };
    }

    protected virtual SqlNode VisitGrant(GrantStatement node)
    {
        var objects = VisitOptionalList(node.Objects);
        var grantees = VisitOptionalList(node.Grantees);
        return ReferenceEquals(objects, node.Objects) && ReferenceEquals(grantees, node.Grantees)
            ? node
            : node with { Objects = objects, Grantees = grantees };
    }

    protected virtual SqlNode VisitSet(SetStatement node)
    {
        var keywords = VisitOptionalList(node.Keywords);
        var arguments = VisitList(node.Arguments);
        return ReferenceEquals(keywords, node.Keywords) && ReferenceEquals(arguments, node.Arguments)
            ? node
            : node with { Keywords = keywords, Arguments = arguments };
    }

    protected virtual SqlNode VisitIdentifier(SqlIdentifier node) => node;

    protected virtual SqlNode VisitColumn(ColumnExpression node) =>
        Update(node, VisitList(node.Parts), node.Parts, static (n, parts) => n with { Parts = parts });

    protected virtual SqlNode VisitStar(StarExpression node) =>
        UpdateOptional(node, VisitOptionalList(node.Qualifier), node.Qualifier, static (n, qualifier) => n with { Qualifier = qualifier });

    protected virtual SqlNode VisitLiteral(LiteralExpression node) => node;
    protected virtual SqlNode VisitTrim(TrimExpression node)
    {
        var character = VisitOptional(node.Character);
        var source = Visit(node.Source);
        return ReferenceEquals(character, node.Character) && ReferenceEquals(source, node.Source)
            ? node
            : node with { Character = character, Source = source };
    }

    protected virtual SqlNode VisitTypedLiteral(TypedLiteralExpression node)
    {
        var typeName = Visit(node.TypeName);
        var value = Visit(node.Value);
        return ReferenceEquals(typeName, node.TypeName) && ReferenceEquals(value, node.Value)
            ? node
            : node with { TypeName = typeName, Value = value };
    }

    protected virtual SqlNode VisitHexLiteral(HexLiteralExpression node) => node;

    protected virtual SqlNode VisitParameter(ParameterExpression node)
    {
        var defaultValue = VisitOptional(node.DefaultValue);
        return ReferenceEquals(defaultValue, node.DefaultValue)
            ? node
            : node with { DefaultValue = defaultValue };
    }

    protected virtual SqlNode VisitParenthesized(ParenthesizedExpression node) =>
        Update(
            node,
            Visit(node.Expression),
            node.Expression,
            static (n, expression) => n with { Expression = expression });

    protected virtual SqlNode VisitUnary(UnaryExpression node) =>
        Update(node, Visit(node.Operand), node.Operand, static (n, operand) => n with { Operand = operand });

    protected virtual SqlNode VisitBinary(BinaryExpression node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);
        return ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right)
            ? node
            : node with { Left = left, Right = right };
    }

    protected virtual SqlNode VisitQuantifiedComparison(QuantifiedComparisonExpression node)
    {
        var left = Visit(node.Left);
        var query = Visit(node.Query);
        return ReferenceEquals(left, node.Left) && ReferenceEquals(query, node.Query)
            ? node
            : node with { Left = left, Query = query };
    }

    protected virtual SqlNode VisitConvert(ConvertExpression node)
    {
        var expression = Visit(node.Expression);
        var dataType = Visit(node.DataType);
        var style = VisitOptional(node.Style);
        return ReferenceEquals(expression, node.Expression) && ReferenceEquals(dataType, node.DataType)
            && ReferenceEquals(style, node.Style)
                ? node
                : node with { Expression = expression, DataType = dataType, Style = style };
    }

    protected virtual SqlNode VisitJsonArrayAggregate(JsonArrayAggregateExpression node)
    {
        var expression = Visit(node.Expression);
        var orderBy = VisitOptionalList(node.OrderBy);
        return ReferenceEquals(expression, node.Expression) && ReferenceEquals(orderBy, node.OrderBy)
            ? node
            : node with { Expression = expression, OrderBy = orderBy };
    }

    protected virtual SqlNode VisitBetween(BetweenExpression node)
    {
        var expression = Visit(node.Expression);
        var lower = Visit(node.Lower);
        var upper = Visit(node.Upper);
        return ReferenceEquals(expression, node.Expression)
            && ReferenceEquals(lower, node.Lower)
            && ReferenceEquals(upper, node.Upper)
                ? node
                : node with { Expression = expression, Lower = lower, Upper = upper };
    }

    protected virtual SqlNode VisitIn(InExpression node)
    {
        var expression = Visit(node.Expression);
        var values = VisitList(node.Values);
        var query = VisitOptional(node.Query);
        return ReferenceEquals(expression, node.Expression)
            && ReferenceEquals(values, node.Values)
            && ReferenceEquals(query, node.Query)
                ? node
                : node with { Expression = expression, Values = values, Query = query };
    }

    protected virtual SqlNode VisitIsNull(IsNullExpression node) =>
        Update(node, Visit(node.Expression), node.Expression, static (n, expression) => n with { Expression = expression });

    protected virtual SqlNode VisitFunctionCall(FunctionCallExpression node)
    {
        var name = Visit(node.Name);
        var qualifiers = VisitOptionalList(node.Qualifiers);
        var arguments = VisitList(node.Arguments);
        var filter = VisitOptional(node.Filter);
        var withinGroup = VisitOptionalList(node.WithinGroup);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(qualifiers, node.Qualifiers)
            && ReferenceEquals(arguments, node.Arguments)
            && ReferenceEquals(filter, node.Filter)
            && ReferenceEquals(withinGroup, node.WithinGroup)
            ? node
            : node with
            {
                Name = name,
                Qualifiers = qualifiers,
                Arguments = arguments,
                Filter = filter,
                WithinGroup = withinGroup,
            };
    }

    protected virtual SqlNode VisitWindow(WindowExpression node)
    {
        var expression = Visit(node.Expression);
        var partitionBy = VisitOptionalList(node.PartitionBy);
        var orderBy = VisitOptionalList(node.OrderBy);
        var frame = VisitOptional(node.Frame);
        var windowName = VisitOptional(node.WindowName);
        return ReferenceEquals(expression, node.Expression)
            && ReferenceEquals(partitionBy, node.PartitionBy)
            && ReferenceEquals(orderBy, node.OrderBy)
            && ReferenceEquals(frame, node.Frame)
            && ReferenceEquals(windowName, node.WindowName)
                ? node
                : node with
                {
                    Expression = expression,
                    PartitionBy = partitionBy,
                    OrderBy = orderBy,
                    Frame = frame,
                    WindowName = windowName,
                };
    }

    protected virtual SqlNode VisitExists(ExistsExpression node) =>
        Update(node, Visit(node.Query), node.Query, static (n, query) => n with { Query = query });

    protected virtual SqlNode VisitSubquery(SubqueryExpression node) =>
        Update(node, Visit(node.Query), node.Query, static (n, query) => n with { Query = query });

    protected virtual SqlNode VisitWhen(WhenClause node)
    {
        var condition = Visit(node.Condition);
        var result = Visit(node.Result);
        return ReferenceEquals(condition, node.Condition) && ReferenceEquals(result, node.Result)
            ? node
            : node with { Condition = condition, Result = result };
    }

    protected virtual SqlNode VisitCase(CaseExpression node)
    {
        var operand = VisitOptional(node.Operand);
        var whens = VisitList(node.Whens);
        var @else = VisitOptional(node.Else);
        return ReferenceEquals(operand, node.Operand)
            && ReferenceEquals(whens, node.Whens)
            && ReferenceEquals(@else, node.Else)
                ? node
                : node with { Operand = operand, Whens = whens, Else = @else };
    }

    protected virtual SqlNode VisitCast(CastExpression node)
    {
        var expression = Visit(node.Expression);
        var dataType = Visit(node.DataType);
        return ReferenceEquals(expression, node.Expression) && ReferenceEquals(dataType, node.DataType)
            ? node
            : node with { Expression = expression, DataType = dataType };
    }

    protected virtual SqlNode VisitDataType(SqlDataType node)
    {
        var name = Visit(node.Name);
        var intervalEndField = VisitOptional(node.IntervalEndField);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(intervalEndField, node.IntervalEndField)
                ? node
                : node with { Name = name, IntervalEndField = intervalEndField };
    }

    protected virtual SqlNode VisitTableName(TableName node) =>
        Update(node, VisitList(node.Parts), node.Parts, static (n, parts) => n with { Parts = parts });

    protected virtual SqlNode VisitNamedTable(NamedTable node)
    {
        var name = Visit(node.Name);
        var alias = VisitOptional(node.Alias);
        var hints = VisitOptionalList(node.Hints);
        var sample = VisitOptional(node.Sample);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(alias, node.Alias) && ReferenceEquals(hints, node.Hints)
            && ReferenceEquals(sample, node.Sample)
            ? node
            : node with { Name = name, Alias = alias, Hints = hints, Sample = sample };
    }

    protected virtual SqlNode VisitDerivedTable(DerivedTable node)
    {
        var query = Visit(node.Query);
        var alias = Visit(node.Alias);
        var columns = VisitOptionalList(node.Columns);
        return ReferenceEquals(query, node.Query) && ReferenceEquals(alias, node.Alias)
            && ReferenceEquals(columns, node.Columns)
            ? node
            : node with { Query = query, Alias = alias, Columns = columns };
    }

    protected virtual SqlNode VisitJoin(JoinTable node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);
        var condition = VisitOptional(node.Condition);
        var usingColumns = VisitOptionalList(node.Using);
        return ReferenceEquals(left, node.Left)
            && ReferenceEquals(right, node.Right)
            && ReferenceEquals(condition, node.Condition)
            && ReferenceEquals(usingColumns, node.Using)
                ? node
                : node with { Left = left, Right = right, Condition = condition, Using = usingColumns };
    }

    protected virtual SqlNode VisitSelectItem(SelectItem node)
    {
        var expression = Visit(node.Expression);
        var alias = VisitOptional(node.Alias);
        var target = VisitOptional(node.AssignmentTarget);
        return ReferenceEquals(expression, node.Expression) && ReferenceEquals(alias, node.Alias)
            && ReferenceEquals(target, node.AssignmentTarget)
            ? node
            : node with { Expression = expression, Alias = alias, AssignmentTarget = target };
    }

    protected virtual SqlNode VisitOrderByItem(OrderByItem node) =>
        Update(node, Visit(node.Expression), node.Expression, static (n, expression) => n with { Expression = expression });

    protected virtual SqlNode VisitCommonTableExpression(CommonTableExpression node)
    {
        var name = Visit(node.Name);
        var query = Visit(node.Query);
        var columns = VisitOptionalList(node.Columns);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(query, node.Query)
            && ReferenceEquals(columns, node.Columns)
                ? node
                : node with { Name = name, Query = query, Columns = columns };
    }

    protected virtual SqlNode VisitAssignment(Assignment node)
    {
        var column = Visit(node.Column);
        var value = Visit(node.Value);
        return ReferenceEquals(column, node.Column) && ReferenceEquals(value, node.Value)
            ? node
            : node with { Column = column, Value = value };
    }

    private T? VisitOptional<T>(T? node) where T : SqlNode => node is null ? null : Visit(node);

    private IReadOnlyList<T> VisitList<T>(IReadOnlyList<T> nodes) where T : SqlNode
    {
        T[]? rewritten = null;

        for (var i = 0; i < nodes.Count; i++)
        {
            var item = Visit(nodes[i]);
            if (rewritten is null && !ReferenceEquals(item, nodes[i]))
            {
                rewritten = new T[nodes.Count];
                for (var j = 0; j < i; j++) rewritten[j] = nodes[j];
            }

            if (rewritten is not null) rewritten[i] = item;
        }

        return rewritten ?? nodes;
    }

    private IReadOnlyList<T>? VisitOptionalList<T>(IReadOnlyList<T>? nodes) where T : SqlNode =>
        nodes is null ? null : VisitList(nodes);

    private IReadOnlyList<IReadOnlyList<SqlExpression>>? VisitRows(IReadOnlyList<IReadOnlyList<SqlExpression>>? rows)
    {
        if (rows is null) return null;

        IReadOnlyList<SqlExpression>[]? rewritten = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = VisitList(rows[i]);
            if (rewritten is null && !ReferenceEquals(row, rows[i]))
            {
                rewritten = new IReadOnlyList<SqlExpression>[rows.Count];
                for (var j = 0; j < i; j++) rewritten[j] = rows[j];
            }

            if (rewritten is not null) rewritten[i] = row;
        }

        return rewritten ?? rows;
    }

    private static SqlNode Update<TNode, TChild>(
        TNode node,
        TChild child,
        TChild original,
        Func<TNode, TChild, TNode> update)
        where TNode : SqlNode
        where TChild : SqlNode =>
        ReferenceEquals(child, original) ? node : update(node, child);

    private static SqlNode Update<TNode, TChild>(
        TNode node,
        IReadOnlyList<TChild> children,
        IReadOnlyList<TChild> original,
        Func<TNode, IReadOnlyList<TChild>, TNode> update)
        where TNode : SqlNode
        where TChild : SqlNode =>
        ReferenceEquals(children, original) ? node : update(node, children);

    private static SqlNode UpdateOptional<TNode, TChild>(
        TNode node,
        IReadOnlyList<TChild>? children,
        IReadOnlyList<TChild>? original,
        Func<TNode, IReadOnlyList<TChild>?, TNode> update)
        where TNode : SqlNode
        where TChild : SqlNode =>
        ReferenceEquals(children, original) ? node : update(node, children);
}

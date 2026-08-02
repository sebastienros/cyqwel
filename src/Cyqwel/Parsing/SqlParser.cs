using System.Collections.Concurrent;
using System.Globalization;
using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Visitors;
using Parlot;
using Parlot.Fluent;
using static Parlot.Fluent.Parsers;

namespace Cyqwel.Parsing;

/// <summary>
/// Parses SQL using a single reusable Parlot parser graph.
/// </summary>
public static class SqlParser
{
    private static readonly ConcurrentDictionary<
        SqlDialectParserOptions,
        Lazy<Parser<SqlDocument>>> ParserCache = new();
    private static readonly Parser<SqlDocument> PermissiveParser =
        CreateDocumentParser(SqlDialectParserOptions.Permissive);

    private static Parser<SqlDocument> CreateDocumentParser(SqlDialectParserOptions syntax)
    {
        var comma = Terms.Char(',');
        var dot = Terms.Char('.');
        var semicolon = Terms.Char(';');
        var leftParenthesis = Terms.Char('(');
        var rightParenthesis = Terms.Char(')');
        var star = Terms.Char('*');

        var SELECT = Keyword("SELECT");
        var DISTINCT = Keyword("DISTINCT");
        var TOP = Keyword("TOP");
        var PERCENT = Keyword("PERCENT");
        var TIES = Keyword("TIES");
        var FROM = Keyword("FROM");
        var AS = Keyword("AS");
        var WHERE = Keyword("WHERE");
        var GROUP = Keyword("GROUP");
        var BY = Keyword("BY");
        var HAVING = Keyword("HAVING");
        var ORDER = Keyword("ORDER");
        var OVER = Keyword("OVER");
        var PARTITION = Keyword("PARTITION");
        var ASC = Keyword("ASC");
        var DESC = Keyword("DESC");
        var NULLS = Keyword("NULLS");
        var FIRST = Keyword("FIRST");
        var LAST = Keyword("LAST");
        var LIMIT = Keyword("LIMIT");
        var OFFSET = Keyword("OFFSET");
        var ROW = Keyword("ROW");
        var ROWS = Keyword("ROWS");
        var FETCH = Keyword("FETCH");
        var NEXT = Keyword("NEXT");
        var ONLY = Keyword("ONLY");
        var UNION = Keyword("UNION");
        var INTERSECT = Keyword("INTERSECT");
        var EXCEPT = Keyword("EXCEPT");
        var ALL = Keyword("ALL");
        var WITH = Keyword("WITH");
        var JOIN = Keyword("JOIN");
        var INNER = Keyword("INNER");
        var LEFT = Keyword("LEFT");
        var RIGHT = Keyword("RIGHT");
        var FULL = Keyword("FULL");
        var CROSS = Keyword("CROSS");
        var OUTER = Keyword("OUTER");
        var ON = Keyword("ON");
        var AND = Keyword("AND");
        var OR = Keyword("OR");
        var NOT = Keyword("NOT");
        var BETWEEN = Keyword("BETWEEN");
        var IN = Keyword("IN");
        var IS = Keyword("IS");
        var LIKE = Keyword("LIKE");
        var ILIKE = Keyword("ILIKE");
        var TRUE = Keyword("TRUE");
        var FALSE = Keyword("FALSE");
        var NULL = Keyword("NULL");
        var EXISTS = Keyword("EXISTS");
        var CASE = Keyword("CASE");
        var WHEN = Keyword("WHEN");
        var THEN = Keyword("THEN");
        var ELSE = Keyword("ELSE");
        var END = Keyword("END");
        var CAST = Keyword("CAST");
        var INSERT = Keyword("INSERT");
        var INTO = Keyword("INTO");
        var VALUES = Keyword("VALUES");
        var UPDATE = Keyword("UPDATE");
        var SET = Keyword("SET");
        var DELETE = Keyword("DELETE");
        var RETURNING = Keyword("RETURNING");

        var reservedWords = CreateReservedWords(syntax);

        var unquotedIdentifier = Terms.Identifier()
            .Then(span => new SqlIdentifier(span.ToString()))
            .When((_, identifier) =>
                !reservedWords.Contains(identifier.Value)
                && (syntax.DollarSignIsIdentifier || !identifier.Value.Contains('$')));
        var parameterIdentifier = Terms.Identifier()
            .Then(span => new SqlIdentifier(span.ToString()))
            .When((_, identifier) =>
                syntax.DollarSignIsIdentifier || !identifier.Value.Contains('$'));
        var identifierParsers = new List<Parser<SqlIdentifier>>(4);
        if (syntax.IdentifierQuotes.HasFlag(SqlIdentifierQuoteStyle.Brackets))
        {
            identifierParsers.Add(QuotedIdentifier('[', ']'));
        }

        if (syntax.IdentifierQuotes.HasFlag(SqlIdentifierQuoteStyle.DoubleQuote))
        {
            identifierParsers.Add(QuotedIdentifier('"', '"'));
        }

        if (syntax.IdentifierQuotes.HasFlag(SqlIdentifierQuoteStyle.Backtick))
        {
            identifierParsers.Add(QuotedIdentifier('`', '`'));
        }

        identifierParsers.Add(unquotedIdentifier);
        var simpleIdentifier = OneOf(identifierParsers.ToArray());
        var nonKeywordIdentifier = simpleIdentifier;
        var identifierParts = Separated(dot, simpleIdentifier);

        var tableName = identifierParts.Then(parts => new TableName(parts));
        var column = identifierParts.Then<SqlExpression>(parts => new ColumnExpression(parts));

        var expression = Deferred<SqlExpression>();
        var query = Deferred<SqlQuery>();
        var tableSource = Deferred<TableSource>();
        var windowSpecification = Deferred<ParsedWindow>();

        var number = Terms.Decimal().Then<SqlExpression>(value =>
            decimal.Truncate(value) == value
                && (decimal.GetBits(value)[3] & 0x00FF0000) == 0
                && value >= long.MinValue
                && value <= long.MaxValue
                ? new LiteralExpression((long)value)
                : new LiteralExpression(value));
        var text = QuotedString('\'', syntax.SupportsBackslashStringEscapes);
        if (syntax.SupportsDoubleQuotedStrings)
        {
            text = QuotedString('"', syntax.SupportsBackslashStringEscapes).Or(text);
        }
        var boolean = TRUE.Then<SqlExpression>(new LiteralExpression(true))
            .Or(FALSE.Then<SqlExpression>(new LiteralExpression(false)));
        var nullLiteral = NULL.Then<SqlExpression>(new LiteralExpression(null));

        var parameterDefault = text.Or(boolean).Or(nullLiteral).Or(number);
        var parameter = CreateParameterParser(
            parameterIdentifier,
            parameterDefault,
            syntax.ParameterStyles,
            syntax.SupportsParameterDefaults);

        var argumentList = Separated(comma, expression);
        var functionArguments = DISTINCT.Optional()
            .And(argumentList.Or(Always<IReadOnlyList<SqlExpression>>(Array.Empty<SqlExpression>())))
            .Then(value => new ParsedFunctionArguments(value.Item2, value.Item1.HasValue));
        var function = simpleIdentifier
            .And(Between(leftParenthesis, functionArguments, rightParenthesis))
            .And(OVER.SkipAnd(Between(leftParenthesis, windowSpecification, rightParenthesis)).Optional())
            .Then<SqlExpression>(value =>
            {
                SqlExpression result = new FunctionCallExpression(
                    value.Item1,
                    value.Item2.Arguments,
                    value.Item2.IsDistinct);
                return value.Item3.HasValue
                    ? new WindowExpression(
                        result,
                        value.Item3.Value.PartitionBy,
                        value.Item3.Value.OrderBy)
                    : result;
            });

        var dataTypeArguments = Between(
            leftParenthesis,
            Separated(comma, Terms.Decimal().Then(value => checked((int)value))),
            rightParenthesis);
        var dataType = simpleIdentifier.And(dataTypeArguments.Optional())
            .Then(value => new SqlDataType(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : null));
        var cast = CAST.SkipAnd(leftParenthesis)
            .SkipAnd(expression)
            .AndSkip(AS)
            .And(dataType)
            .AndSkip(rightParenthesis)
            .Then<SqlExpression>(value => new CastExpression(value.Item1, value.Item2));

        var whenClause = WHEN.SkipAnd(expression)
            .AndSkip(THEN)
            .And(expression)
            .Then(value => new WhenClause(value.Item1, value.Item2));
        var caseExpression = CASE.SkipAnd(OneOrMany(whenClause))
            .And(ELSE.SkipAnd(expression).Optional())
            .AndSkip(END)
            .Then<SqlExpression>(value => new CaseExpression(
                null,
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : null));

        var exists = NOT.Optional()
            .AndSkip(EXISTS)
            .And(Between(leftParenthesis, query, rightParenthesis))
            .Then<SqlExpression>(value => new ExistsExpression(value.Item2, value.Item1.HasValue));
        var subquery = Between(leftParenthesis, query, rightParenthesis)
            .Then<SqlExpression>(value => new SubqueryExpression(value));
        var grouped = Between(leftParenthesis, expression, rightParenthesis)
            .Then<SqlExpression>(value => new ParenthesizedExpression(value));
        var qualifiedStar = simpleIdentifier.AndSkip(dot).AndSkip(star)
            .Then<SqlExpression>(qualifier => new StarExpression([qualifier]));
        var starExpression = star.Then<SqlExpression>(new StarExpression());

        var term = cast
            .Or(caseExpression)
            .Or(exists)
            .Or(subquery)
            .Or(function)
            .Or(grouped)
            .Or(qualifiedStar)
            .Or(starExpression)
            .Or(parameter)
            .Or(boolean)
            .Or(nullLiteral)
            .Or(text)
            .Or(number)
            .Or(column);

        var unary = Terms.Char('-').And(term)
            .Then<SqlExpression>(value => new UnaryExpression(UnaryOperator.Minus, value.Item2))
            .Or(Terms.Char('+').And(term)
                .Then<SqlExpression>(value => new UnaryExpression(UnaryOperator.Plus, value.Item2)))
            .Or(Terms.Char('~').And(term)
                .Then<SqlExpression>(value => new UnaryExpression(UnaryOperator.BitwiseNot, value.Item2)));
        var primary = unary.Or(term);

        var multiplicative = primary.LeftAssociative(
            (Terms.Char('*'), (left, right) => new BinaryExpression(left, BinaryOperator.Multiply, right)),
            (Terms.Char('/'), (left, right) => new BinaryExpression(left, BinaryOperator.Divide, right)),
            (Terms.Char('%'), (left, right) => new BinaryExpression(left, BinaryOperator.Modulo, right)));

        var additiveOperators = new List<(Parser<int> op, Func<SqlExpression, SqlExpression, SqlExpression> factory)>();
        if (syntax.DoublePipeBehavior == SqlDoublePipeBehavior.Concatenate)
        {
            additiveOperators.Add((
                Terms.Text("||").Then(_ => 0),
                (left, right) => new BinaryExpression(left, BinaryOperator.Concatenate, right)));
        }

        additiveOperators.Add((
            Terms.Char('+').Then(_ => 0),
            (left, right) => new BinaryExpression(left, BinaryOperator.Add, right)));
        additiveOperators.Add((
            Terms.Char('-').Then(_ => 0),
            (left, right) => new BinaryExpression(left, BinaryOperator.Subtract, right)));
        var additive = multiplicative.LeftAssociative(additiveOperators.ToArray());

        var comparison = additive.LeftAssociative(
            (Terms.Text(">=").Then(_ => 0), (left, right) => new BinaryExpression(left, BinaryOperator.GreaterThanOrEqual, right)),
            (Terms.Text("<=").Then(_ => 0), (left, right) => new BinaryExpression(left, BinaryOperator.LessThanOrEqual, right)),
            (Terms.Text("<>").Then(_ => 0), (left, right) => new BinaryExpression(left, BinaryOperator.NotEqual, right)),
            (Terms.Text("!=").Then(_ => 0), (left, right) => new BinaryExpression(left, BinaryOperator.NotEqual, right)),
            (Terms.Char('>').Then(_ => 0), (left, right) => new BinaryExpression(left, BinaryOperator.GreaterThan, right)),
            (Terms.Char('<').Then(_ => 0), (left, right) => new BinaryExpression(left, BinaryOperator.LessThan, right)),
            (Terms.Char('=').Then(_ => 0), (left, right) => new BinaryExpression(left, BinaryOperator.Equal, right)));

        var bitwise = comparison.LeftAssociative(
            (Terms.Char('&'), (left, right) => new BinaryExpression(left, BinaryOperator.BitwiseAnd, right)),
            (Terms.Char('^'), (left, right) => new BinaryExpression(left, BinaryOperator.BitwiseXor, right)),
            (Terms.Char('|'), (left, right) => new BinaryExpression(left, BinaryOperator.BitwiseOr, right)));

        var betweenSuffix = NOT.Optional()
            .AndSkip(BETWEEN)
            .And(bitwise)
            .AndSkip(AND)
            .And(bitwise)
            .Then<Func<SqlExpression, SqlExpression>>(value =>
            {
                var (negated, lower, upper) = value;
                return target => new BetweenExpression(target, lower, upper, negated.HasValue);
            });

        var inValues = Separated(comma, expression)
            .Then(value => new InTarget(value, null));
        var inQuery = query.Then(value => new InTarget(Array.Empty<SqlExpression>(), value));
        var inSuffix = NOT.Optional()
            .AndSkip(IN)
            .And(Between(leftParenthesis, inQuery.Or(inValues), rightParenthesis))
            .Then<Func<SqlExpression, SqlExpression>>(value => target => new InExpression(
                target,
                value.Item2.Values,
                value.Item2.Query,
                value.Item1.HasValue));

        var isNullSuffix = IS.SkipAnd(NOT.Optional())
            .AndSkip(NULL)
            .Then<Func<SqlExpression, SqlExpression>>(negated =>
                target => new IsNullExpression(target, negated.HasValue));

        Parser<string> likeKeyword = LIKE;
        if (syntax.SupportsILike)
        {
            likeKeyword = ILIKE.Or(likeKeyword);
        }

        var likeOperator = NOT.Optional().And(likeKeyword)
            .Then(value => value.Item2.ToString().Equals("ILIKE", StringComparison.OrdinalIgnoreCase)
                ? (value.Item1.HasValue ? BinaryOperator.NotILike : BinaryOperator.ILike)
                : (value.Item1.HasValue ? BinaryOperator.NotLike : BinaryOperator.Like));
        var likeSuffix = likeOperator.And(bitwise)
            .Then<Func<SqlExpression, SqlExpression>>(value =>
                target => new BinaryExpression(target, value.Item1, value.Item2));

        var predicateSuffix = betweenSuffix
            .Or(inSuffix)
            .Or(isNullSuffix)
            .Or(likeSuffix);
        var predicate = bitwise.And(predicateSuffix.Optional())
            .Then<SqlExpression>(value =>
                value.Item2.HasValue ? value.Item2.Value(value.Item1) : value.Item1);
        var notExpression = Deferred<SqlExpression>();
        notExpression.Parser = NOT.And(notExpression)
            .Then<SqlExpression>(value => new UnaryExpression(UnaryOperator.Not, value.Item2))
            .Or(predicate);
        var andExpression = notExpression.LeftAssociative(
            (AND, (left, right) => new BinaryExpression(left, BinaryOperator.And, right)));
        var orOperators = new List<(Parser<string> op, Func<SqlExpression, SqlExpression, SqlExpression> factory)>
        {
            (OR, (left, right) => new BinaryExpression(left, BinaryOperator.Or, right)),
        };
        if (syntax.DoublePipeBehavior == SqlDoublePipeBehavior.LogicalOr)
        {
            orOperators.Add((
                Terms.Text("||"),
                (left, right) => new BinaryExpression(left, BinaryOperator.Or, right)));
        }

        var orExpression = andExpression.LeftAssociative(orOperators.ToArray());
        expression.Parser = orExpression;

        var orderDirection = DESC.Then(OrderDirection.Descending)
            .Or(ASC.Then(OrderDirection.Ascending));
        var nullOrder = syntax.SupportsNullOrdering
            ? NULLS.SkipAnd(FIRST.Then(NullOrder.First).Or(LAST.Then(NullOrder.Last)))
            : Fail<NullOrder>();
        var windowOrderItem = expression.And(orderDirection.Optional()).And(nullOrder.Optional())
            .Then(value => new OrderByItem(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : OrderDirection.Unspecified,
                value.Item3.HasValue ? value.Item3.Value : NullOrder.Unspecified));
        var windowPartitionBy = PARTITION.SkipAnd(BY).SkipAnd(Separated(comma, expression));
        var windowOrderBy = ORDER.SkipAnd(BY).SkipAnd(Separated(comma, windowOrderItem));
        windowSpecification.Parser = windowPartitionBy.Optional()
            .And(windowOrderBy.Optional())
            .Then(value => new ParsedWindow(
                value.Item1.HasValue ? value.Item1.Value : null,
                value.Item2.HasValue ? value.Item2.Value : null));

        var alias = AS.SkipAnd(simpleIdentifier).Or(nonKeywordIdentifier);
        var selectItem = expression.And(alias.Optional())
            .Then(value => new SelectItem(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : null));
        var projections = Separated(comma, selectItem);

        var derivedTable = Between(leftParenthesis, query, rightParenthesis)
            .And(AS.Optional().SkipAnd(nonKeywordIdentifier))
            .Then<TableSource>(value => new DerivedTable(value.Item1, value.Item2));
        var namedTable = tableName.And(alias.Optional())
            .Then<TableSource>(value => new NamedTable(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : null));
        var tablePrimary = derivedTable.Or(namedTable);

        var joinKind = LEFT.AndSkip(OUTER.Optional()).AndSkip(JOIN).Then(JoinKind.Left)
            .Or(RIGHT.AndSkip(OUTER.Optional()).AndSkip(JOIN).Then(JoinKind.Right))
            .Or(FULL.AndSkip(OUTER.Optional()).AndSkip(JOIN).Then(JoinKind.Full))
            .Or(CROSS.AndSkip(JOIN).Then(JoinKind.Cross))
            .Or(INNER.AndSkip(JOIN).Then(JoinKind.Inner))
            .Or(JOIN.Then(JoinKind.Inner));
        var join = joinKind.And(tablePrimary).And(ON.SkipAnd(expression).Optional())
            .Then(value => new ParsedJoin(
                value.Item1,
                value.Item2,
                value.Item3.HasValue ? value.Item3.Value : null,
                JoinSyntax.Explicit));
        var commaTable = comma.SkipAnd(tablePrimary)
            .Then(value => new ParsedJoin(JoinKind.Cross, value, null, JoinSyntax.Comma));
        tableSource.Parser = tablePrimary.And(ZeroOrMany(join.Or(commaTable)))
            .Then(value =>
            {
                TableSource result = value.Item1;
                foreach (var parsedJoin in value.Item2)
                {
                    result = new JoinTable(
                        result,
                        parsedJoin.Right,
                        parsedJoin.Kind,
                        parsedJoin.Condition,
                        parsedJoin.Syntax);
                }

                return result;
            });

        var orderItem = expression.And(orderDirection.Optional()).And(nullOrder.Optional())
            .Then(value => new OrderByItem(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : OrderDirection.Unspecified,
                value.Item3.HasValue ? value.Item3.Value : NullOrder.Unspecified));
        var orderBy = ORDER.SkipAnd(BY).SkipAnd(Separated(comma, orderItem));

        var limit = LIMIT.SkipAnd(expression)
            .And(OFFSET.SkipAnd(expression).Optional())
            .Then(value => new RowLimit(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : null));
        var limitComma = LIMIT.SkipAnd(expression)
            .AndSkip(comma)
            .And(expression)
            .Then(value => new RowLimit(value.Item2, value.Item1));
        var offsetOnly = OFFSET.SkipAnd(expression)
            .Then(value => new RowLimit(null, value));
        var rowOrRows = ROW.Or(ROWS);
        var fetch = FETCH
            .SkipAnd(NEXT.Or(FIRST))
            .SkipAnd(expression)
            .AndSkip(rowOrRows)
            .AndSkip(ONLY);
        var offsetFetch = OFFSET.SkipAnd(expression)
            .AndSkip(rowOrRows)
            .And(fetch.Optional())
            .Then(value => new RowLimit(
                value.Item2.HasValue ? value.Item2.Value : null,
                value.Item1));
        var rowLimitParsers = new List<Parser<RowLimit>>(4);
        if (syntax.SupportsLimitComma) rowLimitParsers.Add(limitComma);
        if (syntax.SupportsLimit) rowLimitParsers.Add(limit);
        if (syntax.SupportsOffsetFetch) rowLimitParsers.Add(offsetFetch);
        if (syntax.SupportsOffsetOnly) rowLimitParsers.Add(offsetOnly);
        Parser<RowLimit?> rowLimit = rowLimitParsers.Count == 0
            ? Always<RowLimit?>(null)
            : OneOf(rowLimitParsers.ToArray())
                .Then<RowLimit?>(value => value)
                .Or(Always<RowLimit?>(null));

        var topExpression = TOP
            .SkipAnd(Between(leftParenthesis, expression, rightParenthesis).Or(expression))
            .And(PERCENT.Optional())
            .And(WITH.SkipAnd(TIES).Optional())
            .Then(value => new ParsedTop(value.Item1, value.Item2.HasValue, value.Item3.HasValue));
        Parser<ParsedTop?> top = syntax.SupportsTop
            ? topExpression.Then<ParsedTop?>(value => value).Or(Always<ParsedTop?>(null))
            : Always<ParsedTop?>(null);
        var from = FROM.SkipAnd(tableSource);
        var groupBy = GROUP.SkipAnd(BY).SkipAnd(Separated(comma, expression));

        var selectCore = SELECT.SkipAnd(DISTINCT.Optional())
            .And(top)
            .And(projections)
            .And(from.Optional())
            .And(WHERE.SkipAnd(expression).Optional())
            .And(groupBy.Optional())
            .And(HAVING.SkipAnd(expression).Optional())
            .Then(value =>
            {
                var (distinct, parsedTop, items, parsedFrom, parsedWhere, parsedGroupBy, parsedHaving) = value;
                return new SelectStatement(
                    items,
                    parsedFrom.HasValue ? parsedFrom.Value : null,
                    parsedWhere.HasValue ? parsedWhere.Value : null,
                    parsedGroupBy.HasValue ? parsedGroupBy.Value : null,
                    parsedHaving.HasValue ? parsedHaving.Value : null,
                    IsDistinct: distinct.HasValue,
                    Top: parsedTop?.Expression,
                    IsTopPercent: parsedTop?.IsPercent ?? false,
                    WithTies: parsedTop?.WithTies ?? false);
            });

        var setOperator = UNION.Then(SetOperator.Union)
            .Or(INTERSECT.Then(SetOperator.Intersect))
            .Or(EXCEPT.Then(SetOperator.Except));
        var setTail = setOperator.And(ALL.Optional()).And(selectCore)
            .Then(value => new SetTail(value.Item1, value.Item3, value.Item2.HasValue));

        var queryBody = selectCore
            .And(ZeroOrMany(setTail))
            .And(orderBy.Optional())
            .And(rowLimit)
            .Then(value =>
            {
                SqlQuery result = value.Item1;
                foreach (var tail in value.Item2)
                {
                    result = new SetOperationStatement(result, tail.Operator, tail.Right, tail.IsAll);
                }

                var parsedOrderBy = value.Item3.HasValue ? value.Item3.Value : null;
                var parsedLimit = value.Item4;
                return ApplyQueryTail(result, parsedOrderBy, parsedLimit);
            });

        var cteColumns = Between(leftParenthesis, Separated(comma, simpleIdentifier), rightParenthesis);
        var cte = simpleIdentifier
            .And(cteColumns.Optional())
            .AndSkip(AS)
            .And(Between(leftParenthesis, query, rightParenthesis))
            .Then(value => new CommonTableExpression(
                value.Item1,
                value.Item3,
                value.Item2.HasValue ? value.Item2.Value : null));
        var with = WITH.SkipAnd(Separated(comma, cte));

        query.Parser = with.Optional().And(queryBody)
            .Then(value => value.Item1.HasValue
                ? ApplyCommonTableExpressions(value.Item2, value.Item1.Value)
                : value.Item2);

        var parsedReturning = RETURNING.SkipAnd(Separated(comma, expression));
        Parser<IReadOnlyList<SqlExpression>?> returning = syntax.SupportsReturning
            ? parsedReturning
                .Then<IReadOnlyList<SqlExpression>?>(value => value)
                .Or(Always<IReadOnlyList<SqlExpression>?>(null))
            : Always<IReadOnlyList<SqlExpression>?>(null);
        var insertColumns = Between(leftParenthesis, Separated(comma, simpleIdentifier), rightParenthesis);
        var valueRow = Between(leftParenthesis, Separated(comma, expression), rightParenthesis);
        var insertValues = VALUES.SkipAnd(Separated(comma, valueRow));
        var insert = INSERT.SkipAnd(INTO)
            .SkipAnd(tableName)
            .And(insertColumns.Optional())
            .And(insertValues.Optional())
            .And(query.Optional())
            .And(returning)
            .When((_, value) => value.Item3.HasValue || value.Item4.HasValue)
            .Then<SqlStatement>(value => new InsertStatement(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : null,
                value.Item3.HasValue ? value.Item3.Value : null,
                value.Item4.HasValue ? value.Item4.Value : null,
                value.Item5));

        var assignment = column.AndSkip(Terms.Char('=')).And(expression)
            .Then(value => new Assignment((ColumnExpression)value.Item1, value.Item2));
        var update = UPDATE.SkipAnd(namedTable)
            .AndSkip(SET)
            .And(Separated(comma, assignment))
            .And(WHERE.SkipAnd(expression).Optional())
            .And(returning)
            .Then<SqlStatement>(value => new UpdateStatement(
                (NamedTable)value.Item1,
                value.Item2,
                value.Item3.HasValue ? value.Item3.Value : null,
                value.Item4));

        var delete = DELETE.SkipAnd(FROM)
            .SkipAnd(namedTable)
            .And(WHERE.SkipAnd(expression).Optional())
            .And(returning)
            .Then<SqlStatement>(value => new DeleteStatement(
                (NamedTable)value.Item1,
                value.Item2.HasValue ? value.Item2.Value : null,
                value.Item3));

        var statement = query.Then<SqlStatement>(value => value)
            .Or(insert)
            .Or(update)
            .Or(delete);
        var document = Separated(semicolon, statement)
            .AndSkip(semicolon.Optional())
            .Then(statements => new SqlDocument(statements))
            .AndSkip(Terms.WhiteSpace().Optional())
            .Eof();

        return document.WithComments(comments => comments
            .WithWhiteSpaceOrNewLine()
            .WithSingleLine("--")
            .WithMultiLine("/*", "*/"))
            .Compile();
    }

    public static SqlDocument Parse(
        string sql,
        SqlDialect? dialect = null,
        SqlParseOptions? options = null)
    {
        if (TryParse(sql, dialect, out var document, out var error, options))
        {
            return document!;
        }

        throw new SqlParseException(error!);
    }

    public static bool TryParse(
        string sql,
        SqlDialect? dialect,
        out SqlDocument? document,
        out SqlParseError? error,
        SqlParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sql);
        options ??= SqlParseOptions.Default;
        var selectedDialect = dialect ?? SqlDialects.Generic;
        var parser = selectedDialect.ParserOptions == SqlDialectParserOptions.Permissive
            ? PermissiveParser
            : ParserCache.GetOrAdd(
                selectedDialect.ParserOptions,
                static syntax => new(
                    () => CreateDocumentParser(syntax),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        if (sql.Length > options.MaximumInputLength)
        {
            document = null;
            error = new SqlParseError(
                $"SQL input exceeds the maximum length of {options.MaximumInputLength}.",
                options.MaximumInputLength,
                1,
                1,
                SqlParseErrorCode.InputTooLarge);
            return false;
        }

        if (!parser.TryParse(sql, out var parsed, out var parseError))
        {
            document = null;
            var message = parseError?.Message ?? "Invalid SQL.";
            var errorCode = SqlParseErrorCode.Syntax;
            if (selectedDialect.ParserOptions != SqlDialectParserOptions.Permissive
                && PermissiveParser.TryParse(sql, out _, out _))
            {
                message = $"SQL syntax is not supported by the '{selectedDialect.Name}' dialect.";
                errorCode = SqlParseErrorCode.DialectIncompatible;
            }

            error = ToParseError(message, parseError, errorCode);
            return false;
        }

        if (parsed.DescendantsAndSelf().Take(options.MaximumAstNodes + 1).Count() > options.MaximumAstNodes)
        {
            document = null;
            error = new SqlParseError(
                $"SQL AST exceeds the maximum node count of {options.MaximumAstNodes}.",
                0,
                1,
                1,
                SqlParseErrorCode.AstTooLarge);
            return false;
        }

        if (TryGetDialectCompatibilityError(parsed, selectedDialect, out var compatibilityError))
        {
            document = null;
            error = new SqlParseError(
                compatibilityError,
                0,
                1,
                1,
                SqlParseErrorCode.DialectIncompatible);
            return false;
        }

        var preprocessed = selectedDialect.Preprocess(parsed);
        if (preprocessed is not SqlDocument parsedDocument)
        {
            throw new InvalidOperationException("A dialect preprocessor must preserve the SqlDocument root node.");
        }

        document = parsedDocument;
        error = null;
        return true;
    }

    private static Parser<string> Keyword(string value) => Terms.Keyword(value, caseInsensitive: true);

    private static HashSet<string> CreateReservedWords(SqlDialectParserOptions syntax)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "DISTINCT", "FROM", "AS", "WHERE", "GROUP", "BY", "HAVING",
            "ORDER", "ASC", "DESC", "UNION", "INTERSECT", "EXCEPT", "ALL", "WITH",
            "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "ON", "AND",
            "OR", "NOT", "BETWEEN", "IN", "IS", "LIKE", "TRUE", "FALSE", "NULL",
            "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END", "CAST", "INSERT", "INTO",
            "VALUES", "UPDATE", "SET", "DELETE",
        };

        if (syntax.SupportsTop)
        {
            words.UnionWith(["TOP", "PERCENT", "TIES"]);
        }

        if (syntax.SupportsLimit || syntax.SupportsLimitComma)
        {
            words.Add("LIMIT");
        }

        if (syntax.SupportsLimit || syntax.SupportsOffsetOnly || syntax.SupportsOffsetFetch)
        {
            words.Add("OFFSET");
        }

        if (syntax.SupportsOffsetFetch)
        {
            words.UnionWith(["ROW", "ROWS", "FETCH", "NEXT", "FIRST", "ONLY"]);
        }

        if (syntax.SupportsReturning) words.Add("RETURNING");
        if (syntax.SupportsILike) words.Add("ILIKE");

        if (syntax.SupportsNullOrdering)
        {
            words.UnionWith(["NULLS", "FIRST", "LAST"]);
        }

        return words;
    }

    private static bool TryGetDialectCompatibilityError(
        SqlDocument document,
        SqlDialect dialect,
        out string error)
    {
        if (dialect.RequiresOrderByForOffset)
        {
            foreach (var select in document.FindAll<SelectStatement>())
            {
                if (select.Offset is not null && select.OrderBy is not { Count: > 0 })
                {
                    error = $"OFFSET requires ORDER BY in the '{dialect.Name}' dialect.";
                    return true;
                }
            }

            foreach (var set in document.FindAll<SetOperationStatement>())
            {
                if (set.Offset is not null && set.OrderBy is not { Count: > 0 })
                {
                    error = $"OFFSET requires ORDER BY in the '{dialect.Name}' dialect.";
                    return true;
                }
            }
        }

        error = "";
        return false;
    }

    private static SqlParseError ToParseError(
        string message,
        ParseError? error,
        SqlParseErrorCode code) =>
        new(
            message,
            error?.Position.Offset ?? 0,
            error?.Position.Line ?? 1,
            error?.Position.Column ?? 1,
            code);

    private static Parser<SqlIdentifier> QuotedIdentifier(char openQuote, char closeQuote)
    {
        var close = closeQuote.ToString();
        var escapedClose = Terms.Text(close + close).Then(_ => close);
        var chunk = Literals.NoneOf(close).Then(value => value.ToString());

        return Between(
                Terms.Char(openQuote),
                ZeroOrMany(escapedClose.Or(chunk)),
                Terms.Char(closeQuote))
            .Then(parts => new SqlIdentifier(string.Concat(parts), true));
    }

    private static Parser<SqlExpression> QuotedString(char quote, bool supportsBackslashEscapes)
    {
        var quoteText = quote.ToString();
        var parts = new List<Parser<string>>
        {
            Terms.Text(quoteText + quoteText).Then(_ => quoteText),
        };

        var excluded = quoteText;
        if (supportsBackslashEscapes)
        {
            excluded += "\\";
            parts.Add(Terms.Char('\\')
                .SkipAnd(Literals.Pattern(static _ => true, 1, 1))
                .Then(value => DecodeEscapedCharacter(value.Span[0]).ToString()));
        }

        parts.Add(Literals.NoneOf(excluded).Then(value => value.ToString()));
        return Between(
                Terms.Char(quote),
                ZeroOrMany(OneOf(parts.ToArray())),
                Terms.Char(quote))
            .Then<SqlExpression>(value => new LiteralExpression(string.Concat(value)));
    }

    private static char DecodeEscapedCharacter(char value) => value switch
    {
        '0' => '\0',
        'b' => '\b',
        'n' => '\n',
        'r' => '\r',
        't' => '\t',
        'Z' => '\u001a',
        _ => value,
    };

    private static Parser<SqlExpression> CreateParameterParser(
        Parser<SqlIdentifier> identifier,
        Parser<SqlExpression> defaultValue,
        SqlParameterStyle styles,
        bool supportsDefaults)
    {
        var parsers = new List<Parser<SqlExpression>>(5);

        if (styles.HasFlag(SqlParameterStyle.QuestionMark))
        {
            parsers.Add(Terms.Char('?').Then<SqlExpression>(new ParameterExpression("", '?')));
        }

        if (styles.HasFlag(SqlParameterStyle.AtNamed))
        {
            parsers.Add(NamedParameter('@', identifier, defaultValue, supportsDefaults));
        }

        if (styles.HasFlag(SqlParameterStyle.ColonNamed))
        {
            parsers.Add(NamedParameter(':', identifier, defaultValue, supportsDefaults));
        }

        if (styles.HasFlag(SqlParameterStyle.DollarNumbered))
        {
            parsers.Add(Terms.Char('$')
                .SkipAnd(Terms.Integer())
                .Then<SqlExpression>(value => new ParameterExpression(
                    value.ToString(CultureInfo.InvariantCulture),
                    '$')));
        }

        if (styles.HasFlag(SqlParameterStyle.DollarNamed))
        {
            parsers.Add(NamedParameter('$', identifier, defaultValue, supportsDefaults));
        }

        return parsers.Count == 0 ? Fail<SqlExpression>() : OneOf(parsers.ToArray());
    }

    private static Parser<SqlExpression> NamedParameter(
        char prefix,
        Parser<SqlIdentifier> identifier,
        Parser<SqlExpression> defaultValue,
        bool supportsDefaults)
    {
        var name = Terms.Char(prefix).SkipAnd(identifier);
        if (!supportsDefaults)
        {
            return name.Then<SqlExpression>(value => new ParameterExpression(value.Value, prefix));
        }

        return name
            .And(Terms.Char(':').SkipAnd(defaultValue).Optional())
            .Then<SqlExpression>(value => new ParameterExpression(
                value.Item1.Value,
                prefix,
                value.Item2.HasValue ? value.Item2.Value : null));
    }

    private static SqlQuery ApplyQueryTail(
        SqlQuery query,
        IReadOnlyList<OrderByItem>? orderBy,
        RowLimit? rowLimit) =>
        query switch
        {
            SelectStatement select => select with
            {
                OrderBy = orderBy,
                Limit = rowLimit?.Limit,
                Offset = rowLimit?.Offset,
            },
            SetOperationStatement set => set with
            {
                OrderBy = orderBy,
                Limit = rowLimit?.Limit,
                Offset = rowLimit?.Offset,
            },
            _ => query,
        };

    private static SqlQuery ApplyCommonTableExpressions(
        SqlQuery query,
        IReadOnlyList<CommonTableExpression> commonTableExpressions) =>
        query switch
        {
            SelectStatement select => select with { CommonTableExpressions = commonTableExpressions },
            SetOperationStatement set => set with
            {
                Left = ApplyCommonTableExpressions(set.Left, commonTableExpressions),
            },
            _ => query,
        };

    private sealed record InTarget(IReadOnlyList<SqlExpression> Values, SqlQuery? Query);

    private sealed record ParsedJoin(
        JoinKind Kind,
        TableSource Right,
        SqlExpression? Condition,
        JoinSyntax Syntax);

    private sealed record ParsedFunctionArguments(IReadOnlyList<SqlExpression> Arguments, bool IsDistinct);

    private sealed record ParsedWindow(
        IReadOnlyList<SqlExpression>? PartitionBy,
        IReadOnlyList<OrderByItem>? OrderBy);

    private sealed record ParsedTop(SqlExpression Expression, bool IsPercent, bool WithTies);

    private sealed record SetTail(SetOperator Operator, SelectStatement Right, bool IsAll);

    private sealed record RowLimit(SqlExpression? Limit, SqlExpression? Offset);
}

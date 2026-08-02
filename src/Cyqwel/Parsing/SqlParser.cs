using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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
        var UNKNOWN = Keyword("UNKNOWN");
        var DEFAULT = Keyword("DEFAULT");
        var COLLATE = Keyword("COLLATE");
        var EXTRACT = Keyword("EXTRACT");
        var INTERVAL = Keyword("INTERVAL");
        var TRY_CAST = Keyword("TRY_CAST");
        var FILTER = Keyword("FILTER");
        var WITHIN = Keyword("WITHIN");
        var WINDOW = Keyword("WINDOW");
        var RANGE = Keyword("RANGE");
        var GROUPS = Keyword("GROUPS");
        var UNBOUNDED = Keyword("UNBOUNDED");
        var PRECEDING = Keyword("PRECEDING");
        var CURRENT = Keyword("CURRENT");
        var FOLLOWING = Keyword("FOLLOWING");
        var UNION = Keyword("UNION");
        var INTERSECT = Keyword("INTERSECT");
        var EXCEPT = Keyword("EXCEPT");
        var MINUS = Keyword("MINUS");
        var ALL = Keyword("ALL");
        var WITH = Keyword("WITH");
        var RECURSIVE = Keyword("RECURSIVE");
        var MATERIALIZED = Keyword("MATERIALIZED");
        var JOIN = Keyword("JOIN");
        var INNER = Keyword("INNER");
        var LEFT = Keyword("LEFT");
        var RIGHT = Keyword("RIGHT");
        var FULL = Keyword("FULL");
        var CROSS = Keyword("CROSS");
        var OUTER = Keyword("OUTER");
        var ON = Keyword("ON");
        var USING = Keyword("USING");
        var NATURAL = Keyword("NATURAL");
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
        var MERGE = Keyword("MERGE");
        var MATCHED = Keyword("MATCHED");
        var SOURCE = Keyword("SOURCE");
        var CONNECT = Keyword("CONNECT");
        var START = Keyword("START");
        var NOCYCLE = Keyword("NOCYCLE");
        var PRIOR = Keyword("PRIOR");
        var CONNECT_BY_ROOT = Keyword("CONNECT_BY_ROOT");
        var SIBLINGS = Keyword("SIBLINGS");
        var CREATE = Keyword("CREATE");
        var ALTER = Keyword("ALTER");
        var DROP = Keyword("DROP");
        var TRUNCATE = Keyword("TRUNCATE");
        var TABLE = Keyword("TABLE");
        var TEMPORARY = Keyword("TEMPORARY");
        var IF = Keyword("IF");
        var REPLACE = Keyword("REPLACE");
        var VIEW = Keyword("VIEW");
        var INDEX = Keyword("INDEX");
        var UNIQUE = Keyword("UNIQUE");
        var SEQUENCE = Keyword("SEQUENCE");
        var COLUMN = Keyword("COLUMN");
        var CONSTRAINT = Keyword("CONSTRAINT");
        var PRIMARY = Keyword("PRIMARY");
        var KEY = Keyword("KEY");
        var FOREIGN = Keyword("FOREIGN");
        var REFERENCES = Keyword("REFERENCES");
        var CHECK = Keyword("CHECK");
        var CASCADE = Keyword("CASCADE");
        var RESTRICT = Keyword("RESTRICT");
        var NO = Keyword("NO");
        var ACTION = Keyword("ACTION");
        var RENAME = Keyword("RENAME");
        var TO = Keyword("TO");
        var TYPE = Keyword("TYPE");
        var GENERATED = Keyword("GENERATED");
        var ALWAYS = Keyword("ALWAYS");
        var BY_DEFAULT = BY.SkipAnd(DEFAULT);
        var IDENTITY = Keyword("IDENTITY");
        var VIRTUAL = Keyword("VIRTUAL");
        var STORED = Keyword("STORED");
        var ADD = Keyword("ADD");
        var CYCLE = Keyword("CYCLE");
        var CACHE = Keyword("CACHE");
        var MINVALUE = Keyword("MINVALUE");
        var MAXVALUE = Keyword("MAXVALUE");
        var INCREMENT = Keyword("INCREMENT");

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
        var column = identifierParts.Then<SqlExpression>(parts =>
        {
            if (parts.Count > 1
                && parts[^1].Value.Equals("NEXTVAL", StringComparison.OrdinalIgnoreCase))
            {
                return new SequenceValueExpression(
                    new TableName(parts.Take(parts.Count - 1).ToArray()),
                    SequenceValueKind.Next);
            }

            if (parts.Count > 1
                && parts[^1].Value.Equals("CURRVAL", StringComparison.OrdinalIgnoreCase))
            {
                return new SequenceValueExpression(
                    new TableName(parts.Take(parts.Count - 1).ToArray()),
                    SequenceValueKind.Current);
            }

            return new ColumnExpression(parts);
        });

        var expression = Deferred<SqlExpression>();
        var query = Deferred<SqlQuery>();
        var tableSource = Deferred<TableSource>();
        var windowSpecification = Deferred<ParsedWindow>();
        var withinGroupOrderBy = Deferred<IReadOnlyList<OrderByItem>>();

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
        var functionCore = simpleIdentifier
            .And(Between(leftParenthesis, functionArguments, rightParenthesis))
            .Then(value => new FunctionCallExpression(
                value.Item1,
                value.Item2.Arguments,
                value.Item2.IsDistinct));
        var withinGroup = WITHIN.SkipAnd(GROUP)
            .SkipAnd(Between(leftParenthesis, withinGroupOrderBy, rightParenthesis));
        var functionFilter = FILTER.SkipAnd(Between(
            leftParenthesis,
            WHERE.SkipAnd(expression),
            rightParenthesis));
        var function = functionCore
            .And(withinGroup.Optional())
            .And(functionFilter.Optional())
            .And(OVER.SkipAnd(Between(leftParenthesis, windowSpecification, rightParenthesis)).Optional())
            .Then<SqlExpression>(value =>
            {
                var call = value.Item1 with
                {
                    WithinGroup = value.Item2.HasValue ? value.Item2.Value : null,
                    Filter = value.Item3.HasValue ? value.Item3.Value : null,
                };
                return value.Item4.HasValue
                    ? new WindowExpression(
                        call,
                        value.Item4.Value.PartitionBy,
                        value.Item4.Value.OrderBy,
                        value.Item4.Value.Frame,
                        value.Item4.Value.WindowName)
                    : call;
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
        var tryCast = TRY_CAST.SkipAnd(leftParenthesis)
            .SkipAnd(expression)
            .AndSkip(AS)
            .And(dataType)
            .AndSkip(rightParenthesis)
            .Then<SqlExpression>(value => new TryCastExpression(value.Item1, value.Item2));
        var extract = EXTRACT.SkipAnd(leftParenthesis)
            .SkipAnd(simpleIdentifier)
            .AndSkip(FROM)
            .And(expression)
            .AndSkip(rightParenthesis)
            .Then<SqlExpression>(value => new ExtractExpression(value.Item1, value.Item2));
        var interval = INTERVAL.SkipAnd(text.Or(number).Or(parameter))
            .And(simpleIdentifier)
            .Then<SqlExpression>(value => new IntervalExpression(value.Item1, value.Item2));

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
        var row = ROW.SkipAnd(Between(leftParenthesis, Separated(comma, expression), rightParenthesis))
            .Then<SqlExpression>(values => new RowExpression(values));
        var grouped = Between(leftParenthesis, expression, rightParenthesis)
            .Then<SqlExpression>(value => new ParenthesizedExpression(value));
        var qualifiedStar = simpleIdentifier.AndSkip(dot).AndSkip(star)
            .Then<SqlExpression>(qualifier => new StarExpression([qualifier]));
        var starExpression = star.Then<SqlExpression>(new StarExpression());

        var defaultExpression = DEFAULT.Then<SqlExpression>(new DefaultExpression());
        var term = tryCast
            .Or(cast)
            .Or(extract)
            .Or(interval)
            .Or(caseExpression)
            .Or(exists)
            .Or(subquery)
            .Or(function)
            .Or(row)
            .Or(grouped)
            .Or(qualifiedStar)
            .Or(starExpression)
            .Or(parameter)
            .Or(boolean)
            .Or(nullLiteral)
            .Or(defaultExpression)
            .Or(text)
            .Or(number)
            .Or(column);

        Parser<SqlExpression> unary = Terms.Char('-').And(term)
            .Then<SqlExpression>(value => new UnaryExpression(UnaryOperator.Minus, value.Item2))
            .Or(Terms.Char('+').And(term)
                .Then<SqlExpression>(value => new UnaryExpression(UnaryOperator.Plus, value.Item2)))
            .Or(Terms.Char('~').And(term)
                .Then<SqlExpression>(value => new UnaryExpression(UnaryOperator.BitwiseNot, value.Item2)));
        if (syntax.SupportsHierarchicalQueries)
        {
            unary = PRIOR.SkipAnd(term)
                .Then<SqlExpression>(value => new UnaryExpression(UnaryOperator.Prior, value))
                .Or(CONNECT_BY_ROOT.SkipAnd(term)
                    .Then<SqlExpression>(value => new UnaryExpression(UnaryOperator.ConnectByRoot, value)))
                .Or(unary);
        }

        var primary = unary.Or(term);
        var collated = primary
            .And(COLLATE.SkipAnd(simpleIdentifier).Optional())
            .Then<SqlExpression>(value => value.Item2.HasValue
                ? new CollateExpression(value.Item1, value.Item2.Value)
                : value.Item1);

        var multiplicative = collated.LeftAssociative(
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
        var booleanTestKind = TRUE.Then(BooleanTestKind.True)
            .Or(FALSE.Then(BooleanTestKind.False))
            .Or(UNKNOWN.Then(BooleanTestKind.Unknown));
        var booleanTestSuffix = IS.SkipAnd(NOT.Optional())
            .And(booleanTestKind)
            .Then<Func<SqlExpression, SqlExpression>>(value =>
                target => new BooleanTestExpression(target, value.Item2, value.Item1.HasValue));
        var distinctFromSuffix = IS.SkipAnd(NOT.Optional())
            .AndSkip(DISTINCT)
            .AndSkip(FROM)
            .And(bitwise)
            .Then<Func<SqlExpression, SqlExpression>>(value =>
                target => new DistinctFromExpression(target, value.Item2, value.Item1.HasValue));

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
            .Or(booleanTestSuffix)
            .Or(distinctFromSuffix)
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
        withinGroupOrderBy.Parser = windowOrderBy;
        var unboundedPreceding = UNBOUNDED.SkipAnd(PRECEDING)
            .Then(new WindowFrameBound(WindowFrameBoundKind.UnboundedPreceding));
        var offsetPreceding = expression.AndSkip(PRECEDING)
            .Then(value => new WindowFrameBound(WindowFrameBoundKind.Preceding, value));
        var currentRow = CURRENT.SkipAnd(ROW)
            .Then(new WindowFrameBound(WindowFrameBoundKind.CurrentRow));
        var offsetFollowing = expression.AndSkip(FOLLOWING)
            .Then(value => new WindowFrameBound(WindowFrameBoundKind.Following, value));
        var unboundedFollowing = UNBOUNDED.SkipAnd(FOLLOWING)
            .Then(new WindowFrameBound(WindowFrameBoundKind.UnboundedFollowing));
        var windowFrameBound = unboundedPreceding
            .Or(currentRow)
            .Or(unboundedFollowing)
            .Or(offsetPreceding)
            .Or(offsetFollowing);
        var windowFrameUnit = ROWS.Then(WindowFrameUnit.Rows)
            .Or(RANGE.Then(WindowFrameUnit.Range))
            .Or(GROUPS.Then(WindowFrameUnit.Groups));
        var betweenFrame = BETWEEN.SkipAnd(windowFrameBound)
            .AndSkip(AND)
            .And(windowFrameBound);
        var windowFrame = windowFrameUnit
            .And(betweenFrame
                .Then(value => (Start: value.Item1, End: (WindowFrameBound?)value.Item2))
                .Or(windowFrameBound.Then(value => (Start: value, End: (WindowFrameBound?)null))))
            .Then(value => new WindowFrame(value.Item1, value.Item2.Start, value.Item2.End));
        windowSpecification.Parser = windowPartitionBy.Optional()
            .And(windowOrderBy.Optional())
            .And(windowFrame.Optional())
            .Then(value => new ParsedWindow(
                value.Item1.HasValue ? value.Item1.Value : null,
                value.Item2.HasValue ? value.Item2.Value : null,
                value.Item3.HasValue ? value.Item3.Value : null,
                null));

        var alias = AS.SkipAnd(simpleIdentifier).Or(nonKeywordIdentifier);
        var tableAlias = syntax.SupportsTableAliasAs
            ? alias
            : nonKeywordIdentifier;
        var selectItem = expression.And(alias.Optional())
            .Then(value => new SelectItem(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : null));
        var projections = Separated(comma, selectItem);

        var derivedTable = Between(leftParenthesis, query, rightParenthesis)
            .And(tableAlias)
            .Then<TableSource>(value => new DerivedTable(value.Item1, value.Item2));
        var namedTable = tableName.And(tableAlias.Optional())
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
        var joinCondition = ON.SkipAnd(expression)
            .Then(value => new ParsedJoinCondition(value, null))
            .Or(USING.SkipAnd(Between(leftParenthesis, Separated(comma, simpleIdentifier), rightParenthesis))
                .Then(value => new ParsedJoinCondition(null, value)));
        var join = NATURAL.Optional().And(joinKind).And(tablePrimary).And(joinCondition.Optional())
            .Then(value => new ParsedJoin(
                value.Item2,
                value.Item3,
                value.Item4.HasValue ? value.Item4.Value.Condition : null,
                JoinSyntax.Explicit,
                value.Item4.HasValue ? value.Item4.Value.Using : null,
                value.Item1.HasValue));
        var commaTable = comma.SkipAnd(tablePrimary)
            .Then(value => new ParsedJoin(JoinKind.Cross, value, null, JoinSyntax.Comma, null, false));
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
                        parsedJoin.Syntax,
                        parsedJoin.Using,
                        parsedJoin.IsNatural);
                }

                return result;
            });

        var orderItem = expression.And(orderDirection.Optional()).And(nullOrder.Optional())
            .Then(value => new OrderByItem(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : OrderDirection.Unspecified,
                value.Item3.HasValue ? value.Item3.Value : NullOrder.Unspecified));
        var orderBy = ORDER.SkipAnd(SIBLINGS.Optional())
            .AndSkip(BY)
            .And(Separated(comma, orderItem))
            .Then(value => new ParsedOrderBy(value.Item2, value.Item1.HasValue));

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
        var fetchOnly = FETCH
            .SkipAnd(NEXT.Or(FIRST))
            .SkipAnd(expression)
            .AndSkip(rowOrRows)
            .AndSkip(ONLY)
            .Then(value => new RowLimit(value, null));
        var rowLimitParsers = new List<Parser<RowLimit>>(4);
        if (syntax.SupportsLimitComma) rowLimitParsers.Add(limitComma);
        if (syntax.SupportsLimit) rowLimitParsers.Add(limit);
        if (syntax.SupportsOffsetFetch)
        {
            rowLimitParsers.Add(offsetFetch);
            rowLimitParsers.Add(fetchOnly);
        }
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
        var windowDefinition = simpleIdentifier
            .AndSkip(AS)
            .And(Between(leftParenthesis, windowSpecification, rightParenthesis))
            .Then(value => new WindowDefinition(
                value.Item1,
                value.Item2.WindowName,
                value.Item2.PartitionBy,
                value.Item2.OrderBy,
                value.Item2.Frame));
        var windows = WINDOW.SkipAnd(Separated(comma, windowDefinition));
        var connectBy = syntax.SupportsHierarchicalQueries
            ? START.SkipAnd(WITH).SkipAnd(expression).Optional()
                .And(CONNECT.SkipAnd(BY).SkipAnd(NOCYCLE.Optional()).And(expression))
                .Then(value => new ConnectByClause(
                    value.Item2.Item2,
                    value.Item1.HasValue ? value.Item1.Value : null,
                    value.Item2.Item1.HasValue))
                .Then<ConnectByClause?>(value => value)
                .Or(Always<ConnectByClause?>(null))
            : Always<ConnectByClause?>(null);

        var selectHead = SELECT.SkipAnd(DISTINCT.Optional())
            .And(top)
            .And(projections)
            .And(from.Optional())
            .And(WHERE.SkipAnd(expression).Optional())
            .And(groupBy.Optional())
            .And(HAVING.SkipAnd(expression).Optional())
            .Then(value => new ParsedSelectHead(
                value.Item1.HasValue,
                value.Item2,
                value.Item3,
                value.Item4.HasValue ? value.Item4.Value : null,
                value.Item5.HasValue ? value.Item5.Value : null,
                value.Item6.HasValue ? value.Item6.Value : null,
                value.Item7.HasValue ? value.Item7.Value : null));
        var selectCore = selectHead
            .And(windows.Optional())
            .And(Keyword("QUALIFY").SkipAnd(expression).Optional())
            .And(connectBy)
            .Then(value =>
            {
                var (head, parsedWindows, parsedQualify, parsedConnectBy) = value;
                return new SelectStatement(
                    head.Items,
                    head.From,
                    head.Where,
                    head.GroupBy,
                    head.Having,
                    IsDistinct: head.IsDistinct,
                    Top: head.Top?.Expression,
                    IsTopPercent: head.Top?.IsPercent ?? false,
                    WithTies: head.Top?.WithTies ?? false,
                    Windows: parsedWindows.HasValue ? parsedWindows.Value : null,
                    Qualify: parsedQualify.HasValue ? parsedQualify.Value : null,
                    ConnectBy: parsedConnectBy);
            });

        var setOperator = UNION.Then(SetOperator.Union)
            .Or(INTERSECT.Then(SetOperator.Intersect))
            .Or(EXCEPT.Then(SetOperator.Except));
        if (syntax.SupportsMinus)
        {
            setOperator = MINUS.Then(SetOperator.Except).Or(setOperator);
        }

        var valueRow = Between(leftParenthesis, Separated(comma, expression), rightParenthesis);
        var valuesCore = VALUES.SkipAnd(Separated(comma, valueRow))
            .Then<SqlQuery>(rows => new ValuesStatement(rows));
        var queryPrimary = selectCore.Then<SqlQuery>(value => value).Or(valuesCore);
        var setTail = setOperator.And(ALL.Optional()).And(queryPrimary)
            .Then(value => new SetTail(value.Item1, value.Item3, value.Item2.HasValue));

        var queryBody = queryPrimary
            .And(ZeroOrMany(setTail))
            .And(orderBy.Optional())
            .And(rowLimit)
            .Then(value =>
            {
                var result = BuildSetOperation(value.Item1, value.Item2);

                var parsedOrderBy = value.Item3.HasValue ? value.Item3.Value : null;
                var parsedLimit = value.Item4;
                return ApplyQueryTail(result, parsedOrderBy, parsedLimit);
            });

        var cteColumns = Between(leftParenthesis, Separated(comma, simpleIdentifier), rightParenthesis);
        var materialization = NOT.SkipAnd(MATERIALIZED).Then(CteMaterialization.NotMaterialized)
            .Or(MATERIALIZED.Then(CteMaterialization.Materialized));
        var cte = simpleIdentifier
            .And(cteColumns.Optional())
            .AndSkip(AS)
            .And(materialization.Optional())
            .And(Between(leftParenthesis, query, rightParenthesis))
            .Then(value => new CommonTableExpression(
                value.Item1,
                value.Item4,
                value.Item2.HasValue ? value.Item2.Value : null,
                value.Item3.HasValue ? value.Item3.Value : CteMaterialization.Unspecified));
        Parser<bool> recursive = syntax.SupportsRecursiveCte
            ? RECURSIVE.Optional().Then(value => value.HasValue)
            : Always(false);
        var with = WITH.SkipAnd(recursive)
            .And(Separated(comma, cte))
            .Then(value => new ParsedWith(value.Item2, value.Item1));

        query.Parser = with.Optional().And(queryBody)
            .Then(value => value.Item1.HasValue
                ? ApplyCommonTableExpressions(
                    value.Item2,
                    value.Item1.Value.Expressions,
                    value.Item1.Value.IsRecursive)
                : value.Item2);

        var parsedReturning = RETURNING.SkipAnd(Separated(comma, expression))
            .And(INTO.SkipAnd(Separated(comma, expression)).Optional())
            .Then(value => new ParsedReturning(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : null));
        Parser<ParsedReturning?> returning = syntax.SupportsReturning
            ? parsedReturning
                .When((_, value) => value.Into is null || syntax.SupportsReturningInto)
                .Then<ParsedReturning?>(value => value)
                .Or(Always<ParsedReturning?>(null))
            : Always<ParsedReturning?>(null);
        var insertColumns = Between(leftParenthesis, Separated(comma, simpleIdentifier), rightParenthesis);
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
                value.Item5?.Expressions,
                value.Item5?.Into));

        var assignment = column.AndSkip(Terms.Char('=')).And(expression)
            .Then(value => new Assignment((ColumnExpression)value.Item1, value.Item2));
        var update = UPDATE.SkipAnd(namedTable)
            .AndSkip(SET)
            .And(Separated(comma, assignment))
            .And(FROM.SkipAnd(tableSource).Optional())
            .And(WHERE.SkipAnd(expression).Optional())
            .And(returning)
            .Then<SqlStatement>(value => new UpdateStatement(
                (NamedTable)value.Item1,
                value.Item2,
                value.Item4.HasValue ? value.Item4.Value : null,
                value.Item5?.Expressions,
                value.Item5?.Into,
                value.Item3.HasValue ? value.Item3.Value : null));

        var delete = DELETE.SkipAnd(FROM)
            .SkipAnd(namedTable)
            .And(USING.SkipAnd(tableSource).Optional())
            .And(WHERE.SkipAnd(expression).Optional())
            .And(returning)
            .Then<SqlStatement>(value => new DeleteStatement(
                (NamedTable)value.Item1,
                value.Item3.HasValue ? value.Item3.Value : null,
                value.Item4?.Expressions,
                value.Item4?.Into,
                value.Item2.HasValue ? value.Item2.Value : null));

        var mergeUpdate = UPDATE.SkipAnd(SET)
            .SkipAnd(Separated(comma, assignment))
            .And(DELETE.SkipAnd(WHERE).SkipAnd(expression).Optional())
            .Then<MergeAction>(value => new MergeUpdateAction(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : null));
        var mergeInsert = INSERT
            .SkipAnd(insertColumns.Optional())
            .AndSkip(VALUES)
            .And(valueRow)
            .Then<MergeAction>(value => new MergeInsertAction(
                value.Item1.HasValue ? value.Item1.Value : null,
                value.Item2));
        var mergeDelete = DELETE.Then<MergeAction>(new MergeDeleteAction());
        var mergeMatchKind = MATCHED.Then(MergeMatchKind.Matched)
            .Or(NOT.SkipAnd(MATCHED)
                .And(BY.SkipAnd(SOURCE).Optional())
                .Then(value => value.Item2.HasValue
                    ? MergeMatchKind.NotMatchedBySource
                    : MergeMatchKind.NotMatched));
        var mergeWhen = WHEN.SkipAnd(mergeMatchKind)
            .And(AND.SkipAnd(expression).Optional())
            .AndSkip(THEN)
            .And(mergeUpdate.Or(mergeInsert).Or(mergeDelete))
            .Then(value => new MergeWhenClause(
                value.Item1,
                value.Item3,
                value.Item2.HasValue ? value.Item2.Value : null));
        var merge = MERGE.SkipAnd(INTO)
            .SkipAnd(namedTable)
            .AndSkip(USING)
            .And(tableSource)
            .AndSkip(ON)
            .And(expression)
            .And(OneOrMany(mergeWhen))
            .And(returning)
            .Then<SqlStatement>(value => new MergeStatement(
                (NamedTable)value.Item1,
                value.Item2,
                value.Item3,
                value.Item4,
                value.Item5?.Expressions,
                value.Item5?.Into));

        var columnModifier = DEFAULT.SkipAnd(expression)
            .Then(value => new ParsedColumnModifier(ParsedColumnModifierKind.Default, value))
            .Or(GENERATED.SkipAnd(ALWAYS)
                .SkipAnd(AS)
                .SkipAnd(IDENTITY)
                .Then(new ParsedColumnModifier(
                    ParsedColumnModifierKind.Identity,
                    Identity: IdentityGeneration.Always)))
            .Or(GENERATED.SkipAnd(BY_DEFAULT)
                .SkipAnd(AS)
                .SkipAnd(IDENTITY)
                .Then(new ParsedColumnModifier(
                    ParsedColumnModifierKind.Identity,
                    Identity: IdentityGeneration.ByDefault)))
            .Or(GENERATED.SkipAnd(ALWAYS)
                .SkipAnd(AS)
                .SkipAnd(Between(leftParenthesis, expression, rightParenthesis))
                .And(STORED.Then(GeneratedColumnKind.Stored)
                    .Or(VIRTUAL.Then(GeneratedColumnKind.Virtual))
                    .Optional())
                .Then(value => new ParsedColumnModifier(
                    ParsedColumnModifierKind.Generated,
                    value.Item1,
                    value.Item2.HasValue ? value.Item2.Value : GeneratedColumnKind.Virtual)))
            .Or(NOT.SkipAnd(NULL)
                .Then(new ParsedColumnModifier(ParsedColumnModifierKind.NotNull)))
            .Or(NULL.Then(new ParsedColumnModifier(ParsedColumnModifierKind.Null)))
            .Or(PRIMARY.SkipAnd(KEY)
                .Then(new ParsedColumnModifier(ParsedColumnModifierKind.PrimaryKey)))
            .Or(UNIQUE.Then(new ParsedColumnModifier(ParsedColumnModifierKind.Unique)))
            .Or(IDENTITY.Then(new ParsedColumnModifier(
                ParsedColumnModifierKind.Identity,
                Identity: IdentityGeneration.ByDefault)));
        var columnDefinition = simpleIdentifier
            .And(dataType)
            .And(ZeroOrMany(columnModifier))
            .Then(value =>
            {
                var nullability = Nullability.Unspecified;
                SqlExpression? defaultValue = null;
                SqlExpression? generated = null;
                var generatedKind = GeneratedColumnKind.Virtual;
                var identity = IdentityGeneration.None;
                var isPrimaryKey = false;
                var isUnique = false;
                foreach (var modifier in value.Item3)
                {
                    if (modifier.Kind == ParsedColumnModifierKind.Null)
                    {
                        nullability = Nullability.Null;
                    }
                    else if (modifier.Kind == ParsedColumnModifierKind.NotNull)
                    {
                        nullability = Nullability.NotNull;
                    }
                    else if (modifier.Kind == ParsedColumnModifierKind.Default)
                    {
                        defaultValue = modifier.Expression;
                    }
                    else if (modifier.Kind == ParsedColumnModifierKind.Generated)
                    {
                        generated = modifier.Expression;
                        generatedKind = modifier.GeneratedKind;
                    }
                    else if (modifier.Kind == ParsedColumnModifierKind.Identity)
                    {
                        identity = modifier.Identity;
                    }
                    else if (modifier.Kind == ParsedColumnModifierKind.PrimaryKey)
                    {
                        isPrimaryKey = true;
                    }
                    else
                    {
                        isUnique = true;
                    }
                }

                return new ColumnDefinition(
                    value.Item1,
                    value.Item2,
                    nullability,
                    defaultValue,
                    generated,
                    generatedKind,
                    identity,
                    isPrimaryKey,
                    isUnique);
            });

        var identifierList = Between(
            leftParenthesis,
            Separated(comma, simpleIdentifier),
            rightParenthesis);
        var constraintName = CONSTRAINT.SkipAnd(simpleIdentifier).Optional();
        var primaryKeyConstraint = constraintName
            .And(PRIMARY.SkipAnd(KEY).SkipAnd(identifierList))
            .Then<TableConstraint>(value => new PrimaryKeyConstraint(
                value.Item2,
                value.Item1.HasValue ? value.Item1.Value : null));
        var uniqueConstraint = constraintName
            .And(UNIQUE.SkipAnd(identifierList))
            .Then<TableConstraint>(value => new UniqueConstraint(
                value.Item2,
                value.Item1.HasValue ? value.Item1.Value : null));
        var foreignKeyConstraint = constraintName
            .And(FOREIGN.SkipAnd(KEY).SkipAnd(identifierList))
            .AndSkip(REFERENCES)
            .And(tableName)
            .And(identifierList)
            .Then<TableConstraint>(value => new ForeignKeyConstraint(
                value.Item2,
                value.Item3,
                value.Item4,
                ReferentialAction.Unspecified,
                ReferentialAction.Unspecified,
                value.Item1.HasValue ? value.Item1.Value : null));
        var checkConstraint = constraintName
            .And(CHECK.SkipAnd(Between(leftParenthesis, expression, rightParenthesis)))
            .Then<TableConstraint>(value => new CheckConstraint(
                value.Item2,
                value.Item1.HasValue ? value.Item1.Value : null));
        var tableConstraint = primaryKeyConstraint
            .Or(uniqueConstraint)
            .Or(foreignKeyConstraint)
            .Or(checkConstraint);
        var tableElement = tableConstraint.Then<TableElement>(value => value)
            .Or(columnDefinition.Then<TableElement>(value => value));
        var tableElements = Between(
            leftParenthesis,
            Separated(comma, tableElement),
            rightParenthesis);
        var ifNotExists = IF.SkipAnd(NOT).SkipAnd(EXISTS).Optional();
        var createTable = CREATE.SkipAnd(TEMPORARY.Optional())
            .AndSkip(TABLE)
            .And(ifNotExists)
            .And(tableName)
            .And(tableElements.Optional())
            .And(AS.SkipAnd(query).Optional())
            .Then<SqlStatement>(value => new CreateTableStatement(
                value.Item3,
                value.Item4.HasValue ? value.Item4.Value : Array.Empty<TableElement>(),
                value.Item2.HasValue,
                value.Item1.HasValue,
                value.Item5.HasValue ? value.Item5.Value : null));

        var createView = CREATE.SkipAnd(OR.SkipAnd(REPLACE).Optional())
            .And(TEMPORARY.Optional())
            .AndSkip(VIEW)
            .And(tableName)
            .And(identifierList.Optional())
            .AndSkip(AS)
            .And(query)
            .Then<SqlStatement>(value => new CreateViewStatement(
                value.Item3,
                value.Item5,
                value.Item4.HasValue ? value.Item4.Value : null,
                value.Item1.HasValue,
                value.Item2.HasValue));

        var indexColumn = expression.And(orderDirection.Optional()).And(nullOrder.Optional())
            .Then(value => new IndexColumn(
                value.Item1,
                value.Item2.HasValue ? value.Item2.Value : OrderDirection.Unspecified,
                value.Item3.HasValue ? value.Item3.Value : NullOrder.Unspecified));
        var createIndex = CREATE.SkipAnd(UNIQUE.Optional())
            .AndSkip(INDEX)
            .And(ifNotExists)
            .And(tableName)
            .AndSkip(ON)
            .And(tableName)
            .And(Between(leftParenthesis, Separated(comma, indexColumn), rightParenthesis))
            .And(WHERE.SkipAnd(expression).Optional())
            .Then<SqlStatement>(value => new CreateIndexStatement(
                value.Item3,
                value.Item4,
                value.Item5,
                value.Item1.HasValue,
                value.Item2.HasValue,
                value.Item6.HasValue ? value.Item6.Value : null));

        var sequenceOption = START.SkipAnd(WITH).SkipAnd(expression)
            .Then(value => new ParsedSequenceOption(ParsedSequenceOptionKind.Start, value))
            .Or(INCREMENT.SkipAnd(BY).SkipAnd(expression)
                .Then(value => new ParsedSequenceOption(ParsedSequenceOptionKind.Increment, value)))
            .Or(MINVALUE.SkipAnd(expression)
                .Then(value => new ParsedSequenceOption(ParsedSequenceOptionKind.Minimum, value)))
            .Or(MAXVALUE.SkipAnd(expression)
                .Then(value => new ParsedSequenceOption(ParsedSequenceOptionKind.Maximum, value)))
            .Or(CACHE.SkipAnd(expression)
                .Then(value => new ParsedSequenceOption(ParsedSequenceOptionKind.Cache, value)))
            .Or(CYCLE.Then(new ParsedSequenceOption(ParsedSequenceOptionKind.Cycle)))
            .Or(NO.SkipAnd(CYCLE).Then(new ParsedSequenceOption(ParsedSequenceOptionKind.NoCycle)));
        var sequenceOptions = ZeroOrMany(sequenceOption).Then(CreateSequenceOptions);
        var createSequence = CREATE.SkipAnd(SEQUENCE)
            .SkipAnd(ifNotExists)
            .And(tableName)
            .And(sequenceOptions)
            .Then<SqlStatement>(value => new CreateSequenceStatement(
                value.Item2,
                value.Item3,
                value.Item1.HasValue));
        var alterSequence = ALTER.SkipAnd(SEQUENCE)
            .SkipAnd(tableName)
            .And(sequenceOptions)
            .Then<SqlStatement>(value => new AlterSequenceStatement(value.Item1, value.Item2));

        var schemaObjectKind = TABLE.Then(SchemaObjectKind.Table)
            .Or(VIEW.Then(SchemaObjectKind.View))
            .Or(INDEX.Then(SchemaObjectKind.Index))
            .Or(SEQUENCE.Then(SchemaObjectKind.Sequence));
        var dropStatement = DROP.SkipAnd(schemaObjectKind)
            .And(IF.SkipAnd(EXISTS).Optional())
            .And(Separated(comma, tableName))
            .And(CASCADE.Optional())
            .Then<SqlStatement>(value => new DropStatement(
                value.Item1,
                value.Item3,
                value.Item2.HasValue,
                value.Item4.HasValue));
        var truncateStatement = TRUNCATE.SkipAnd(TABLE.Optional())
            .SkipAnd(Separated(comma, tableName))
            .And(CASCADE.Optional())
            .Then<SqlStatement>(value => new TruncateStatement(
                value.Item1,
                Cascade: value.Item2.HasValue));

        var addColumn = ADD.SkipAnd(COLUMN.Optional()).SkipAnd(columnDefinition)
            .Then<AlterTableAction>(value => new AddColumnAction(value));
        var addConstraint = ADD.SkipAnd(tableConstraint)
            .Then<AlterTableAction>(value => new AddConstraintAction(value));
        var dropColumn = DROP.SkipAnd(COLUMN)
            .SkipAnd(IF.SkipAnd(EXISTS).Optional())
            .And(simpleIdentifier)
            .And(CASCADE.Optional())
            .Then<AlterTableAction>(value => new DropColumnAction(
                value.Item2,
                value.Item1.HasValue,
                value.Item3.HasValue));
        var dropConstraint = DROP.SkipAnd(CONSTRAINT)
            .SkipAnd(IF.SkipAnd(EXISTS).Optional())
            .And(simpleIdentifier)
            .And(CASCADE.Optional())
            .Then<AlterTableAction>(value => new DropConstraintAction(
                value.Item2,
                value.Item1.HasValue,
                value.Item3.HasValue));
        var renameColumn = RENAME.SkipAnd(COLUMN)
            .SkipAnd(simpleIdentifier)
            .AndSkip(TO)
            .And(simpleIdentifier)
            .Then<AlterTableAction>(value => new RenameColumnAction(value.Item1, value.Item2));
        var renameTable = RENAME.SkipAnd(TO)
            .SkipAnd(simpleIdentifier)
            .Then<AlterTableAction>(value => new RenameTableAction(value));
        var alterColumnType = simpleIdentifier
            .AndSkip(TYPE)
            .And(dataType)
            .Then<AlterTableAction>(value => new AlterColumnAction(value.Item1, value.Item2));
        var alterColumnDefault = simpleIdentifier
            .AndSkip(SET)
            .AndSkip(DEFAULT)
            .And(expression)
            .Then<AlterTableAction>(value => new AlterColumnAction(value.Item1, Default: value.Item2));
        var alterColumnDropDefault = simpleIdentifier
            .AndSkip(DROP)
            .AndSkip(DEFAULT)
            .Then<AlterTableAction>(value => new AlterColumnAction(value, DropDefault: true));
        var alterColumnSetNotNull = simpleIdentifier
            .AndSkip(SET)
            .AndSkip(NOT)
            .AndSkip(NULL)
            .Then<AlterTableAction>(value => new AlterColumnAction(
                value,
                Nullability: Nullability.NotNull));
        var alterColumnDropNotNull = simpleIdentifier
            .AndSkip(DROP)
            .AndSkip(NOT)
            .AndSkip(NULL)
            .Then<AlterTableAction>(value => new AlterColumnAction(
                value,
                Nullability: Nullability.Null));
        var alterColumn = ALTER.SkipAnd(COLUMN)
            .SkipAnd(alterColumnType
                .Or(alterColumnDefault)
                .Or(alterColumnDropDefault)
                .Or(alterColumnSetNotNull)
                .Or(alterColumnDropNotNull));
        var alterTableAction = addConstraint
            .Or(addColumn)
            .Or(dropConstraint)
            .Or(dropColumn)
            .Or(alterColumn)
            .Or(renameColumn)
            .Or(renameTable);
        var alterTable = ALTER.SkipAnd(TABLE)
            .SkipAnd(tableName)
            .And(Separated(comma, alterTableAction))
            .Then<SqlStatement>(value => new AlterTableStatement(value.Item1, value.Item2));

        var statement = query.Then<SqlStatement>(value => value)
            .Or(insert)
            .Or(update)
            .Or(delete)
            .Or(merge)
            .Or(createTable)
            .Or(createView)
            .Or(createIndex)
            .Or(createSequence)
            .Or(alterSequence)
            .Or(alterTable)
            .Or(dropStatement)
            .Or(truncateStatement);
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
            var message = GetParseErrorMessage(parseError);
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
            "DEFAULT", "COLLATE", "EXTRACT", "INTERVAL", "TRY_CAST", "FILTER", "WITHIN",
            "WINDOW", "QUALIFY", "RANGE", "GROUPS", "UNBOUNDED", "PRECEDING", "CURRENT", "FOLLOWING",
            "UNKNOWN", "USING", "NATURAL", "MERGE", "MATCHED",
            "CREATE", "ALTER", "DROP", "TRUNCATE", "TABLE", "TEMPORARY", "IF", "REPLACE",
            "VIEW", "INDEX", "UNIQUE", "SEQUENCE", "COLUMN", "CONSTRAINT", "PRIMARY", "KEY",
            "FOREIGN", "REFERENCES", "CHECK", "CASCADE", "RESTRICT", "NO", "ACTION", "RENAME",
            "TO", "TYPE", "GENERATED", "ALWAYS", "IDENTITY", "VIRTUAL", "STORED", "ADD",
            "CYCLE", "CACHE", "MINVALUE", "MAXVALUE", "INCREMENT",
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
        if (syntax.SupportsReturningInto) words.Add("INTO");
        if (syntax.SupportsILike) words.Add("ILIKE");
        if (syntax.SupportsMinus) words.Add("MINUS");
        if (syntax.SupportsRecursiveCte) words.UnionWith(["RECURSIVE", "MATERIALIZED"]);
        if (syntax.SupportsHierarchicalQueries)
        {
            words.UnionWith(["CONNECT", "START", "NOCYCLE", "PRIOR", "CONNECT_BY_ROOT", "SIBLINGS"]);
        }

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
        SqlParseErrorCode code)
    {
        if (error is null)
        {
            return new SqlParseError(message, 0, 1, 1, code);
        }

        return new SqlParseError(
            message,
            error.Position.Offset,
            error.Position.Line,
            error.Position.Column,
            code);
    }

    private static string GetParseErrorMessage(ParseError? error) =>
        error?.Message ?? "Invalid SQL.";

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
        ParsedOrderBy? orderBy,
        RowLimit? rowLimit) =>
        query switch
        {
            SelectStatement select => select with
            {
                OrderBy = orderBy?.Items,
                OrderSiblings = orderBy?.Siblings ?? false,
                Limit = rowLimit?.Limit,
                Offset = rowLimit?.Offset,
            },
            ValuesStatement values => values with
            {
                OrderBy = orderBy?.Items,
                Limit = rowLimit?.Limit,
                Offset = rowLimit?.Offset,
            },
            SetOperationStatement set => set with
            {
                OrderBy = orderBy?.Items,
                Limit = rowLimit?.Limit,
                Offset = rowLimit?.Offset,
            },
            _ => query,
        };

    private static SequenceOptions CreateSequenceOptions(
        IReadOnlyList<ParsedSequenceOption> parsed)
    {
        SqlExpression? start = null;
        SqlExpression? increment = null;
        SqlExpression? minimum = null;
        SqlExpression? maximum = null;
        SqlExpression? cache = null;
        bool? cycle = null;

        foreach (var option in parsed)
        {
            if (option.Kind == ParsedSequenceOptionKind.Start)
            {
                start = option.Value;
            }
            else if (option.Kind == ParsedSequenceOptionKind.Increment)
            {
                increment = option.Value;
            }
            else if (option.Kind == ParsedSequenceOptionKind.Minimum)
            {
                minimum = option.Value;
            }
            else if (option.Kind == ParsedSequenceOptionKind.Maximum)
            {
                maximum = option.Value;
            }
            else if (option.Kind == ParsedSequenceOptionKind.Cache)
            {
                cache = option.Value;
            }
            else if (option.Kind == ParsedSequenceOptionKind.Cycle)
            {
                cycle = true;
            }
            else
            {
                cycle = false;
            }
        }

        return new SequenceOptions(start, increment, minimum, maximum, cache, cycle);
    }

    private static SqlQuery BuildSetOperation(
        SqlQuery first,
        IReadOnlyList<SetTail> tails)
    {
        var queries = new Stack<SqlQuery>();
        var operators = new Stack<(SetOperator Operator, bool IsAll)>();
        queries.Push(first);

        foreach (var tail in tails)
        {
            while (operators.TryPeek(out var current)
                && GetSetOperatorPrecedence(current.Operator) >= GetSetOperatorPrecedence(tail.Operator))
            {
                ReduceSetOperation(queries, operators.Pop());
            }

            operators.Push((tail.Operator, tail.IsAll));
            queries.Push(tail.Right);
        }

        while (operators.Count > 0)
        {
            ReduceSetOperation(queries, operators.Pop());
        }

        return queries.Pop();
    }

    private static void ReduceSetOperation(
        Stack<SqlQuery> queries,
        (SetOperator Operator, bool IsAll) operation)
    {
        var right = queries.Pop();
        var left = queries.Pop();
        queries.Push(new SetOperationStatement(left, operation.Operator, right, operation.IsAll));
    }

    private static int GetSetOperatorPrecedence(SetOperator value) =>
        value == SetOperator.Intersect ? 2 : 1;

    private static SqlQuery ApplyCommonTableExpressions(
        SqlQuery query,
        IReadOnlyList<CommonTableExpression> commonTableExpressions,
        bool isRecursive) =>
        query switch
        {
            SelectStatement select => select with
            {
                CommonTableExpressions = commonTableExpressions,
                IsRecursive = isRecursive,
            },
            SetOperationStatement set => set with
            {
                CommonTableExpressions = commonTableExpressions,
                IsRecursive = isRecursive,
            },
            ValuesStatement values => values with
            {
                CommonTableExpressions = commonTableExpressions,
                IsRecursive = isRecursive,
            },
            _ => query,
        };

    [ExcludeFromCodeCoverage]
    private sealed record InTarget(IReadOnlyList<SqlExpression> Values, SqlQuery? Query);

    [ExcludeFromCodeCoverage]
    private sealed record ParsedJoin(
        JoinKind Kind,
        TableSource Right,
        SqlExpression? Condition,
        JoinSyntax Syntax,
        IReadOnlyList<SqlIdentifier>? Using,
        bool IsNatural);

    [ExcludeFromCodeCoverage]
    private sealed record ParsedJoinCondition(
        SqlExpression? Condition,
        IReadOnlyList<SqlIdentifier>? Using);

    [ExcludeFromCodeCoverage]
    private sealed record ParsedFunctionArguments(IReadOnlyList<SqlExpression> Arguments, bool IsDistinct);

    [ExcludeFromCodeCoverage]
    private sealed record ParsedWindow(
        IReadOnlyList<SqlExpression>? PartitionBy,
        IReadOnlyList<OrderByItem>? OrderBy,
        WindowFrame? Frame,
        SqlIdentifier? WindowName);

    [ExcludeFromCodeCoverage]
    private sealed record ParsedSelectHead(
        bool IsDistinct,
        ParsedTop? Top,
        IReadOnlyList<SelectItem> Items,
        TableSource? From,
        SqlExpression? Where,
        IReadOnlyList<SqlExpression>? GroupBy,
        SqlExpression? Having);

    [ExcludeFromCodeCoverage]
    private sealed record ParsedTop(SqlExpression Expression, bool IsPercent, bool WithTies);

    [ExcludeFromCodeCoverage]
    private sealed record SetTail(SetOperator Operator, SqlQuery Right, bool IsAll);

    [ExcludeFromCodeCoverage]
    private sealed record RowLimit(SqlExpression? Limit, SqlExpression? Offset);

    [ExcludeFromCodeCoverage]
    private sealed record ParsedOrderBy(
        IReadOnlyList<OrderByItem> Items,
        bool Siblings);

    [ExcludeFromCodeCoverage]
    private sealed record ParsedWith(
        IReadOnlyList<CommonTableExpression> Expressions,
        bool IsRecursive);

    [ExcludeFromCodeCoverage]
    private sealed record ParsedReturning(
        IReadOnlyList<SqlExpression> Expressions,
        IReadOnlyList<SqlExpression>? Into);

    private enum ParsedColumnModifierKind
    {
        Null,
        NotNull,
        Default,
        Generated,
        Identity,
        PrimaryKey,
        Unique,
    }

    [ExcludeFromCodeCoverage]
    private sealed record ParsedColumnModifier(
        ParsedColumnModifierKind Kind,
        SqlExpression? Expression = null,
        GeneratedColumnKind GeneratedKind = GeneratedColumnKind.Virtual,
        IdentityGeneration Identity = IdentityGeneration.None);

    private enum ParsedSequenceOptionKind
    {
        Start,
        Increment,
        Minimum,
        Maximum,
        Cache,
        Cycle,
        NoCycle,
    }

    [ExcludeFromCodeCoverage]
    private sealed record ParsedSequenceOption(
        ParsedSequenceOptionKind Kind,
        SqlExpression? Value = null);
}

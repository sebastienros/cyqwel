using System.Reflection;
using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Parsing;
using Cyqwel.Visitors;
using Parlot;

namespace Cyqwel.Tests;

public class ParserCoverageTests
{
    [Fact]
    public void Covers_remaining_expression_and_query_forms()
    {
        var document = SqlParser.Parse("""
            SELECT seq.CURRVAL,
                   TRY_CAST('1' AS INTEGER),
                   EXTRACT(YEAR FROM created_at),
                   INTERVAL '1' DAY,
                   NOT EXISTS (SELECT 1),
                   (SELECT 1),
                   ROW(1, 2),
                   d.*,
                   CONNECT_BY_ROOT id,
                   8 / 2,
                   2 * 3,
                   5 + 3,
                   5 - 3,
                   1 <= 2,
                   1 <> 2,
                   1 != 2,
                   1 < 2,
                   CASE WHEN 1 = 1 THEN 'yes' ELSE 'no' END,
                   name COLLATE ordinal
            FROM (SELECT 1 AS id) d
            WHERE id IN (1, 2)
              AND name LIKE 'a%'
              AND name NOT ILIKE 'b%'
            """);

        Assert.Single(document.FindAll<SequenceValueExpression>());
        Assert.Single(document.FindAll<TryCastExpression>());
        Assert.Single(document.FindAll<ExtractExpression>());
        Assert.Single(document.FindAll<IntervalExpression>());
        Assert.Single(document.FindAll<ExistsExpression>());
        Assert.Single(document.FindAll<SubqueryExpression>());
        Assert.Single(document.FindAll<RowExpression>());
        Assert.Single(document.FindAll<StarExpression>());
        Assert.Single(document.FindAll<DerivedTable>());
        Assert.Contains(
            document.FindAll<UnaryExpression>(),
            value => value.Operator == UnaryOperator.ConnectByRoot);
        Assert.Contains(
            document.FindAll<InExpression>(),
            value => value.Values.Count == 2);
        Assert.Contains(
            document.FindAll<CaseExpression>(),
            value => value.Else is not null);
        Assert.Single(document.FindAll<CollateExpression>());
    }

    [Fact]
    public void Covers_all_window_frame_bounds_and_units()
    {
        var document = SqlParser.Parse("""
            SELECT SUM(value) OVER (ORDER BY id ROWS 2 FOLLOWING),
                   SUM(value) OVER (ORDER BY id RANGE UNBOUNDED FOLLOWING),
                   SUM(value) OVER (ORDER BY id GROUPS CURRENT ROW),
                   SUM(value) OVER (ORDER BY id ASC NULLS FIRST)
            FROM entries
            """);
        var frames = document.FindAll<WindowFrame>().ToArray();

        Assert.Equal(3, frames.Length);
        Assert.Contains(frames, value => value.Start.Kind == WindowFrameBoundKind.Following);
        Assert.Contains(frames, value => value.Start.Kind == WindowFrameBoundKind.UnboundedFollowing);
        Assert.Contains(frames, value => value.Unit == WindowFrameUnit.Groups);
    }

    [Fact]
    public void Covers_optional_select_and_cte_clauses()
    {
        var hierarchy = SqlParser.Parse("""
            SELECT id
            FROM nodes
            QUALIFY id > 0
            CONNECT BY PRIOR id = parent_id
            """);
        var with = SqlParser.Parse("""
            WITH cached AS MATERIALIZED (SELECT 1),
                 inline AS NOT MATERIALIZED (SELECT 2)
            SELECT 1
            """);

        var select = Assert.IsType<SelectStatement>(Assert.Single(hierarchy.Statements));
        Assert.NotNull(select.ConnectBy);
        Assert.Null(select.ConnectBy.StartWith);
        Assert.False(select.ConnectBy.NoCycle);
        Assert.NotNull(select.Qualify);
        Assert.Equal(
            [CteMaterialization.Materialized, CteMaterialization.NotMaterialized],
            Assert.IsType<SelectStatement>(Assert.Single(with.Statements))
                .CommonTableExpressions!
                .Select(value => value.Materialization));
    }

    [Fact]
    public void Covers_optional_insert_and_merge_clauses()
    {
        var document = SqlParser.Parse("""
            INSERT INTO audit VALUES (1) RETURNING id INTO @inserted;
            MERGE INTO target t
            USING source s
            ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET t.name = s.name DELETE WHERE s.deleted IS TRUE
            WHEN NOT MATCHED BY SOURCE THEN DELETE
            WHEN NOT MATCHED THEN INSERT VALUES (s.id, s.name)
            RETURNING t.id INTO @merged
            """);
        var insert = Assert.IsType<InsertStatement>(document.Statements[0]);
        var merge = Assert.IsType<MergeStatement>(document.Statements[1]);

        Assert.Null(insert.Columns);
        Assert.NotNull(insert.Values);
        Assert.Single(insert.ReturningInto!);
        Assert.NotNull(Assert.IsType<MergeUpdateAction>(merge.WhenClauses[0].Action).DeleteWhere);
        Assert.Equal(MergeMatchKind.NotMatchedBySource, merge.WhenClauses[1].MatchKind);
        Assert.Null(Assert.IsType<MergeInsertAction>(merge.WhenClauses[2].Action).Columns);
        Assert.Single(merge.ReturningInto!);
    }

    [Fact]
    public void Covers_all_column_constraint_and_view_variants()
    {
        var document = SqlParser.Parse("""
            CREATE TABLE generated_columns (
                nullable_value INTEGER NULL,
                virtual_value INTEGER GENERATED ALWAYS AS (nullable_value + 1),
                stored_value INTEGER GENERATED ALWAYS AS (nullable_value + 2) STORED,
                PRIMARY KEY (nullable_value),
                CONSTRAINT fk_parent FOREIGN KEY (nullable_value) REFERENCES parent (id)
            );
            CREATE OR REPLACE TEMPORARY VIEW generated_view (id) AS SELECT nullable_value FROM generated_columns
            """);
        var columns = document.FindAll<ColumnDefinition>().ToArray();
        var view = Assert.Single(document.FindAll<CreateViewStatement>());

        Assert.Contains(columns, value => value.Nullability == Nullability.Null);
        Assert.Contains(columns, value => value.GeneratedExpression is not null
            && value.GeneratedKind == GeneratedColumnKind.Virtual);
        Assert.Contains(columns, value => value.GeneratedKind == GeneratedColumnKind.Stored);
        Assert.Single(document.FindAll<PrimaryKeyConstraint>());
        Assert.Single(document.FindAll<ForeignKeyConstraint>());
        Assert.True(view.OrReplace);
        Assert.True(view.IsTemporary);
        Assert.Single(view.Columns!);
    }

    [Fact]
    public void Covers_sequence_and_alter_table_variants()
    {
        var document = SqlParser.Parse("""
            CREATE SEQUENCE complete_seq MINVALUE 1 MAXVALUE 999 CYCLE;
            ALTER SEQUENCE complete_seq START WITH 2 INCREMENT BY 3;
            ALTER TABLE child ADD CONSTRAINT child_pk PRIMARY KEY (id);
            ALTER TABLE child DROP COLUMN IF EXISTS obsolete CASCADE;
            ALTER TABLE child DROP CONSTRAINT IF EXISTS old_constraint CASCADE;
            ALTER TABLE child RENAME COLUMN old_name TO new_name;
            ALTER TABLE child RENAME TO children
            """);
        var sequence = Assert.Single(document.FindAll<CreateSequenceStatement>());

        Assert.NotNull(sequence.Options.MinimumValue);
        Assert.NotNull(sequence.Options.MaximumValue);
        Assert.True(sequence.Options.Cycle);
        Assert.Single(document.FindAll<AlterSequenceStatement>());
        Assert.Single(document.FindAll<AddConstraintAction>());
        Assert.Single(document.FindAll<DropColumnAction>());
        Assert.Single(document.FindAll<DropConstraintAction>());
        Assert.Single(document.FindAll<RenameColumnAction>());
        Assert.Single(document.FindAll<RenameTableAction>());
    }

    [Fact]
    public void Covers_optional_ddl_clauses()
    {
        var document = SqlParser.Parse("""
            CREATE TEMPORARY TABLE IF NOT EXISTS copied AS SELECT 1;
            CREATE TABLE constraints (
                id INTEGER,
                UNIQUE (id),
                FOREIGN KEY (id) REFERENCES parent (id),
                CHECK (id > 0)
            );
            CREATE VIEW simple_view AS SELECT 1;
            CREATE INDEX filtered_index ON constraints (id DESC NULLS LAST) WHERE id > 0
            """);
        var copied = Assert.IsType<CreateTableStatement>(document.Statements[0]);
        var view = Assert.IsType<CreateViewStatement>(document.Statements[2]);
        var index = Assert.IsType<CreateIndexStatement>(document.Statements[3]);

        Assert.True(copied.IfNotExists);
        Assert.True(copied.IsTemporary);
        Assert.Empty(copied.Elements);
        Assert.NotNull(copied.AsQuery);
        Assert.Single(document.FindAll<UniqueConstraint>());
        Assert.Single(document.FindAll<ForeignKeyConstraint>());
        Assert.Single(document.FindAll<CheckConstraint>());
        Assert.Null(view.Columns);
        Assert.Equal(OrderDirection.Descending, Assert.Single(index.Columns).Direction);
        Assert.Equal(NullOrder.Last, Assert.Single(index.Columns).NullOrder);
        Assert.NotNull(index.Where);
    }

    [Fact]
    public void Covers_parser_configuration_without_limits_or_parameters()
    {
        var dialect = SqlDialectBuilder.Create($"minimal-{Guid.NewGuid():N}")
            .ConfigureParser(options => options with
            {
                ParameterStyles = SqlParameterStyle.None,
                SupportsTop = false,
                SupportsLimit = false,
                SupportsLimitComma = false,
                SupportsOffsetOnly = false,
                SupportsOffsetFetch = false,
            })
            .Build();

        Assert.Equal("SELECT 1", dialect.Parse("SELECT 1").ToSql(dialect));
        Assert.Throws<SqlParseException>(() => dialect.Parse("SELECT @id"));
    }

    [Fact]
    public void Covers_dollar_sign_identifier_configuration()
    {
        var dialect = SqlDialectBuilder.Create($"dollar-identifiers-{Guid.NewGuid():N}")
            .ConfigureParser(options => options with
            {
                DollarSignIsIdentifier = true,
            })
            .Build();
        var document = dialect.Parse("SELECT cash$value FROM data$table WHERE cash$value = @arg$value");

        Assert.Contains(
            document.FindAll<SqlIdentifier>(),
            value => value.Value == "cash$value");
        Assert.Contains(
            document.FindAll<SqlIdentifier>(),
            value => value.Value == "data$table");
        Assert.Equal("arg$value", Assert.Single(document.FindAll<ParameterExpression>()).Name);
    }

    [Fact]
    public void Covers_set_offset_compatibility_paths()
    {
        Assert.NotNull(SqlDialects.TSql.Parse(
            "SELECT id FROM users UNION SELECT id FROM archived"));
        Assert.NotNull(SqlDialects.TSql.Parse(
            "SELECT id FROM users UNION SELECT id FROM archived ORDER BY id OFFSET 1 ROWS"));

        var parsed = SqlDialects.TSql.TryParse(
            "SELECT id FROM users UNION SELECT id FROM archived OFFSET 1 ROWS",
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Equal(SqlParseErrorCode.DialectIncompatible, error!.Code);
    }

    [Fact]
    public void Covers_escape_and_empty_error_fallbacks()
    {
        var escaped = SqlDialects.MySql.Parse("""SELECT '\\'""");
        var literal = Assert.Single(escaped.FindAll<LiteralExpression>());

        Assert.Equal("\\", literal.Value);
        Assert.False(SqlParser.TryParse("", SqlDialects.Generic, out _, out var error));
        Assert.Equal(SqlParseErrorCode.Syntax, error!.Code);
    }

    [Fact]
    public void Covers_defensive_unknown_query_paths()
    {
        var query = new UnknownQuery();
        var applyTail = GetParserMethod("ApplyQueryTail");
        var applyCtes = GetParserMethod("ApplyCommonTableExpressions");
        var toParseError = GetParserMethod("ToParseError");
        var getParseErrorMessage = GetParserMethod("GetParseErrorMessage");

        Assert.Same(query, applyTail.Invoke(null, [query, null, null]));
        Assert.Same(
            query,
            applyCtes.Invoke(
                null,
                [query, Array.Empty<CommonTableExpression>(), false]));
        var error = Assert.IsType<SqlParseError>(
            toParseError.Invoke(null, ["fallback", null, SqlParseErrorCode.Syntax]));
        Assert.Equal(0, error.Offset);
        Assert.Equal(1, error.Line);
        Assert.Equal(1, error.Column);

        var parlotError = new ParseError
        {
            Message = "detailed",
            Position = new TextPosition(4, 2, 3),
        };
        var detailed = Assert.IsType<SqlParseError>(
            toParseError.Invoke(null, ["fallback", parlotError, SqlParseErrorCode.Syntax]));
        Assert.Equal(4, detailed.Offset);
        Assert.Equal(2, detailed.Line);
        Assert.Equal(3, detailed.Column);
        Assert.Equal("Invalid SQL.", getParseErrorMessage.Invoke(null, [null]));
        Assert.Equal(
            "Invalid SQL.",
            getParseErrorMessage.Invoke(null, [new ParseError()]));
        Assert.Equal("detailed", getParseErrorMessage.Invoke(null, [parlotError]));
    }

    private static MethodInfo GetParserMethod(string name) =>
        typeof(SqlParser).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Parser method '{name}' was not found.");

    private sealed record UnknownQuery : SqlQuery;
}

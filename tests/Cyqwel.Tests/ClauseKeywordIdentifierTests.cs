using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Parsing;

namespace Cyqwel.Tests;

public class ClauseKeywordIdentifierTests
{
    private static readonly string[] ClauseKeywords =
    [
        "ACTION", "ADD", "ALWAYS", "CACHE", "CASCADE", "CYCLE", "GENERATED", "IDENTITY", "INCREMENT",
        "KEY", "MAXVALUE", "MINVALUE", "NO", "RENAME", "RESTRICT", "SEQUENCE", "STORED", "TEMPORARY",
        "TYPE", "VIRTUAL",
        "ANALYZE", "DEFINER", "EXPLAIN", "FILTER", "FIRST", "FOLLOWING", "GROUPS", "HASH", "INDEX", "INOUT",
        "INVOKER", "LANGUAGE", "LAST", "MATCHED", "NEXT", "NULLS", "ONLY", "OUT", "PRECEDING", "SECURITY",
        "SQL", "TIES", "UNBOUNDED", "UNKNOWN", "VIEW",
    ];

    // Words each vendor documents as reserved; generic has no vendor and reserves none of them.
    private static readonly Dictionary<string, string[]> Reserved = new()
    {
        ["generic"] = [],
        ["postgresql"] = ["ANALYZE", "ONLY"],
        ["sqlite"] = ["ADD", "INDEX"],
        ["oracle"] = ["ADD", "INCREMENT", "INDEX", "RENAME", "VIEW"],
        ["tsql"] = ["ADD", "CASCADE", "IDENTITY", "INDEX", "KEY", "RESTRICT", "VIEW"],
        ["mysql"] =
        [
            "ADD", "ANALYZE", "CASCADE", "EXPLAIN", "GENERATED", "GROUPS", "INDEX", "INOUT", "KEY", "MAXVALUE",
            "OUT", "RENAME", "RESTRICT", "SQL", "STORED", "VIRTUAL",
        ],
    };

    private static readonly string[] IdentifierPositions =
    [
        "SELECT {0} FROM audit",
        "SELECT audit.{0} FROM audit",
        "SELECT id AS {0} FROM audit",
        "SELECT id {0} FROM audit",
        "SELECT id FROM {0}",
        "SELECT id FROM audit WHERE {0} = 1",
        "SELECT id FROM audit ORDER BY {0}",
        "INSERT INTO audit ({0}) VALUES (1)",
        "UPDATE audit SET {0} = 1",
        "CREATE TABLE audit ({0} INT)",
        "CREATE TABLE audit ({0} VARCHAR(10), id INT)",
        "CREATE TABLE audit (id INT, {0} INT NOT NULL DEFAULT 1)",
    ];

    private const string Control = "zzcontrol";

    // Each statement has to parse to the same tree whichever name it holds, so a keyword that
    // gets captured as a clause, an alias or an index element differs from the control name.
    private static readonly string[] EquivalencePositions =
    [
        "SELECT {0} FROM t",
        "SELECT {0}, id FROM t",
        "SELECT t.{0} FROM t",
        "SELECT {0}.id FROM {0}",
        "SELECT id AS {0} FROM t",
        "SELECT id {0} FROM t",
        "SELECT id {0}, name FROM t",
        "SELECT COUNT(*) {0} FROM t",
        "SELECT SUM(id) OVER () {0} FROM t",
        "SELECT {0}(id) FROM t",
        "SELECT id + {0} FROM t",
        "SELECT CASE WHEN {0} = 1 THEN {0} ELSE 0 END FROM t",
        "SELECT CAST(id AS {0}) FROM t",
        "SELECT id FROM {0}",
        "SELECT id FROM s.{0}",
        "SELECT id FROM t {0}",
        "SELECT id FROM t AS {0}",
        "SELECT id FROM t {0} WHERE id = 1",
        "SELECT id FROM t {0} ORDER BY id",
        "SELECT id FROM t {0}, u",
        "SELECT id FROM t {0} JOIN u ON id = 1",
        "SELECT id FROM t {0} INNER JOIN u ON id = 1",
        "SELECT id FROM t JOIN u {0} ON id = 1",
        "SELECT id FROM t JOIN {0} ON id = 1",
        "SELECT id FROM (SELECT 1 AS id) {0}",
        "SELECT id FROM t WHERE {0} = 1",
        "SELECT id FROM t WHERE {0} IS NULL",
        "SELECT id FROM t WHERE {0} IS NOT TRUE",
        "SELECT id FROM t GROUP BY {0}",
        "SELECT id FROM t GROUP BY id HAVING {0} = 1",
        "SELECT id FROM t ORDER BY {0}",
        "SELECT id FROM t ORDER BY {0} DESC",
        "SELECT id FROM t ORDER BY id, {0}",
        "SELECT id FROM t ORDER BY id NULLS FIRST, {0}",
        "SELECT SUM(id) OVER {0} FROM t",
        "SELECT SUM(id) OVER ({0}) FROM t",
        "SELECT SUM(id) OVER ({0} ORDER BY id) FROM t",
        "SELECT SUM(id) OVER (PARTITION BY {0}) FROM t",
        "SELECT SUM(id) OVER (ORDER BY {0} ROWS 1 PRECEDING) FROM t",
        "SELECT SUM(id) OVER w FROM t WINDOW {0} AS (ORDER BY id), w AS ({0})",
        "SELECT COUNT(*) FILTER (WHERE {0} = 1) FROM t",
        "WITH {0} AS (SELECT 1 AS id) SELECT id FROM {0}",
        "INSERT INTO {0} (id) VALUES (1)",
        "INSERT INTO t ({0}) VALUES (1)",
        "INSERT INTO t ({0}, id) SELECT {0}, id FROM u",
        "UPDATE {0} SET id = 1",
        "UPDATE t SET {0} = 1 WHERE {0} = 2",
        "DELETE FROM {0} WHERE id = 1",
        "DELETE FROM t WHERE {0} = 1",
        "MERGE INTO {0} USING s ON {0}.id = s.id WHEN MATCHED THEN DELETE",
        "MERGE INTO t USING {0} ON t.id = {0}.id WHEN NOT MATCHED THEN INSERT (id) VALUES (1)",
        "MERGE INTO t USING s ON t.id = s.id WHEN MATCHED THEN UPDATE SET {0} = 1",
        "CREATE TABLE {0} (id INT)",
        "CREATE TABLE t ({0} INT)",
        "CREATE TABLE t ({0} VARCHAR(10))",
        "CREATE TABLE t ({0} NUMERIC(10, 2), id INT)",
        "CREATE TABLE t ({0} INT NOT NULL DEFAULT 1)",
        "CREATE TABLE t (id INT, {0} VARCHAR(10) PRIMARY KEY)",
        "CREATE TABLE t (id INT, PRIMARY KEY ({0}))",
        "CREATE TABLE t (id INT, CONSTRAINT {0} UNIQUE (id))",
        "CREATE TABLE t (id INT, FOREIGN KEY ({0}) REFERENCES {0} ({0}))",
        "CREATE TABLE t ({0} INT, INDEX ix ({0}))",
        "CREATE VIEW {0} AS SELECT 1 AS id",
        "CREATE VIEW v ({0}) AS SELECT 1 AS id",
        "CREATE INDEX {0} ON t (id)",
        "CREATE INDEX ix ON {0} ({0})",
        "DROP TABLE {0}",
        "DROP VIEW {0}",
        "DROP INDEX {0}",
        "TRUNCATE TABLE {0}",
        "ALTER TABLE {0} ADD id INT",
        "ALTER TABLE t ADD {0} INT",
        "ALTER TABLE t ADD COLUMN {0} VARCHAR(10)",
        "ALTER TABLE t DROP COLUMN {0}",
        "ALTER TABLE t RENAME COLUMN {0} TO id",
        "ALTER TABLE t RENAME COLUMN id TO {0}",
        "ALTER TABLE t RENAME TO {0}",
        "ALTER TABLE t ALTER COLUMN {0} TYPE TEXT",
        "CALL {0}(1)",
        "CALL p({0})",
        "CALL p({0}, id)",
        "DROP PROCEDURE {0}",
        "CREATE PROCEDURE {0}(id INT) AS BEGIN SELECT {0}; END",
        "EXPLAIN SELECT {0} FROM t",
        "EXPLAIN ANALYZE SELECT id {0} FROM t",
        "EXEC {0} 1",
        "EXEC p {0}",
        "SELECT TOP 5 {0} FROM t",
        "SELECT TOP 5 WITH TIES {0} FROM t ORDER BY {0}",
        "SELECT id FROM t ORDER BY {0} OFFSET 1 ROWS FETCH NEXT {0} ROWS ONLY",
        "SELECT id FROM t FETCH FIRST {0} ROWS ONLY",
    ];

    public static TheoryData<string> Dialects => new(Reserved.Keys);

    [Theory]
    [InlineData("generic")]
    [InlineData("postgresql")]
    [InlineData("tsql")]
    [InlineData("mysql")]
    [InlineData("oracle")]
    [InlineData("sqlite")]
    public void Action_is_an_identifier(string dialectName)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);

        var select = Assert.IsType<SelectStatement>(
            Assert.Single(dialect.Parse("SELECT action FROM audit").Statements));
        var insert = Assert.IsType<InsertStatement>(
            Assert.Single(dialect.Parse("INSERT INTO audit (action) VALUES (1)").Statements));

        var column = Assert.IsType<ColumnExpression>(Assert.Single(select.Projections).Expression);
        Assert.Equal("action", Assert.Single(column.Parts).Value);
        Assert.Equal("action", Assert.Single(insert.Columns!).Value);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Unreserved_clause_keywords_are_identifiers(string dialectName)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        foreach (var word in ClauseKeywords.Except(Reserved[dialectName]))
        {
            foreach (var position in IdentifierPositions)
            {
                foreach (var spelling in new[] { word, word.ToLowerInvariant() })
                {
                    var sql = string.Format(position, spelling);
                    Assert.True(SqlParser.TryParse(sql, dialect, out var document, out var error),
                        $"{dialectName}: {sql}: {error?.Message}");

                    // Generated SQL has to stay parseable by the same dialect.
                    var generated = document!.ToSql(dialect);
                    Assert.True(SqlParser.TryParse(generated, dialect, out _, out error),
                        $"{dialectName}: {generated}: {error?.Message}");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Unreserved_clause_keywords_parse_to_the_expected_nodes(string dialectName)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        foreach (var word in ClauseKeywords.Except(Reserved[dialectName]))
        {
            var select = Assert.IsType<SelectStatement>(Assert.Single(
                dialect.Parse($"SELECT {word}, id {word} FROM {word}").Statements));
            var column = Assert.IsType<ColumnExpression>(select.Projections[0].Expression);
            Assert.Equal(word, Assert.Single(column.Parts).Value);
            Assert.Equal(word, select.Projections[1].Alias!.Value);

            foreach (var type in new[] { "INT", "VARCHAR(10)", "NUMERIC(10, 2)" })
            {
                var create = Assert.IsType<CreateTableStatement>(Assert.Single(
                    dialect.Parse($"CREATE TABLE audit ({word} {type})").Statements));
                var definition = Assert.IsType<ColumnDefinition>(Assert.Single(create.Elements));
                Assert.Equal(word, definition.Name.Value);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Reserved_clause_keywords_require_quoting(string dialectName)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        foreach (var word in Reserved[dialectName])
        {
            Assert.False(SqlParser.TryParse($"SELECT {word} FROM audit", dialect, out _, out _), word);

            var quoted = $"{dialect.IdentifierOpenQuote}{word}{dialect.IdentifierCloseQuote}";
            Assert.True(SqlParser.TryParse($"SELECT {quoted} FROM audit", dialect, out _, out _), word);
        }
    }

    [Theory]
    [InlineData("generic", "SELECT key, cascade, rename, type FROM t")]
    [InlineData("postgresql", "SELECT key, cascade, rename, type FROM t")]
    [InlineData("sqlite", "SELECT key, cascade, rename, type FROM t")]
    [InlineData("mysql", "SELECT `key`, `cascade`, `rename`, type FROM t")]
    [InlineData("tsql", "SELECT [key], [cascade], rename, type FROM t")]
    [InlineData("oracle", "SELECT key, cascade, \"rename\", type FROM t")]
    public void Generation_quotes_only_what_the_target_dialect_reserves(string target, string expected)
    {
        var document = SqlParser.Parse("SELECT key, cascade, rename, type FROM t", SqlDialects.Generic);

        Assert.Equal(expected, document.ToSql(SqlDialectRegistry.Get(target)));
    }

    [Theory]
    [InlineData("generic", "NO ACTION", ReferentialAction.NoAction)]
    [InlineData("generic", "CASCADE", ReferentialAction.Cascade)]
    [InlineData("generic", "RESTRICT", ReferentialAction.Restrict)]
    [InlineData("postgresql", "NO ACTION", ReferentialAction.NoAction)]
    [InlineData("postgresql", "CASCADE", ReferentialAction.Cascade)]
    [InlineData("postgresql", "RESTRICT", ReferentialAction.Restrict)]
    [InlineData("mysql", "NO ACTION", ReferentialAction.NoAction)]
    [InlineData("mysql", "CASCADE", ReferentialAction.Cascade)]
    [InlineData("mysql", "RESTRICT", ReferentialAction.Restrict)]
    [InlineData("oracle", "CASCADE", ReferentialAction.Cascade)]
    [InlineData("sqlite", "NO ACTION", ReferentialAction.NoAction)]
    [InlineData("sqlite", "RESTRICT", ReferentialAction.Restrict)]
    [InlineData("tsql", "NO ACTION", ReferentialAction.NoAction)]
    [InlineData("tsql", "CASCADE", ReferentialAction.Cascade)]
    public void Referential_actions_keep_their_meaning(string dialectName, string action, ReferentialAction expected)
    {
        var create = Assert.IsType<CreateTableStatement>(Assert.Single(SqlParser.Parse(
            $"""
            CREATE TABLE audit (
                action INT,
                no INT,
                FOREIGN KEY (action) REFERENCES actions (no) ON DELETE {action} ON UPDATE {action}
            )
            """,
            SqlDialectRegistry.Get(dialectName)).Statements));

        var foreignKey = Assert.IsType<ForeignKeyConstraint>(create.Elements[^1]);
        Assert.Equal("action", Assert.Single(foreignKey.Columns).Value);
        Assert.Equal("no", Assert.Single(foreignKey.ReferencedColumns).Value);
        Assert.Equal(expected, foreignKey.OnDelete);
        Assert.Equal(expected, foreignKey.OnUpdate);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("postgresql")]
    [InlineData("sqlite")]
    [InlineData("oracle")]
    public void Trailing_cascade_is_not_read_as_a_name(string dialectName)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);

        var drop = Assert.IsType<DropStatement>(Assert.Single(
            dialect.Parse("DROP TABLE cascade CASCADE").Statements));
        var dropColumn = Assert.IsType<AlterTableStatement>(Assert.Single(
            dialect.Parse("ALTER TABLE t DROP COLUMN cascade CASCADE").Statements));
        var truncate = Assert.IsType<TruncateStatement>(Assert.Single(
            dialect.Parse("TRUNCATE TABLE cascade RESTART IDENTITY CASCADE").Statements));

        Assert.True(drop.Cascade);
        Assert.Equal("cascade", Assert.Single(drop.Names).Parts[0].Value);
        Assert.True(Assert.IsType<DropColumnAction>(Assert.Single(dropColumn.Actions)).Cascade);
        Assert.True(truncate.RestartIdentity);
        Assert.True(truncate.Cascade);
    }

    [Fact]
    public void Alter_table_actions_accept_clause_keywords_as_names()
    {
        var dialect = SqlDialects.PostgreSql;

        var rename = Assert.IsType<RenameColumnAction>(SingleAction(dialect, "ALTER TABLE t RENAME COLUMN rename TO type"));
        Assert.Equal("rename", rename.Column.Value);
        Assert.Equal("type", rename.NewName.Value);

        var renameTable = Assert.IsType<RenameTableAction>(SingleAction(dialect, "ALTER TABLE rename RENAME TO no"));
        Assert.Equal("no", renameTable.NewName.Value);

        var alter = Assert.IsType<AlterColumnAction>(SingleAction(dialect, "ALTER TABLE t ALTER COLUMN type TYPE TEXT"));
        Assert.Equal("type", alter.Column.Value);
        Assert.Equal("TEXT", alter.DataType!.Name.Value);

        var add = Assert.IsType<AddColumnAction>(SingleAction(dialect, "ALTER TABLE add ADD add INT"));
        Assert.Equal("add", add.Column.Name.Value);
    }

    [Fact]
    public void Column_modifiers_are_not_read_as_names()
    {
        var create = Assert.IsType<CreateTableStatement>(Assert.Single(SqlParser.Parse(
            """
            CREATE TEMPORARY TABLE temporary (
                key INT PRIMARY KEY,
                generated INT GENERATED ALWAYS AS (key + 1) STORED,
                virtual INT GENERATED ALWAYS AS (key + 2) VIRTUAL,
                stored INT DEFAULT generated,
                identity INT GENERATED BY DEFAULT AS IDENTITY,
                always INT GENERATED ALWAYS AS IDENTITY
            )
            """,
            SqlDialects.PostgreSql).Statements));

        Assert.True(create.IsTemporary);
        Assert.Equal("temporary", create.Name.Parts[0].Value);
        var columns = create.Elements.Cast<ColumnDefinition>().ToArray();
        Assert.Equal(["key", "generated", "virtual", "stored", "identity", "always"], columns.Select(c => c.Name.Value));
        Assert.True(columns[0].IsPrimaryKey);
        Assert.Equal(GeneratedColumnKind.Stored, columns[1].GeneratedKind);
        Assert.Equal(GeneratedColumnKind.Virtual, columns[2].GeneratedKind);
        Assert.NotNull(columns[3].Default);
        Assert.Equal(IdentityGeneration.ByDefault, columns[4].Identity);
        Assert.Equal(IdentityGeneration.Always, columns[5].Identity);
    }

    [Fact]
    public void Key_column_and_key_table_element_are_distinguished()
    {
        var create = Assert.IsType<CreateTableStatement>(Assert.Single(SqlParser.Parse(
            "CREATE TABLE t (key VARCHAR(10), KEY ix_key (key), UNIQUE KEY ux_key (key))",
            SqlDialects.Generic).Statements));

        Assert.Equal("key", Assert.IsType<ColumnDefinition>(create.Elements[0]).Name.Value);
        Assert.Equal("ix_key", Assert.IsType<IndexTableElement>(create.Elements[1]).Name!.Value);
        Assert.True(Assert.IsType<IndexTableElement>(create.Elements[2]).IsUnique);
    }

    [Fact]
    public void Sequence_options_are_not_read_as_names()
    {
        var create = Assert.IsType<CreateSequenceStatement>(Assert.Single(SqlParser.Parse(
            "CREATE SEQUENCE cycle INCREMENT BY cache MINVALUE 1 MAXVALUE 9 CACHE no NO CYCLE",
            SqlDialects.PostgreSql).Statements));

        Assert.Equal("cycle", create.Name.Parts[0].Value);
        Assert.Equal("CREATE SEQUENCE cycle INCREMENT BY cache MINVALUE 1 MAXVALUE 9 CACHE no NO CYCLE",
            new SqlDocument([create]).ToSql(SqlDialects.PostgreSql));

        Assert.IsType<DropStatement>(Assert.Single(
            SqlParser.Parse("DROP SEQUENCE sequence", SqlDialects.PostgreSql).Statements));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Unreserved_clause_keywords_parse_like_any_other_name(string dialectName)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var failures = new List<string>();
        foreach (var position in EquivalencePositions)
        {
            // The control name tells which positions the dialect's grammar supports at all.
            if (!SqlParser.TryParse(string.Format(position, Control), dialect, out var document, out _))
            {
                continue;
            }

            var expected = Dump(document);
            foreach (var word in ClauseKeywords.Except(Reserved[dialectName]))
            {
                foreach (var spelling in new[] { word, word.ToLowerInvariant() })
                {
                    var sql = string.Format(position, spelling);
                    if (!SqlParser.TryParse(sql, dialect, out document, out var error))
                    {
                        failures.Add($"{sql}: {error?.Message}");
                    }
                    else if (Dump(document).Replace(spelling, Control) != expected.Replace(spelling, Control))
                    {
                        failures.Add($"{sql}: {Dump(document)}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("postgresql")]
    [InlineData("oracle")]
    [InlineData("sqlite")]
    public void Null_ordering_keeps_its_meaning(string dialectName)
    {
        var select = ParseSingle<SelectStatement>(dialectName,
            "SELECT nulls, first, last FROM t ORDER BY nulls NULLS FIRST, first NULLS LAST, last DESC NULLS FIRST, nulls");

        Assert.Equal(["nulls", "first", "last"], select.Projections.Select(item => ColumnName(item.Expression)));
        Assert.Equal(["nulls", "first", "last", "nulls"], select.OrderBy!.Select(item => ColumnName(item.Expression)));
        Assert.Equal(
            [NullOrder.First, NullOrder.Last, NullOrder.First, NullOrder.Unspecified],
            select.OrderBy!.Select(item => item.NullOrder));
        Assert.Equal(OrderDirection.Descending, select.OrderBy![2].Direction);
    }

    [Theory]
    [InlineData("generic", "only")]
    [InlineData("oracle", "only")]
    [InlineData("tsql", "only")]
    [InlineData("postgresql", "id")]
    public void Row_limits_keep_their_meaning(string dialectName, string column)
    {
        var offsetFetch = ParseSingle<SelectStatement>(dialectName,
            $"SELECT {column} FROM t ORDER BY {column} OFFSET first ROWS FETCH NEXT next ROWS ONLY");
        Assert.Equal(column, ColumnName(Assert.Single(offsetFetch.Projections).Expression));
        Assert.Equal("next", ColumnName(offsetFetch.Limit!));
        Assert.Equal("first", ColumnName(offsetFetch.Offset!));

        if (dialectName != "tsql")
        {
            var fetchFirst = ParseSingle<SelectStatement>(dialectName, "SELECT id FROM t FETCH FIRST first ROW ONLY");
            Assert.Equal("first", ColumnName(fetchFirst.Limit!));
            Assert.Null(fetchFirst.Offset);
        }
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void Is_unknown_is_a_boolean_test(string dialectName)
    {
        var select = ParseSingle<SelectStatement>(dialectName, "SELECT id FROM t WHERE unknown IS NOT UNKNOWN");

        var test = Assert.IsType<BooleanTestExpression>(select.Where);
        Assert.Equal("unknown", ColumnName(test.Expression));
        Assert.Equal(BooleanTestKind.Unknown, test.Kind);
        Assert.True(test.IsNegated);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("postgresql")]
    [InlineData("sqlite")]
    public void Window_frames_keep_their_meaning(string dialectName)
    {
        var select = ParseSingle<SelectStatement>(dialectName,
            """
            SELECT
                SUM(groups) OVER (ORDER BY preceding GROUPS BETWEEN UNBOUNDED PRECEDING AND following FOLLOWING),
                SUM(unbounded) OVER (ORDER BY id GROUPS BETWEEN preceding PRECEDING AND UNBOUNDED FOLLOWING),
                SUM(id) OVER (GROUPS 1 PRECEDING),
                SUM(id) OVER groups
            FROM t
            """);
        var windows = select.Projections.Select(item => Assert.IsType<WindowExpression>(item.Expression)).ToArray();

        Assert.Equal(WindowFrameUnit.Groups, windows[0].Frame!.Unit);
        Assert.Null(windows[0].WindowName);
        Assert.Equal("groups", ColumnName(Assert.IsType<FunctionCallExpression>(windows[0].Expression).Arguments[0]));
        Assert.Equal("preceding", ColumnName(windows[0].OrderBy![0].Expression));
        Assert.Equal(WindowFrameBoundKind.UnboundedPreceding, windows[0].Frame!.Start.Kind);
        Assert.Equal(WindowFrameBoundKind.Following, windows[0].Frame!.End!.Kind);
        Assert.Equal("following", ColumnName(windows[0].Frame!.End!.Offset!));

        Assert.Equal(WindowFrameBoundKind.Preceding, windows[1].Frame!.Start.Kind);
        Assert.Equal("preceding", ColumnName(windows[1].Frame!.Start.Offset!));
        Assert.Equal(WindowFrameBoundKind.UnboundedFollowing, windows[1].Frame!.End!.Kind);

        Assert.Equal(WindowFrameUnit.Groups, windows[2].Frame!.Unit);
        Assert.Null(windows[2].WindowName);
        Assert.Equal(WindowFrameBoundKind.Preceding, windows[2].Frame!.Start.Kind);

        Assert.Null(windows[3].Frame);
        Assert.Equal("groups", windows[3].WindowName!.Value);
    }

    [Theory]
    [InlineData("SELECT SUM(id) OVER (groups) FROM t", null)]
    [InlineData("SELECT SUM(id) OVER (groups ORDER BY id) FROM t", "id")]
    public void Window_named_groups_can_be_referenced_without_a_frame(string sql, string? orderedBy)
    {
        var window = Assert.IsType<WindowExpression>(
            Assert.Single(ParseSingle<SelectStatement>("generic", sql).Projections).Expression);

        Assert.Equal("groups", window.WindowName!.Value);
        Assert.Null(window.Frame);
        Assert.Equal(orderedBy, window.OrderBy is null ? null : ColumnName(window.OrderBy[0].Expression));
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("postgresql")]
    [InlineData("sqlite")]
    public void Filter_clause_and_filter_alias_are_distinguished(string dialectName)
    {
        var select = ParseSingle<SelectStatement>(dialectName,
            "SELECT COUNT(*) FILTER (WHERE filter = 1) AS filter, COUNT(*) filter, filter(id) FROM filter");

        var filtered = Assert.IsType<FunctionCallExpression>(select.Projections[0].Expression);
        Assert.Equal("filter", ColumnName(Assert.IsType<BinaryExpression>(filtered.Filter).Left));
        Assert.Equal("filter", select.Projections[0].Alias!.Value);

        Assert.Null(Assert.IsType<FunctionCallExpression>(select.Projections[1].Expression).Filter);
        Assert.Equal("filter", select.Projections[1].Alias!.Value);

        Assert.Equal("filter", Assert.IsType<FunctionCallExpression>(select.Projections[2].Expression).Name.Value);
        Assert.Equal("filter", Assert.IsType<NamedTable>(select.From).Name.Parts[0].Value);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("postgresql")]
    [InlineData("oracle")]
    [InlineData("tsql")]
    public void Merge_matches_keep_their_meaning(string dialectName)
    {
        var merge = ParseSingle<MergeStatement>(dialectName,
            """
            MERGE INTO matched USING s ON matched.id = s.id
            WHEN MATCHED THEN DELETE
            WHEN NOT MATCHED THEN INSERT (matched) VALUES (1);
            """);

        Assert.Equal("matched", merge.Target.Name.Parts[0].Value);
        Assert.Equal(
            [MergeMatchKind.Matched, MergeMatchKind.NotMatched],
            merge.WhenClauses.Select(clause => clause.MatchKind));
        Assert.Equal("matched", Assert.Single(Assert.IsType<MergeInsertAction>(merge.WhenClauses[1].Action).Columns!).Value);
    }

    [Fact]
    public void Top_with_ties_and_join_hints_keep_their_meaning()
    {
        var select = ParseSingle<SelectStatement>("tsql",
            "SELECT TOP 5 WITH TIES ties, hash FROM hash INNER HASH JOIN ties ON hash.id = ties.id ORDER BY ties");

        Assert.True(select.WithTies);
        Assert.Equal(["ties", "hash"], select.Projections.Select(item => ColumnName(item.Expression)));
        var join = Assert.IsType<JoinTable>(select.From);
        Assert.Equal(TSqlJoinHint.Hash, join.Hint);
        Assert.Equal("hash", Assert.IsType<NamedTable>(join.Left).Name.Parts[0].Value);
        Assert.Null(Assert.IsType<NamedTable>(join.Left).Alias);
        Assert.Equal("ties", Assert.IsType<NamedTable>(join.Right).Name.Parts[0].Value);

        Assert.False(ParseSingle<SelectStatement>("tsql", "SELECT TOP 5 ties FROM t").WithTies);
    }

    // SQL Server only takes a join hint after a join type, so a bare HASH is an alias.
    [Fact]
    public void Hash_before_a_bare_join_is_a_table_alias()
    {
        var join = Assert.IsType<JoinTable>(
            ParseSingle<SelectStatement>("tsql", "SELECT hash.id FROM t HASH JOIN u ON hash.id = u.id").From);

        Assert.Null(join.Hint);
        Assert.Equal("HASH", Assert.IsType<NamedTable>(join.Left).Alias!.Value);
    }

    [Theory]
    [InlineData("generic", "CREATE TABLE t (index VARCHAR(10), INDEX ix (index), UNIQUE INDEX ux (index))")]
    [InlineData("postgresql", "CREATE TABLE t (index VARCHAR(10), INDEX ix (index), UNIQUE INDEX ux (index))")]
    public void Index_column_and_index_table_element_are_distinguished(string dialectName, string sql)
    {
        var create = ParseSingle<CreateTableStatement>(dialectName, sql);

        Assert.Equal("index", Assert.IsType<ColumnDefinition>(create.Elements[0]).Name.Value);
        Assert.Equal("ix", Assert.IsType<IndexTableElement>(create.Elements[1]).Name!.Value);
        Assert.True(Assert.IsType<IndexTableElement>(create.Elements[2]).IsUnique);
    }

    [Fact]
    public void Schema_object_keywords_are_not_read_as_names()
    {
        var view = ParseSingle<CreateViewStatement>("postgresql", "CREATE VIEW view AS SELECT view, index FROM view");
        Assert.Equal("view", view.Name.Parts[0].Value);
        var query = Assert.IsType<SelectStatement>(view.Query);
        Assert.Equal(["view", "index"], query.Projections.Select(item => ColumnName(item.Expression)));

        var index = ParseSingle<CreateIndexStatement>("postgresql", "CREATE UNIQUE INDEX index ON view (index)");
        Assert.True(index.IsUnique);
        Assert.Equal("index", index.Name.Parts[0].Value);
        Assert.Equal("view", index.Table.Parts[0].Value);

        Assert.Equal(SchemaObjectKind.View, ParseSingle<DropStatement>("postgresql", "DROP VIEW view").Kind);
        Assert.Equal(SchemaObjectKind.Index, ParseSingle<DropStatement>("postgresql", "DROP INDEX index").Kind);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("postgresql")]
    public void Explain_keywords_keep_their_meaning(string dialectName)
    {
        var analyzed = ParseSingle<ExplainStatement>(dialectName, "EXPLAIN ANALYZE SELECT explain FROM explain");
        Assert.True(analyzed.Analyze);
        Assert.Equal("explain", ColumnName(
            Assert.Single(Assert.IsType<SelectStatement>(analyzed.Query).Projections).Expression));

        Assert.False(ParseSingle<ExplainStatement>(dialectName, "EXPLAIN SELECT id FROM t").Analyze);
        Assert.True(ParseSingle<ExplainStatement>(dialectName, "EXPLAIN (ANALYZE) SELECT id FROM t").Analyze);
    }

    [Fact]
    public void View_security_keeps_its_meaning()
    {
        var view = ParseSingle<CreateViewStatement>("mysql",
            "CREATE SQL SECURITY INVOKER VIEW security AS SELECT definer, invoker FROM security");

        Assert.Equal(ViewSecurity.Invoker, view.Security);
        Assert.Equal("security", view.Name.Parts[0].Value);
        Assert.Equal(["definer", "invoker"], Assert.IsType<SelectStatement>(view.Query).Projections
            .Select(item => ColumnName(item.Expression)));
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("postgresql")]
    public void Parameter_modes_are_not_read_as_names(string dialectName)
    {
        var body = dialectName == "generic"
            ? "BEGIN ATOMIC SELECT 1; END"
            : "LANGUAGE plpgsql AS $cyqwel$ BEGIN SELECT 1; END $cyqwel$";
        var create = ParseSingle<CreateProcedureStatement>(dialectName,
            $"CREATE PROCEDURE language (OUT out INT, INOUT inout INT, IN OUT a INT, IN b INT, c INT) {body}");

        Assert.Equal("language", create.Name.Parts[0].Value);
        Assert.Equal(["out", "inout", "a", "b", "c"], create.Parameters.Select(parameter => parameter.Name.Value));
        Assert.Equal(
            [
                ProcedureParameterMode.Out, ProcedureParameterMode.InOut, ProcedureParameterMode.InOut,
                ProcedureParameterMode.In, ProcedureParameterMode.In,
            ],
            create.Parameters.Select(parameter => parameter.Mode));

        // PostgreSQL reads OUT INT as an unnamed parameter, which the grammar has no form for.
        Assert.False(SqlParser.TryParse($"CREATE PROCEDURE p (OUT INT) {body}", SqlDialectRegistry.Get(dialectName), out _, out _));
    }

    [Fact]
    public void Oracle_parameter_modes_follow_the_name()
    {
        var create = ParseSingle<CreateProcedureStatement>("oracle",
            "CREATE PROCEDURE p (out OUT INT, inout IN OUT INT, language INT) AS BEGIN SELECT 1 FROM dual; END;");

        Assert.Equal(["out", "inout", "language"], create.Parameters.Select(parameter => parameter.Name.Value));
        Assert.Equal(
            [ProcedureParameterMode.Out, ProcedureParameterMode.InOut, ProcedureParameterMode.In],
            create.Parameters.Select(parameter => parameter.Mode));
    }

    [Fact]
    public void Output_arguments_are_not_read_as_names()
    {
        var call = ParseSingle<CallProcedureStatement>("tsql", "EXEC p out, @a OUT, x OUT");

        Assert.Equal("out", ColumnName(call.Arguments[0].Value));
        Assert.False(call.Arguments[0].IsOutput);
        Assert.True(call.Arguments[1].IsOutput);
        Assert.Equal("x", ColumnName(call.Arguments[2].Value));
        Assert.True(call.Arguments[2].IsOutput);
    }

    // Quoting a word the target does not reserve would change how it folds on PostgreSQL and Oracle.
    [Theory]
    [InlineData("generic", "SELECT FIRST, nulls, index, VIEW, out, groups, sql, analyze, only FROM t")]
    [InlineData("postgresql", "SELECT FIRST, nulls, index, VIEW, out, groups, sql, \"analyze\", \"only\" FROM t")]
    [InlineData("sqlite", "SELECT FIRST, nulls, \"index\", VIEW, out, groups, sql, analyze, only FROM t")]
    [InlineData("oracle", "SELECT FIRST, nulls, \"index\", \"VIEW\", out, groups, sql, analyze, only FROM t")]
    [InlineData("tsql", "SELECT FIRST, nulls, [index], [VIEW], out, groups, sql, analyze, only FROM t")]
    [InlineData("mysql", "SELECT FIRST, nulls, `index`, VIEW, `out`, `groups`, `sql`, `analyze`, only FROM t")]
    public void Generation_quotes_audited_words_only_where_the_target_reserves_them(string target, string expected)
    {
        var document = SqlParser.Parse(
            "SELECT FIRST, nulls, index, VIEW, out, groups, sql, analyze, only FROM t",
            SqlDialects.Generic);

        var dialect = SqlDialectRegistry.Get(target);
        var generated = document.ToSql(dialect);
        Assert.Equal(expected, generated);
        Assert.Equal(
            ["FIRST", "nulls", "index", "VIEW", "out", "groups", "sql", "analyze", "only"],
            Assert.IsType<SelectStatement>(Assert.Single(SqlParser.Parse(generated, dialect).Statements))
                .Projections.Select(item => ColumnName(item.Expression)));
    }

    private static T ParseSingle<T>(string dialectName, string sql) where T : SqlStatement =>
        Assert.IsType<T>(Assert.Single(SqlParser.Parse(sql, SqlDialectRegistry.Get(dialectName)).Statements));

    private static string ColumnName(SqlExpression expression) =>
        Assert.Single(Assert.IsType<ColumnExpression>(expression).Parts).Value;

    private static string Dump(object? value) => value switch
    {
        null => "null",
        string or Enum or ValueType when value.GetType().Namespace != typeof(SqlNode).Namespace || value is Enum => value.ToString()!,
        System.Collections.IEnumerable items => $"[{string.Join(", ", items.Cast<object?>().Select(Dump))}]",
        _ => $"{value.GetType().Name}({string.Join(", ", value.GetType().GetProperties()
            .Where(property => property.Name is not ("Span" or "EqualityContract") && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={Dump(property.GetValue(value))}"))})",
    };

    private static AlterTableAction SingleAction(SqlDialect dialect, string sql) =>
        Assert.Single(Assert.IsType<AlterTableStatement>(Assert.Single(dialect.Parse(sql).Statements)).Actions);
}

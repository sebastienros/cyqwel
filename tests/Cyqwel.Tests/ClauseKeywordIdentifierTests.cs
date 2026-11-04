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
    ];

    // Words each vendor documents as reserved; generic has no vendor and reserves none of them.
    private static readonly Dictionary<string, string[]> Reserved = new()
    {
        ["generic"] = [],
        ["postgresql"] = [],
        ["sqlite"] = ["ADD"],
        ["oracle"] = ["ADD", "INCREMENT", "RENAME"],
        ["tsql"] = ["ADD", "CASCADE", "IDENTITY", "KEY", "RESTRICT"],
        ["mysql"] = ["ADD", "CASCADE", "GENERATED", "KEY", "MAXVALUE", "RENAME", "RESTRICT", "STORED", "VIRTUAL"],
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

    private static AlterTableAction SingleAction(SqlDialect dialect, string sql) =>
        Assert.Single(Assert.IsType<AlterTableStatement>(Assert.Single(dialect.Parse(sql).Statements)).Actions);
}

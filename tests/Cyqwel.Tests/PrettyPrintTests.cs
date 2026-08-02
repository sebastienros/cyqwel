using Cyqwel.Generation;
using Cyqwel.Parsing;

namespace Cyqwel.Tests;

public class PrettyPrintTests
{
    private static readonly SqlGenerationOptions PrettyPrint = new() { PrettyPrint = true };

    public static TheoryData<string, string> FormattingCases => new()
    {
        {
            """
            select a.id, b.name
            from accounts a
            left join profiles b on b.account_id = a.id
            where a.active = true
            group by a.id, b.name
            having count(*) > 0
            order by b.name desc
            limit 10 offset 5
            """,
            """
            SELECT
              a.id, b.name
            FROM accounts AS a
              LEFT JOIN profiles AS b ON b.account_id = a.id
            WHERE a.active = TRUE
            GROUP BY a.id, b.name
            HAVING COUNT(*) > 0
            ORDER BY b.name DESC
            LIMIT 10
            OFFSET 5
            """
        },
        {
            """
            insert into archive (id, name)
            select id, name from users where active = true
            """,
            """
            INSERT INTO archive (id, name)
            SELECT
              id, name
            FROM users
            WHERE active = TRUE
            """
        },
        {
            """
            update users
            set active = false, name = 'disabled'
            where deleted_at is not null
            returning id
            """,
            """
            UPDATE users
            SET active = FALSE, name = 'disabled'
            WHERE deleted_at IS NOT NULL
            RETURNING id
            """
        },
        {
            """
            select id from current_users
            union all
            select id from archived_users
            order by id limit 5
            """,
            """
            SELECT
              id
            FROM current_users
            UNION ALL
            SELECT
              id
            FROM archived_users
            ORDER BY id
            LIMIT 5
            """
        },
    };

    [Theory]
    [MemberData(nameof(FormattingCases))]
    public void Pretty_prints_sql_and_remains_stable(string sql, string expected)
    {
        var formatted = SqlParser.Parse(sql).ToSql(options: PrettyPrint);

        Assert.Equal(expected.ReplaceLineEndings(), formatted);
        Assert.Equal(formatted, SqlParser.Parse(formatted).ToSql(options: PrettyPrint));
    }

    [Fact]
    public void Pretty_print_honors_indent_size()
    {
        var options = new SqlGenerationOptions { PrettyPrint = true, IndentSize = 4 };
        var formatted = SqlParser.Parse(
            "SELECT a.id FROM accounts a INNER JOIN profiles b ON b.account_id = a.id")
            .ToSql(options: options);

        Assert.Equal(
            """
            SELECT
                a.id
            FROM accounts AS a
                INNER JOIN profiles AS b ON b.account_id = a.id
            """.ReplaceLineEndings(),
            formatted);
    }
}

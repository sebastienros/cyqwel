using Cyqwel.Dialects;

namespace Cyqwel.Benchmarks;

internal sealed record SqlQuery(SqlDialect Dialect, string Sql);

internal static class SqlQueries
{
    public static IReadOnlyDictionary<string, SqlQuery> All { get; } = new Dictionary<string, SqlQuery>
    {
        ["Simple"] = new(SqlDialects.Generic, "SELECT id, name FROM users WHERE id = 1"),
        ["Joins"] = new(SqlDialects.Generic, """
            SELECT c.id, c.name, COUNT(o.id) AS order_count, SUM(o.total) AS revenue
            FROM customers AS c
            INNER JOIN orders AS o ON o.customer_id = c.id
            LEFT JOIN regions AS r ON r.id = c.region_id
            WHERE o.created_at >= '2026-01-01' AND r.name IN ('EMEA', 'APAC') AND o.total > 100
            GROUP BY c.id, c.name
            HAVING SUM(o.total) > 1000
            ORDER BY revenue DESC
            """),
        ["CteWindow"] = new(SqlDialects.PostgreSql, """
            WITH recent AS (
                SELECT customer_id, total, created_at FROM orders WHERE created_at >= '2026-01-01'
            ),
            ranked AS (
                SELECT customer_id, total,
                    ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY created_at DESC) AS position,
                    SUM(total) OVER (PARTITION BY customer_id) AS lifetime_total
                FROM recent
            )
            SELECT customer_id, total, lifetime_total,
                CASE WHEN lifetime_total > 1000 THEN 'gold' WHEN lifetime_total > 100 THEN 'silver' ELSE 'bronze' END AS tier
            FROM ranked
            WHERE position = 1
            ORDER BY lifetime_total DESC
            LIMIT 50
            """),
        ["Dml"] = new(SqlDialects.Generic, """
            INSERT INTO audit (id, kind, created_by) VALUES (1, 'create', 'alice'), (2, 'update', 'bob');
            UPDATE accounts SET balance = balance - 10, updated_by = 'alice' WHERE id = 1 AND balance >= 10;
            DELETE FROM sessions WHERE expires_at < '2026-01-01';
            """),
        ["TSqlBatch"] = new(SqlDialects.TSql, """
            -- Native T-SQL names, hints and batch separators.
            SELECT TOP (10) c.[id], c.[name], o.[total]
            FROM [sales].[customers] AS c WITH (NOLOCK)
            INNER JOIN .dbo.orders AS o ON o.customer_id = c.id
            WHERE c.name LIKE N'A%'
            ORDER BY o.total DESC;
            GO
            UPDATE TOP (5) db..accounts SET balance = balance - @amount WHERE id = @id;
            GO
            """),
    };
}

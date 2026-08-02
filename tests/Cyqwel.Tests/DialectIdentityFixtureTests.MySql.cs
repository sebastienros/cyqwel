using Cyqwel.Dialects;

namespace Cyqwel.Tests;

// Regenerated through Polyglot at 7c4f1f2 from SQLGlot v30.12.0; MySQL cases are a supported static subset.
public partial class DialectIdentityFixtureTests
{
    [InlineData("mysql-identity-4", "CREATE TABLE foo (id BIGINT)")]
    [InlineData("mysql-identity-6", "CREATE TABLE temp (id SERIAL PRIMARY KEY)")]
    [InlineData("mysql-identity-8", "UPDATE /*+ MAX_EXECUTION_TIME(1) */ t SET a = 1")]
    [InlineData("mysql-identity-10", "DELETE /*+ MAX_EXECUTION_TIME(1) */ FROM t WHERE a = 1")]
    [InlineData("mysql-identity-46", "CREATE SQL SECURITY INVOKER VIEW id_test (id, foo) AS SELECT 0, foo FROM test")]
    [InlineData("mysql-identity-47", "CREATE SQL SECURITY DEFINER VIEW id_test (id, foo) AS SELECT 0, foo FROM test")]
    [InlineData("mysql-identity-68", "CREATE TABLE test (id INT, CONSTRAINT pk_name PRIMARY KEY (id))")]
    [InlineData("mysql-identity-69", "CREATE TABLE test (a INT, b INT GENERATED ALWAYS AS (a + a) STORED)")]
    [InlineData("mysql-identity-70", "CREATE TABLE test (a INT, b INT GENERATED ALWAYS AS (a + a) VIRTUAL)")]
    [InlineData("mysql-identity-74", "CREATE TABLE t (name VARCHAR)")]
    [InlineData("mysql-identity-81", "CREATE TABLE test (ts TIMESTAMP, ts_tz TIMESTAMPTZ, ts_ltz TIMESTAMPLTZ)")]
    [InlineData("mysql-identity-107", "ALTER TABLE test_table ALTER COLUMN test_column SET DEFAULT 1")]
    [InlineData("mysql-identity-108", "SELECT DATE_FORMAT(NOW(), '%Y-%m-%d %H:%i:00.0000')")]
    [InlineData("mysql-identity-114", "SELECT /*+ BKA(t1) NO_BKA(t2) */ * FROM t1 INNER JOIN t2")]
    [InlineData("mysql-identity-115", "SELECT /*+ MERGE(dt) */ * FROM (SELECT * FROM t1) AS dt")]
    [InlineData("mysql-identity-116", "SELECT /*+ INDEX(t, i) */ c1 FROM t WHERE c2 = 'value'")]
    [InlineData("mysql-identity-122", "SELECT CURRENT_TIMESTAMP(6)")]
    [InlineData("mysql-identity-123", "SELECT CURRENT_ROLE()")]
    [InlineData("mysql-identity-124", "SELECT CURTIME()")]
    [InlineData("mysql-identity-126", "SELECT CAST(`a`.`b` AS CHAR) FROM foo")]
    [InlineData("mysql-identity-134", "SELECT a || b")]
    [InlineData("mysql-identity-142", "SELECT 1 AS row")]
    [InlineData("mysql-identity-198", "SELECT ELT(2, 'foo', 'bar', 'baz') AS Result")]
    [InlineData("mysql-identity-200", "SELECT VERSION()")]
    [InlineData("mysql-identity-208", "SELECT INSTR('str', 'substr')")]
    [InlineData("mysql-identity-209", "SELECT UCASE('foo')")]
    [InlineData("mysql-identity-210", "SELECT LCASE('foo')")]
    [InlineData("mysql-identity-211", "SELECT DAY_OF_MONTH('2023-01-01')")]
    [InlineData("mysql-identity-212", "SELECT DAY_OF_WEEK('2023-01-01')")]
    [InlineData("mysql-identity-213", "SELECT DAY_OF_YEAR('2023-01-01')")]
    [InlineData("mysql-identity-214", "SELECT WEEK_OF_YEAR('2023-01-01')")]
    [InlineData("mysql-identity-215", "CREATE TABLE t (foo VARBINARY(5))")]
    [InlineData("mysql-identity-232", "SELECT FROM_UNIXTIME(1711366265, '%Y %D %M')")]
    [InlineData("mysql-identity-233", "SELECT TIME_STR_TO_TIME('2023-01-01 13:14:15.123456+00:00')")]
    [InlineData("mysql-identity-234", "SELECT TIME_STR_TO_TIME('2023-01-01 13:14:15.123+00:00')")]
    [InlineData("mysql-identity-235", "SELECT TIME_STR_TO_TIME('2023-01-01 13:14:15+00:00')")]
    [InlineData("mysql-identity-236", "SELECT TIME_STR_TO_TIME('2023-01-01 13:14:15-08:00', 'America/Los_Angeles')")]
    [InlineData("mysql-identity-237", "SELECT TIME_STR_TO_TIME('2023-01-01 13:14:15-08:00', 'America/Los_Angeles')")]
    [InlineData("mysql-identity-238", "SELECT TIME_STR_TO_TIME('2023-01-01 13:14:15.12345+00:00')")]
    [InlineData("mysql-identity-239", "SELECT TIME_STR_TO_TIME('2023-01-01 13:14:15.1234+00:00')")]
    [InlineData("mysql-identity-240", "SELECT TIME_STR_TO_TIME('2023-01-01 13:14:15.12+00:00')")]
    [InlineData("mysql-identity-241", "SELECT TIME_STR_TO_TIME('2023-01-01 13:14:15.1+00:00')")]
    [InlineData("mysql-identity-265", "SELECT JSON_OBJECT('id', 87, 'name', 'carrot')")]
    [Theory]
    public void MySql_parse_generate_parse_is_stable(string caseName, string sql) =>
        AssertStable(SqlDialects.MySql, caseName, sql);
}

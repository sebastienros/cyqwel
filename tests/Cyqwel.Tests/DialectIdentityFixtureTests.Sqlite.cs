using Cyqwel.Dialects;

namespace Cyqwel.Tests;

// Regenerated through Polyglot at 7c4f1f2 from SQLGlot v30.12.0; SQLite cases are a supported static subset.
public partial class DialectIdentityFixtureTests
{
    [InlineData("sqlite-identity-0", "WITH xyz(x) AS (SELECT 1) SELECT x FROM xyz")]
    [InlineData("sqlite-identity-5", "SELECT match FROM t")]
    [InlineData("sqlite-identity-7", "SELECT RANK() OVER (RANGE CURRENT ROW) FROM tbl")]
    [InlineData("sqlite-identity-9", "SELECT DATE()")]
    [InlineData("sqlite-identity-10", "SELECT DATE('now', 'start of month', '+1 month', '-1 day')")]
    [InlineData("sqlite-identity-11", "SELECT DATETIME(1092941466, 'unixepoch')")]
    [InlineData("sqlite-identity-12", "SELECT DATETIME(1092941466, 'auto')")]
    [InlineData("sqlite-identity-13", "SELECT DATETIME(1092941466, 'unixepoch', 'localtime')")]
    [InlineData("sqlite-identity-14", "SELECT UNIXEPOCH()")]
    [InlineData("sqlite-identity-15", "SELECT JULIANDAY('now') - JULIANDAY('1776-07-04')")]
    [InlineData("sqlite-identity-16", "SELECT UNIXEPOCH() - UNIXEPOCH('2004-01-01 02:34:56')")]
    [InlineData("sqlite-identity-17", "SELECT DATE('now', 'start of year', '+9 months', 'weekday 2')")]
    [InlineData("sqlite-identity-18", "SELECT (JULIANDAY('now') - 2440587.5) * 86400.0")]
    [InlineData("sqlite-identity-19", "SELECT UNIXEPOCH('now', 'subsec')")]
    [InlineData("sqlite-identity-20", "SELECT TIMEDIFF('now', '1809-02-12')")]
    [InlineData("sqlite-identity-22", "SELECT INSTR(haystack, needle)")]
    [InlineData("sqlite-identity-23", "SELECT a, SUM(b) OVER (ORDER BY a ROWS BETWEEN -1 PRECEDING AND 1 FOLLOWING) FROM t1 ORDER BY 1")]
    [InlineData("sqlite-identity-24", "SELECT JSON_EXTRACT('[10, 20, [30, 40]]', '$[2]', '$[0]', '$[1]')")]
    [InlineData("sqlite-identity-25", "SELECT item AS \"item\", some AS \"some\" FROM data WHERE (item = 'value_1' COLLATE NOCASE) AND (some = 't' COLLATE NOCASE) ORDER BY item ASC LIMIT 1 OFFSET 0")]
    [InlineData("sqlite-identity-28", "SELECT * FROM t1, t2")]
    [InlineData("sqlite-identity-30", "ALTER TABLE t1 RENAME TO t2")]
    [InlineData("sqlite-identity-32", "SELECT JSON_OBJECT('col1', 1, 'col2', '1')")]
    [InlineData("sqlite-identity-33", "CREATE TABLE \"foo t\" (\"foo t id\" TEXT NOT NULL, PRIMARY KEY (\"foo t id\"))")]
    [InlineData("sqlite-identity-42", "SELECT SQLITE_VERSION()")]
    [InlineData("sqlite-identity-43", "SELECT STRFTIME('%Y/%m/%d', 'now')")]
    [InlineData("sqlite-identity-44", "SELECT STRFTIME('%Y-%m-%d', '2016-10-16', 'start of month')")]
    [InlineData("sqlite-identity-45", "SELECT STRFTIME('%s')")]
    [InlineData("sqlite-identity-57", "CREATE TEMPORARY TABLE foo (id INTEGER)")]
    [Theory]
    public void Sqlite_parse_generate_parse_is_stable(string caseName, string sql) =>
        AssertStable(SqlDialects.Sqlite, caseName, sql);
}

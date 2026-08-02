using Cyqwel.Dialects;

namespace Cyqwel.Tests;

// Regenerated through Polyglot at 7c4f1f2 from SQLGlot v30.12.0; Oracle cases are a supported static subset.
public partial class DialectIdentityFixtureTests
{
    [InlineData("oracle-identity-2", "SELECT BITMAP_BUCKET_NUMBER(32769)")]
    [InlineData("oracle-identity-3", "SELECT BITMAP_CONSTRUCT_AGG(value)")]
    [InlineData("oracle-identity-14", "ALTER TABLE Payments ADD Stock NUMBER NOT NULL")]
    [InlineData("oracle-identity-21", "SELECT :OBJECT")]
    [InlineData("oracle-identity-29", "SELECT STANDARD_HASH('hello')")]
    [InlineData("oracle-identity-30", "SELECT STANDARD_HASH('hello', 'MD5')")]
    [InlineData("oracle-identity-35", "SELECT TO_DATE('January 15, 1989, 11:00 A.M.')")]
    [InlineData("oracle-identity-36", "SELECT INSTR(haystack, needle)")]
    [InlineData("oracle-identity-42", "SELECT last_name, employee_id, manager_id, LEVEL FROM employees START WITH employee_id = 100 CONNECT BY PRIOR employee_id = manager_id ORDER SIBLINGS BY last_name")]
    [InlineData("oracle-identity-48", "SELECT * FROM t WHERE c LIKE (:v)")]
    [InlineData("oracle-identity-55", "SELECT TRUNC(SYSDATE)")]
    [InlineData("oracle-identity-60", "SELECT * FROM T ORDER BY I OFFSET NVL(:variable1, 10) ROWS FETCH NEXT NVL(:variable2, 10) ROWS ONLY")]
    [InlineData("oracle-identity-62", "SELECT TO_CHAR(-100, 'L99', 'NL_CURRENCY = '' AusDollars '' ')")]
    [InlineData("oracle-identity-63", "SELECT * FROM t START WITH col CONNECT BY NOCYCLE PRIOR col1 = col2")]
    [InlineData("oracle-identity-67", "SELECT * FROM t ORDER BY a ASC NULLS LAST, b ASC NULLS FIRST, c DESC NULLS LAST, d DESC NULLS FIRST")]
    [InlineData("oracle-identity-68", "SELECT /*+ ORDERED */* FROM tbl")]
    [InlineData("oracle-identity-69", "SELECT /* test */ /*+ ORDERED */* FROM tbl")]
    [InlineData("oracle-identity-70", "SELECT /*+ ORDERED */*/* test */ FROM tbl")]
    [InlineData("oracle-identity-73", "SELECT TO_TIMESTAMP('05 Dec 2000 10:00 AM', 'DD Mon YYYY HH12:MI AM')")]
    [InlineData("oracle-identity-74", "SELECT TO_TIMESTAMP('05 Dec 2000 10:00 PM', 'DD Mon YYYY HH12:MI PM')")]
    [InlineData("oracle-identity-75", "SELECT TO_TIMESTAMP('05 Dec 2000 10:00 A.M.', 'DD Mon YYYY HH12:MI A.M.')")]
    [InlineData("oracle-identity-76", "SELECT TO_TIMESTAMP('05 Dec 2000 10:00 P.M.', 'DD Mon YYYY HH12:MI P.M.')")]
    [InlineData("oracle-identity-77", "SELECT CUME_DIST(15, 0.05) WITHIN GROUP (ORDER BY col1, col2) FROM t")]
    [InlineData("oracle-identity-78", "SELECT DENSE_RANK(15, 0.05) WITHIN GROUP (ORDER BY col1, col2) FROM t")]
    [InlineData("oracle-identity-79", "SELECT RANK(15, 0.05) WITHIN GROUP (ORDER BY col1, col2) FROM t")]
    [InlineData("oracle-identity-80", "SELECT PERCENT_RANK(15, 0.05) WITHIN GROUP (ORDER BY col1, col2) FROM t")]
    [InlineData("oracle-identity-84", "SELECT /*+ USE_NL(A B) */ A.COL_TEST FROM TABLE_A A, TABLE_B B")]
    [InlineData("oracle-identity-85", "SELECT /*+ INDEX(v.j jhist_employee_ix (employee_id start_date)) */ * FROM v")]
    [InlineData("oracle-identity-86", "SELECT /*+ USE_NL(A B C) */ A.COL_TEST FROM TABLE_A A, TABLE_B B, TABLE_C C")]
    [InlineData("oracle-identity-87", "SELECT /*+ NO_INDEX(employees emp_empid) */ employee_id FROM employees WHERE employee_id > 200")]
    [InlineData("oracle-identity-88", "SELECT /*+ NO_INDEX_FFS(items item_order_ix) */ order_id FROM order_items items")]
    [InlineData("oracle-identity-89", "SELECT /*+ LEADING(e j) */ * FROM employees e, departments d, job_history j WHERE e.department_id = d.department_id AND e.hire_date = j.start_date")]
    [InlineData("oracle-identity-90", "INSERT /*+ APPEND */ INTO IAP_TBL (id, col1) VALUES (2, 'test2')")]
    [InlineData("oracle-identity-91", "INSERT /*+ APPEND_VALUES */ INTO dest_table VALUES (i, 'Value')")]
    [InlineData("oracle-identity-94", "SELECT /*+ LEADING(departments employees) USE_NL(employees) */ * FROM employees JOIN departments ON employees.department_id = departments.department_id")]
    [InlineData("oracle-identity-95", "SELECT /*+ USE_NL(bbbbbbbbbbbbbbbbbbbbbbbb) LEADING(aaaaaaaaaaaaaaaaaaaaaaaa bbbbbbbbbbbbbbbbbbbbbbbb cccccccccccccccccccccccc dddddddddddddddddddddddd) INDEX(cccccccccccccccccccccccc) */ * FROM aaaaaaaaaaaaaaaaaaaaaaaa JOIN bbbbbbbbbbbbbbbbbbbbbbbb ON aaaaaaaaaaaaaaaaaaaaaaaa.id = bbbbbbbbbbbbbbbbbbbbbbbb.a_id JOIN cccccccccccccccccccccccc ON bbbbbbbbbbbbbbbbbbbbbbbb.id = cccccccccccccccccccccccc.b_id JOIN dddddddddddddddddddddddd ON cccccccccccccccccccccccc.id = dddddddddddddddddddddddd.c_id")]
    [InlineData("oracle-identity-96", "SELECT /*+ USE_NL(bbbbbbbbbbbbbbbbbbbbbbbb) LEADING(aaaaaaaaaaaaaaaaaaaaaaaa bbbbbbbbbbbbbbbbbbbbbbbb cccccccccccccccccccccccc dddddddddddddddddddddddd) INDEX(cccccccccccccccccccccccc) */ * FROM aaaaaaaaaaaaaaaaaaaaaaaa JOIN bbbbbbbbbbbbbbbbbbbbbbbb ON aaaaaaaaaaaaaaaaaaaaaaaa.id = bbbbbbbbbbbbbbbbbbbbbbbb.a_id JOIN cccccccccccccccccccccccc ON bbbbbbbbbbbbbbbbbbbbbbbb.id = cccccccccccccccccccccccc.b_id JOIN dddddddddddddddddddddddd ON cccccccccccccccccccccccc.id = dddddddddddddddddddddddd.c_id")]
    [InlineData("oracle-identity-97", "SELECT /*+ LEADING(departments employees) USE_NL(employees) select where group by is order by */ * FROM employees JOIN departments ON employees.department_id = departments.department_id")]
    [InlineData("oracle-identity-98", "SELECT /*+ LEADING(departments, employees) */ * FROM employees JOIN departments ON employees.department_id = departments.department_id")]
    [InlineData("oracle-identity-99", "SELECT /*+ LEADING(departments select) */ * FROM employees JOIN departments ON employees.department_id = departments.department_id")]
    [InlineData("oracle-identity-151", "SELECT id, PRIOR name AS parent_name, name FROM tree CONNECT BY NOCYCLE PRIOR id = parent_id")]
    [InlineData("oracle-identity-154", "WITH t AS (SELECT 1 AS COL) SELECT col, ROWID FROM t WHERE ROWNUM = 1")]
    [InlineData("oracle-identity-156", "SELECT CHR(187)")]
    [Theory]
    public void Oracle_parse_generate_parse_is_stable(string caseName, string sql) =>
        AssertStable(SqlDialects.Oracle, caseName, sql);
}

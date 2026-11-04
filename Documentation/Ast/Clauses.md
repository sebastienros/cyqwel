# Ast / Clauses.cs

Table sources and clauses.

## TableName
Qualified name (`Parts`), `IsVariable` for table variables. Constructor splits `"schema.table"`.

## TableSource (abstract record)
Base of everything usable in `FROM`.

## NamedTable
Table reference with optional alias, T-SQL `Hints` and `Sample` (`TABLESAMPLE`).

## DerivedTable
Subquery in `FROM` with a mandatory alias and optional column aliases.

## JoinKind (enum) / JoinSyntax (enum)
`Inner`, `Left`, `Right`, `Full`, `Cross`, `OuterApply`, `CrossApply`; `Explicit` (`JOIN`) or `Comma`.

## JoinTable
Join of two sources with `Condition`, `Using`, `IsNatural` and optional T-SQL `Hint`.

## SelectItem
Projection with optional alias, plus T-SQL assignment form (`@v = expr`, `+=` etc.) through `AssignmentTarget` / `AssignmentOperator`.

## OrderDirection (enum) / NullOrder (enum) / OrderByItem
`Ascending`, `Descending`, `Unspecified`; `NULLS FIRST|LAST`. Single `ORDER BY` entry.

## CommonTableExpression / CteMaterialization (enum)
`WITH name [(cols)] AS [MATERIALIZED|NOT MATERIALIZED] (query)`.

## SqlAssignmentOperator (enum) / Assignment
`=`, `+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, concatenation. `Assignment(Column, Value)` is a `SET` entry in `UPDATE`.

## WindowDefinition
Named window of the `WINDOW` clause.

## WindowFrameUnit / WindowFrameBoundKind (enums)
`Rows`, `Range`, `Groups`; `UnboundedPreceding`, `Preceding`, `CurrentRow`, `Following`, `UnboundedFollowing`.

## WindowFrameBound / WindowFrame
Frame bound (with optional offset) and complete frame `unit BETWEEN start AND end`.

## ConnectByClause
Oracle hierarchical query: `CONNECT BY [NOCYCLE] condition [START WITH ...]`.

## MergeMatchKind (enum)
`Matched`, `NotMatched`, `NotMatchedBySource`.

## MergeAction / MergeUpdateAction / MergeInsertAction / MergeDeleteAction
Actions of a `WHEN` branch in `MERGE`: update (with optional `DeleteWhere`), insert, delete.

## MergeWhenClause
One `WHEN [NOT] MATCHED [BY SOURCE] [AND condition] THEN action` branch.



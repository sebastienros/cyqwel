# Ast / Routines.cs

Procedural SQL: procedures, variables, control flow, transactions.

## ProcedureParameterMode (enum) / ProcedureParameter
`In`, `Out`, `InOut`. Procedure parameter with type, mode and default.

## LocalVariable / LocalVariableExpression
Declared variable (type + initializer) and its use in an expression.

## ProceduralBlock
Block with `Variables` and `Statements` (`BEGIN ... END`).

## ProceduralIfStatement / ProceduralWhileStatement
`IF cond THEN ... [ELSE ...]` and `WHILE cond ...`. The while statement keeps internal source labels (MySQL-style).

## ProceduralBreakStatement / ProceduralContinueStatement / ProceduralReturnStatement
`BREAK`, `CONTINUE` (optional internal target label) and `RETURN [value]`.

## PrintStatement / ExecuteSqlStatement
`PRINT value` and dynamic SQL execution (`EXEC(...)`).

## DeclareStatement / TableVariableDeclarationStatement
`DECLARE` of variables, and `DECLARE @t TABLE (...)`.

## SetVariableStatement
`SET @var = value` with a compound assignment operator.

## TransactionKind (enum) / TransactionStatement
`Begin`, `Commit`, `Rollback`, with optional name, `WORK`, `WITH MARK` and `DelayedDurability`. `HasValidModifiers` (internal) validates option combinations.

## ApplicationSetOption (enum)
`NoCount`, `XactAbort`.

## ProcedureArgument
Call argument: value, optional name (`@p = value`), `IsOutput`.

## CreateProcedureStatement / ReplaceProcedureStatement / DropProcedureStatement / CallProcedureStatement
Create, `CREATE OR REPLACE`, drop (optional parameter types, `IfExists`) and call (`CALL` / `EXEC`, with optional `ReturnVariable`) procedures.

## ProceduralDollarQuotes (internal static class)
Holds the `$cyqwel$` tag used for dollar-quoted bodies (PostgreSQL).

## DynamicSqlCommands (internal static class)
`IsSupported(expression)`: accepts only string literals, parameters, variables and `+` concatenations of those as dynamic SQL commands.



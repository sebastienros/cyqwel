using Cyqwel.Ast;

namespace Cyqwel.Generation;

public sealed partial class SqlGenerator
{
    internal abstract class RoutineRenderer
    {
        internal static RoutineRenderer Unsupported { get; } = new UnsupportedRenderer();
        internal static RoutineRenderer Ansi { get; } = new AnsiRenderer();
        internal static RoutineRenderer TSql { get; } = new TSqlRenderer();
        internal static RoutineRenderer PostgreSql { get; } = new PostgreSqlRenderer();
        internal static RoutineRenderer MySql { get; } = new MySqlRenderer();
        internal static RoutineRenderer Oracle { get; } = new OracleRenderer();

        internal virtual bool SupportsReplaceProcedure => false;
        internal virtual bool SupportsParameterDefaults => false;
        internal virtual bool SupportsNamedArguments => false;
        internal virtual bool UsesDollarQuotedBodies => false;
        internal virtual bool SupportsTopLevelControlFlow => false;

        internal virtual void WriteProcedureDefinition(
            SqlGenerator sql,
            TableName name,
            IReadOnlyList<ProcedureParameter> parameters,
            ProceduralBlock body,
            bool replace) => sql.Unsupported($"{sql._dialect.Name} cannot generate procedure definitions.");

        internal virtual void WriteAnonymousBlock(SqlGenerator sql, ProceduralBlock block) =>
            sql.Unsupported($"{sql._dialect.Name} cannot generate anonymous procedural blocks.");

        internal virtual void WriteParameter(SqlGenerator sql, ProcedureParameter parameter) =>
            sql.Unsupported($"{sql._dialect.Name} cannot generate procedure parameters.");

        internal virtual void WriteLocalVariable(SqlGenerator sql, LocalVariable variable) =>
            sql.Unsupported($"{sql._dialect.Name} cannot generate local variables.");

        internal virtual void WriteIf(SqlGenerator sql, ProceduralIfStatement statement) =>
            sql.Unsupported($"{sql._dialect.Name} cannot generate IF statements.");

        internal virtual void WriteWhile(
            SqlGenerator sql,
            ProceduralWhileStatement statement,
            string label) => sql.Unsupported($"{sql._dialect.Name} cannot generate WHILE statements.");

        internal virtual void WriteLoopControl(SqlGenerator sql, bool isContinue, string label) =>
            sql.Unsupported($"{sql._dialect.Name} cannot generate loop control statements.");

        internal virtual void WriteReturn(SqlGenerator sql) =>
            sql.Unsupported($"{sql._dialect.Name} cannot generate RETURN statements.");

        internal virtual void WriteDropProcedure(SqlGenerator sql, DropProcedureStatement drop) =>
            sql.Unsupported($"{sql._dialect.Name} cannot generate DROP PROCEDURE.");

        internal virtual void WriteCallProcedure(SqlGenerator sql, CallProcedureStatement call) =>
            sql.Unsupported($"{sql._dialect.Name} cannot generate procedure calls.");

        internal virtual void WriteArgument(SqlGenerator sql, ProcedureArgument argument) =>
            sql.Unsupported($"{sql._dialect.Name} cannot generate procedure arguments.");

        internal virtual void WriteLocalReference(SqlGenerator sql, LocalVariableExpression variable) =>
            sql.Unsupported($"{sql._dialect.Name} cannot generate local variable references.");
    }

    private sealed class UnsupportedRenderer : RoutineRenderer;

    private class AnsiRenderer : RoutineRenderer
    {
        internal override bool SupportsReplaceProcedure => true;
        internal override bool SupportsParameterDefaults => true;
        internal override bool SupportsNamedArguments => true;

        internal override void WriteProcedureDefinition(
            SqlGenerator sql,
            TableName name,
            IReadOnlyList<ProcedureParameter> parameters,
            ProceduralBlock body,
            bool replace)
        {
            sql.Keyword(replace ? "CREATE OR REPLACE PROCEDURE" : "CREATE PROCEDURE");
            sql.Space();
            sql.WriteTableName(name);
            sql.WriteProcedureParameters(parameters);
            sql.Space();
            sql.Keyword("BEGIN ATOMIC");
            sql.WriteProceduralBlockContents(body, declarationsInsideBody: true);
            sql.ProceduralLineBreak();
            sql.Keyword("END");
        }

        internal override void WriteAnonymousBlock(SqlGenerator sql, ProceduralBlock block)
        {
            sql.Keyword("BEGIN ATOMIC");
            sql.WriteProceduralBlockContents(block, declarationsInsideBody: true);
            sql.ProceduralLineBreak();
            sql.Keyword("END");
        }

        internal override void WriteParameter(SqlGenerator sql, ProcedureParameter parameter)
        {
            sql.Keyword(parameter.Mode switch
            {
                ProcedureParameterMode.In => "IN",
                ProcedureParameterMode.Out => "OUT",
                ProcedureParameterMode.InOut => "INOUT",
                _ => throw new ArgumentOutOfRangeException(),
            });
            sql.Space();
            sql.WriteIdentifier(parameter.Name);
            sql.Space();
            sql.WriteDataType(parameter.DataType);
            sql.WriteProcedureParameterDefault(parameter, "DEFAULT");
        }

        internal override void WriteLocalVariable(SqlGenerator sql, LocalVariable variable)
        {
            sql.Keyword("DECLARE");
            sql.Space();
            sql.WriteIdentifier(variable.Name);
            sql.Space();
            sql.WriteDataType(variable.DataType);
            WriteInitializer(sql, variable, ":=");
        }

        internal override void WriteIf(SqlGenerator sql, ProceduralIfStatement statement)
        {
            sql.Keyword("IF");
            sql.Space();
            sql.WriteExpression(statement.Condition);
            sql.ProceduralLineBreak();
            sql.Keyword("THEN");
            sql.WriteProceduralStatements(statement.Then);
            if (statement.Else is { Count: > 0 })
            {
                sql.ProceduralLineBreak();
                sql.Keyword("ELSE");
                sql.WriteProceduralStatements(statement.Else);
            }
            sql.ProceduralLineBreak();
            sql.Keyword("END IF");
        }

        internal override void WriteWhile(
            SqlGenerator sql,
            ProceduralWhileStatement statement,
            string label)
        {
            sql._builder.Append(label);
            sql._builder.Append(": ");
            sql.Keyword("WHILE");
            sql.Space();
            sql.WriteExpression(statement.Condition);
            sql.Space();
            sql.Keyword("DO");
            sql.WriteProceduralStatements(statement.Statements);
            sql.ProceduralLineBreak();
            sql.Keyword("END WHILE");
        }

        internal override void WriteLoopControl(SqlGenerator sql, bool isContinue, string label)
        {
            sql.Keyword(isContinue ? "ITERATE" : "LEAVE");
            sql.Space();
            sql._builder.Append(label);
        }

        internal override void WriteReturn(SqlGenerator sql) => sql.Keyword("RETURN");

        internal override void WriteDropProcedure(SqlGenerator sql, DropProcedureStatement drop) =>
            WriteStandardDrop(sql, drop, includeSignature: false);

        internal override void WriteCallProcedure(SqlGenerator sql, CallProcedureStatement call)
        {
            sql.Keyword("CALL");
            sql.Space();
            sql.WriteTableName(call.Name);
            sql._builder.Append('(');
            sql.WriteSeparated(call.Arguments, sql.WriteProcedureArgument);
            sql._builder.Append(')');
        }

        internal override void WriteArgument(SqlGenerator sql, ProcedureArgument argument)
        {
            if (argument.Name is not null)
            {
                sql.WriteIdentifier(argument.Name);
                sql._builder.Append(" => ");
            }
            sql.WriteExpression(argument.Value);
        }

        internal override void WriteLocalReference(SqlGenerator sql, LocalVariableExpression variable) =>
            sql.WriteIdentifier(variable.Name);

        protected static void WriteInitializer(SqlGenerator sql, LocalVariable variable, string keyword)
        {
            if (variable.Initializer is null) return;
            sql.Space();
            sql.Keyword(keyword);
            sql.Space();
            sql.WriteExpression(variable.Initializer);
        }

        protected static void WriteStandardDrop(
            SqlGenerator sql,
            DropProcedureStatement drop,
            bool includeSignature)
        {
            sql.Keyword("DROP PROCEDURE");
            if (drop.IfExists)
            {
                sql.Space();
                sql.Keyword("IF EXISTS");
            }
            sql.Space();
            sql.WriteTableName(drop.Name);
            if (!includeSignature || drop.ParameterTypes is null) return;
            sql._builder.Append('(');
            sql.WriteSeparated(drop.ParameterTypes, sql.WriteDataType);
            sql._builder.Append(')');
        }
    }

    private sealed class TSqlRenderer : AnsiRenderer
    {
        internal override bool SupportsTopLevelControlFlow => true;

        internal override void WriteProcedureDefinition(
            SqlGenerator sql,
            TableName name,
            IReadOnlyList<ProcedureParameter> parameters,
            ProceduralBlock body,
            bool replace)
        {
            sql.Keyword(replace ? "CREATE OR ALTER PROCEDURE" : "CREATE PROCEDURE");
            sql.Space();
            sql.WriteTableName(name);
            if (parameters.Count > 0)
            {
                sql.Space();
                sql.WriteSeparated(parameters, sql.WriteProcedureParameter);
            }
            sql.Space();
            sql.Keyword("AS BEGIN");
            sql.WriteProceduralBlockContents(body, declarationsInsideBody: true);
            sql.ProceduralLineBreak();
            sql.Keyword("END");
        }

        internal override void WriteAnonymousBlock(SqlGenerator sql, ProceduralBlock block)
        {
            sql.Keyword("BEGIN");
            sql.WriteProceduralBlockContents(block, declarationsInsideBody: true);
            sql.ProceduralLineBreak();
            sql.Keyword("END");
        }

        internal override void WriteParameter(SqlGenerator sql, ProcedureParameter parameter)
        {
            sql._builder.Append('@');
            sql.WriteIdentifier(parameter.Name);
            sql.Space();
            sql.WriteDataType(parameter.DataType);
            if (parameter.Default is not null)
            {
                sql._builder.Append(" = ");
                sql.WriteExpression(parameter.Default);
            }
            if (parameter.Mode == ProcedureParameterMode.In) return;
            sql.Space();
            sql.Keyword("OUTPUT");
        }

        internal override void WriteLocalVariable(SqlGenerator sql, LocalVariable variable)
        {
            sql.Keyword("DECLARE");
            sql.Space();
            sql._builder.Append('@');
            sql.WriteIdentifier(variable.Name);
            sql.Space();
            sql.WriteDataType(variable.DataType);
            WriteInitializer(sql, variable, "=");
        }

        internal override void WriteIf(SqlGenerator sql, ProceduralIfStatement statement)
        {
            sql.Keyword("IF");
            sql.Space();
            sql.WriteExpression(statement.Condition);
            sql.ProceduralLineBreak();
            sql.Keyword("BEGIN");
            sql.WriteProceduralStatements(statement.Then);
            sql.ProceduralLineBreak();
            sql.Keyword("END");
            if (statement.Else is not { Count: > 0 }) return;
            sql.ProceduralLineBreak();
            sql.Keyword("ELSE BEGIN");
            sql.WriteProceduralStatements(statement.Else);
            sql.ProceduralLineBreak();
            sql.Keyword("END");
        }

        internal override void WriteWhile(
            SqlGenerator sql,
            ProceduralWhileStatement statement,
            string label)
        {
            sql.Keyword("WHILE");
            sql.Space();
            sql.WriteExpression(statement.Condition);
            sql.ProceduralLineBreak();
            sql.Keyword("BEGIN");
            sql.WriteProceduralStatements(statement.Statements);
            sql.ProceduralLineBreak();
            sql.Keyword("END");
        }

        internal override void WriteLoopControl(SqlGenerator sql, bool isContinue, string label) =>
            sql.Keyword(isContinue ? "CONTINUE" : "BREAK");

        internal override void WriteCallProcedure(SqlGenerator sql, CallProcedureStatement call)
        {
            sql.Keyword("EXEC");
            sql.Space();
            sql.WriteTableName(call.Name);
            if (call.Arguments.Count > 0) sql.Space();
            sql.WriteSeparated(call.Arguments, sql.WriteProcedureArgument);
        }

        internal override void WriteArgument(SqlGenerator sql, ProcedureArgument argument)
        {
            if (argument.Name is not null)
            {
                sql._builder.Append('@');
                sql.WriteIdentifier(argument.Name);
                sql._builder.Append(" = ");
            }
            sql.WriteExpression(argument.Value);
            if (!argument.IsOutput) return;
            sql.Space();
            sql.Keyword("OUTPUT");
        }

        internal override void WriteLocalReference(SqlGenerator sql, LocalVariableExpression variable)
        {
            sql._builder.Append('@');
            sql.WriteIdentifier(variable.Name);
        }
    }

    private sealed class PostgreSqlRenderer : AnsiRenderer
    {
        internal override bool UsesDollarQuotedBodies => true;

        internal override void WriteProcedureDefinition(
            SqlGenerator sql,
            TableName name,
            IReadOnlyList<ProcedureParameter> parameters,
            ProceduralBlock body,
            bool replace)
        {
            sql.Keyword(replace ? "CREATE OR REPLACE PROCEDURE" : "CREATE PROCEDURE");
            sql.Space();
            sql.WriteTableName(name);
            sql.WriteProcedureParameters(parameters);
            sql.Space();
            sql.Keyword("LANGUAGE PLPGSQL AS");
            sql.Space();
            WriteDollarQuotedBlock(sql, body);
        }

        internal override void WriteAnonymousBlock(SqlGenerator sql, ProceduralBlock block)
        {
            sql.Keyword("DO LANGUAGE PLPGSQL");
            sql.Space();
            WriteDollarQuotedBlock(sql, block);
        }

        internal override void WriteLocalVariable(SqlGenerator sql, LocalVariable variable)
        {
            sql.WriteIdentifier(variable.Name);
            sql.Space();
            sql.WriteDataType(variable.DataType);
            WriteInitializer(sql, variable, ":=");
        }

        internal override void WriteWhile(
            SqlGenerator sql,
            ProceduralWhileStatement statement,
            string label)
        {
            sql.Keyword("WHILE");
            sql.Space();
            sql.WriteExpression(statement.Condition);
            sql.Space();
            sql.Keyword("LOOP");
            sql.WriteProceduralStatements(statement.Statements);
            sql.ProceduralLineBreak();
            sql.Keyword("END LOOP");
        }

        internal override void WriteLoopControl(SqlGenerator sql, bool isContinue, string label) =>
            sql.Keyword(isContinue ? "CONTINUE" : "EXIT");

        internal override void WriteDropProcedure(SqlGenerator sql, DropProcedureStatement drop) =>
            WriteStandardDrop(sql, drop, includeSignature: true);

        private static void WriteDollarQuotedBlock(SqlGenerator sql, ProceduralBlock body)
        {
            sql._builder.Append(ProceduralDollarQuotes.Tag);
            if (body.Variables.Count > 0)
            {
                sql.ProceduralLineBreak();
                sql.Keyword("DECLARE");
                sql.WriteLocalVariables(body.Variables);
            }
            sql.ProceduralLineBreak();
            sql.Keyword("BEGIN");
            sql.WriteProceduralStatements(body.Statements);
            sql.ProceduralLineBreak();
            sql.Keyword("END");
            sql.Space();
            sql._builder.Append(ProceduralDollarQuotes.Tag);
        }
    }

    private sealed class MySqlRenderer : AnsiRenderer
    {
        internal override bool SupportsReplaceProcedure => false;
        internal override bool SupportsParameterDefaults => false;
        internal override bool SupportsNamedArguments => false;

        internal override void WriteProcedureDefinition(
            SqlGenerator sql,
            TableName name,
            IReadOnlyList<ProcedureParameter> parameters,
            ProceduralBlock body,
            bool replace)
        {
            sql.Keyword("CREATE PROCEDURE");
            sql.Space();
            sql.WriteTableName(name);
            sql.WriteProcedureParameters(parameters);
            sql.Space();
            sql._builder.Append("cyqwel_body: ");
            sql.Keyword("BEGIN");
            sql.WriteProceduralBlockContents(body, declarationsInsideBody: true);
            sql.ProceduralLineBreak();
            sql.Keyword("END cyqwel_body");
        }

        internal override void WriteLocalVariable(SqlGenerator sql, LocalVariable variable)
        {
            sql.Keyword("DECLARE");
            sql.Space();
            sql.WriteIdentifier(variable.Name);
            sql.Space();
            sql.WriteDataType(variable.DataType);
            WriteInitializer(sql, variable, "DEFAULT");
        }

        internal override void WriteReturn(SqlGenerator sql) => sql.Keyword("LEAVE cyqwel_body");

        internal override void WriteArgument(SqlGenerator sql, ProcedureArgument argument)
        {
            if (argument.IsOutput && argument.Value is LocalVariableExpression variable)
            {
                sql._builder.Append('@');
                sql.WriteIdentifier(variable.Name);
                return;
            }
            sql.WriteExpression(argument.Value);
        }
    }

    private sealed class OracleRenderer : AnsiRenderer
    {
        internal override void WriteProcedureDefinition(
            SqlGenerator sql,
            TableName name,
            IReadOnlyList<ProcedureParameter> parameters,
            ProceduralBlock body,
            bool replace)
        {
            sql.Keyword(replace ? "CREATE OR REPLACE PROCEDURE" : "CREATE PROCEDURE");
            sql.Space();
            sql.WriteTableName(name);
            if (parameters.Count > 0) sql.WriteProcedureParameters(parameters);
            sql.Space();
            sql.Keyword("AS");
            sql.WriteLocalVariables(body.Variables);
            sql.ProceduralLineBreak();
            sql.Keyword("BEGIN");
            sql.WriteProceduralStatements(body.Statements);
            sql.ProceduralLineBreak();
            sql.Keyword("END");
        }

        internal override void WriteAnonymousBlock(SqlGenerator sql, ProceduralBlock block)
        {
            if (block.Variables.Count > 0)
            {
                sql.Keyword("DECLARE");
                sql.WriteLocalVariables(block.Variables);
                sql.ProceduralLineBreak();
            }
            sql.Keyword("BEGIN");
            sql.WriteProceduralStatements(block.Statements);
            sql.ProceduralLineBreak();
            sql.Keyword("END");
        }

        internal override void WriteParameter(SqlGenerator sql, ProcedureParameter parameter)
        {
            sql.WriteIdentifier(parameter.Name);
            sql.Space();
            sql.Keyword(parameter.Mode switch
            {
                ProcedureParameterMode.In => "IN",
                ProcedureParameterMode.Out => "OUT",
                ProcedureParameterMode.InOut => "IN OUT",
                _ => throw new ArgumentOutOfRangeException(),
            });
            sql.Space();
            sql.WriteDataType(parameter.DataType);
            sql.WriteProcedureParameterDefault(parameter, "DEFAULT");
        }

        internal override void WriteLocalVariable(SqlGenerator sql, LocalVariable variable)
        {
            sql.WriteIdentifier(variable.Name);
            sql.Space();
            sql.WriteDataType(variable.DataType);
            WriteInitializer(sql, variable, ":=");
        }

        internal override void WriteWhile(
            SqlGenerator sql,
            ProceduralWhileStatement statement,
            string label)
        {
            sql.Keyword("WHILE");
            sql.Space();
            sql.WriteExpression(statement.Condition);
            sql.Space();
            sql.Keyword("LOOP");
            sql.WriteProceduralStatements(statement.Statements);
            sql.ProceduralLineBreak();
            sql.Keyword("END LOOP");
        }

        internal override void WriteLoopControl(SqlGenerator sql, bool isContinue, string label) =>
            sql.Keyword(isContinue ? "CONTINUE" : "EXIT");
    }
}

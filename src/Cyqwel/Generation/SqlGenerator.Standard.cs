using Cyqwel.Ast;

namespace Cyqwel.Generation;

public sealed partial class SqlGenerator
{
    private void WriteValues(ValuesStatement values)
    {
        WriteCommonTableExpressions(values.CommonTableExpressions, values.IsRecursive);
        Keyword("VALUES");
        Space();
        WriteSeparated(values.Rows, row =>
        {
            _builder.Append('(');
            WriteSeparated(row, value => WriteExpression(value));
            _builder.Append(')');
        });
        WriteOrderBy(values.OrderBy);
        WriteLimitOffset(values.Limit, values.Offset, values.OrderBy);
    }

    private void WriteMerge(MergeStatement merge)
    {
        Keyword("MERGE INTO");
        Space();
        WriteNamedTable(merge.Target);
        ClauseBreak();
        Keyword("USING");
        Space();
        WriteTableSource(merge.Source);
        ClauseBreak();
        Keyword("ON");
        _builder.Append(" (");
        WriteExpression(merge.Condition);
        _builder.Append(')');

        foreach (var when in merge.WhenClauses)
        {
            ClauseBreak();
            Keyword("WHEN");
            Space();
            Keyword(when.MatchKind switch
            {
                MergeMatchKind.Matched => "MATCHED",
                MergeMatchKind.NotMatched => "NOT MATCHED",
                MergeMatchKind.NotMatchedBySource => "NOT MATCHED BY SOURCE",
                _ => throw new ArgumentOutOfRangeException(),
            });
            if (when.Condition is not null)
            {
                Space();
                Keyword("AND");
                Space();
                WriteExpression(when.Condition);
            }

            Space();
            Keyword("THEN");
            Space();
            WriteMergeAction(when.Action);
        }

        WriteReturning(merge.Returning, merge.ReturningInto);
    }

    private void WriteMergeAction(MergeAction action)
    {
        switch (action)
        {
            case MergeUpdateAction update:
                Keyword("UPDATE SET");
                Space();
                WriteSeparated(update.Assignments, assignment =>
                {
                    WriteExpression(assignment.Column);
                    _builder.Append(" = ");
                    WriteExpression(assignment.Value);
                });
                if (update.DeleteWhere is not null)
                {
                    Space();
                    Keyword("DELETE WHERE");
                    Space();
                    WriteExpression(update.DeleteWhere);
                }

                break;
            case MergeInsertAction insert:
                Keyword("INSERT");
                if (insert.Columns is { Count: > 0 })
                {
                    _builder.Append(" (");
                    WriteSeparated(insert.Columns, WriteIdentifier);
                    _builder.Append(')');
                }

                Space();
                Keyword("VALUES");
                _builder.Append(" (");
                WriteSeparated(insert.Values, value => WriteExpression(value));
                _builder.Append(')');
                break;
            case MergeDeleteAction:
                Keyword("DELETE");
                break;
            default:
                throw new NotSupportedException($"Unsupported MERGE action '{action.GetType().Name}'.");
        }
    }

    private void WriteCreateTable(CreateTableStatement create)
    {
        Keyword("CREATE");
        if (create.IsTemporary)
        {
            Space();
            Keyword("TEMPORARY");
        }

        Space();
        Keyword("TABLE");
        if (create.IfNotExists)
        {
            Space();
            Keyword("IF NOT EXISTS");
        }

        Space();
        WriteTableName(create.Name);
        if (create.Elements.Count > 0)
        {
            _builder.Append(" (");
            WriteSeparated(create.Elements, WriteTableElement);
            _builder.Append(')');
        }

        if (create.AsQuery is not null)
        {
            ClauseBreak();
            Keyword("AS");
            Space();
            WriteNode(create.AsQuery);
        }
    }

    private void WriteTableElement(TableElement element)
    {
        switch (element)
        {
            case ColumnDefinition column:
                WriteColumnDefinition(column);
                break;
            case TableConstraint constraint:
                WriteTableConstraint(constraint);
                break;
            default:
                throw new NotSupportedException($"Unsupported table element '{element.GetType().Name}'.");
        }
    }

    private void WriteColumnDefinition(ColumnDefinition column)
    {
        WriteIdentifier(column.Name);
        Space();
        WriteDataType(column.DataType);
        if (column.Identity != IdentityGeneration.None)
        {
            Space();
            Keyword(column.Identity == IdentityGeneration.Always
                ? "GENERATED ALWAYS AS IDENTITY"
                : "GENERATED BY DEFAULT AS IDENTITY");
        }

        if (column.Default is not null)
        {
            Space();
            Keyword("DEFAULT");
            Space();
            WriteExpression(column.Default);
        }

        if (column.GeneratedExpression is not null)
        {
            Space();
            Keyword("GENERATED ALWAYS AS");
            _builder.Append(" (");
            WriteExpression(column.GeneratedExpression);
            _builder.Append(')');
            Space();
            Keyword(column.GeneratedKind == GeneratedColumnKind.Stored ? "STORED" : "VIRTUAL");
        }

        if (column.Nullability != Nullability.Unspecified)
        {
            Space();
            Keyword(column.Nullability == Nullability.NotNull ? "NOT NULL" : "NULL");
        }

        if (column.IsPrimaryKey)
        {
            Space();
            Keyword("PRIMARY KEY");
        }

        if (column.IsUnique)
        {
            Space();
            Keyword("UNIQUE");
        }
    }

    private void WriteTableConstraint(TableConstraint constraint)
    {
        if (constraint.Name is not null)
        {
            Keyword("CONSTRAINT");
            Space();
            WriteIdentifier(constraint.Name);
            Space();
        }

        switch (constraint)
        {
            case PrimaryKeyConstraint primaryKey:
                Keyword("PRIMARY KEY");
                WriteIdentifierList(primaryKey.Columns);
                break;
            case UniqueConstraint unique:
                Keyword("UNIQUE");
                WriteIdentifierList(unique.Columns);
                break;
            case ForeignKeyConstraint foreignKey:
                Keyword("FOREIGN KEY");
                WriteIdentifierList(foreignKey.Columns);
                Space();
                Keyword("REFERENCES");
                Space();
                WriteTableName(foreignKey.ReferencedTable);
                WriteIdentifierList(foreignKey.ReferencedColumns);
                WriteReferentialAction("ON DELETE", foreignKey.OnDelete);
                WriteReferentialAction("ON UPDATE", foreignKey.OnUpdate);
                break;
            case CheckConstraint check:
                Keyword("CHECK");
                _builder.Append(" (");
                WriteExpression(check.Condition);
                _builder.Append(')');
                break;
            default:
                throw new NotSupportedException($"Unsupported table constraint '{constraint.GetType().Name}'.");
        }
    }

    private void WriteIdentifierList(IReadOnlyList<SqlIdentifier> identifiers)
    {
        _builder.Append(" (");
        WriteSeparated(identifiers, WriteIdentifier);
        _builder.Append(')');
    }

    private void WriteReferentialAction(string clause, ReferentialAction action)
    {
        if (action == ReferentialAction.Unspecified) return;
        Space();
        Keyword(clause);
        Space();
        Keyword(action switch
        {
            ReferentialAction.Cascade => "CASCADE",
            ReferentialAction.Restrict => "RESTRICT",
            ReferentialAction.SetNull => "SET NULL",
            ReferentialAction.SetDefault => "SET DEFAULT",
            ReferentialAction.NoAction => "NO ACTION",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        });
    }

    private void WriteAlterTable(AlterTableStatement alter)
    {
        Keyword("ALTER TABLE");
        Space();
        WriteTableName(alter.Name);
        Space();
        WriteSeparated(alter.Actions, WriteAlterTableAction);
    }

    private void WriteAlterTableAction(AlterTableAction action)
    {
        switch (action)
        {
            case AddColumnAction add:
                Keyword("ADD COLUMN");
                Space();
                WriteColumnDefinition(add.Column);
                break;
            case DropColumnAction drop:
                Keyword("DROP COLUMN");
                if (drop.IfExists)
                {
                    Space();
                    Keyword("IF EXISTS");
                }

                Space();
                WriteIdentifier(drop.Column);
                if (drop.Cascade)
                {
                    Space();
                    Keyword("CASCADE");
                }

                break;
            case AlterColumnAction alter:
                Keyword("ALTER COLUMN");
                Space();
                WriteIdentifier(alter.Column);
                if (alter.DataType is not null)
                {
                    Space();
                    Keyword("TYPE");
                    Space();
                    WriteDataType(alter.DataType);
                }
                else if (alter.DropDefault)
                {
                    Space();
                    Keyword("DROP DEFAULT");
                }
                else if (alter.Default is not null)
                {
                    Space();
                    Keyword("SET DEFAULT");
                    Space();
                    WriteExpression(alter.Default);
                }
                else if (alter.Nullability != Nullability.Unspecified)
                {
                    Space();
                    Keyword(alter.Nullability == Nullability.NotNull ? "SET NOT NULL" : "DROP NOT NULL");
                }

                break;
            case AddConstraintAction add:
                Keyword("ADD");
                Space();
                WriteTableConstraint(add.Constraint);
                break;
            case DropConstraintAction drop:
                Keyword("DROP CONSTRAINT");
                if (drop.IfExists)
                {
                    Space();
                    Keyword("IF EXISTS");
                }

                Space();
                WriteIdentifier(drop.Constraint);
                if (drop.Cascade)
                {
                    Space();
                    Keyword("CASCADE");
                }

                break;
            case RenameColumnAction rename:
                Keyword("RENAME COLUMN");
                Space();
                WriteIdentifier(rename.Column);
                Space();
                Keyword("TO");
                Space();
                WriteIdentifier(rename.NewName);
                break;
            case RenameTableAction rename:
                Keyword("RENAME TO");
                Space();
                WriteIdentifier(rename.NewName);
                break;
            default:
                throw new NotSupportedException($"Unsupported ALTER TABLE action '{action.GetType().Name}'.");
        }
    }

    private void WriteDrop(DropStatement drop)
    {
        Keyword("DROP");
        Space();
        Keyword(drop.Kind.ToString().ToUpperInvariant());
        if (drop.IfExists)
        {
            Space();
            Keyword("IF EXISTS");
        }

        Space();
        WriteSeparated(drop.Names, WriteTableName);
        if (drop.Cascade)
        {
            Space();
            Keyword("CASCADE");
        }
    }

    private void WriteTruncate(TruncateStatement truncate)
    {
        Keyword("TRUNCATE TABLE");
        Space();
        WriteSeparated(truncate.Tables, WriteTableName);
        if (truncate.RestartIdentity)
        {
            Space();
            Keyword("RESTART IDENTITY");
        }

        if (truncate.Cascade)
        {
            Space();
            Keyword("CASCADE");
        }
    }

    private void WriteCreateView(CreateViewStatement create)
    {
        Keyword("CREATE");
        if (create.OrReplace)
        {
            Space();
            Keyword("OR REPLACE");
        }

        if (create.IsTemporary)
        {
            Space();
            Keyword("TEMPORARY");
        }

        if (create.Security is not null)
        {
            if (!_dialect.UsesSqlSecurityForViews)
            {
                Unsupported($"{_dialect.Name} cannot represent SQL SECURITY on a view.");
            }

            Space();
            Keyword("SQL SECURITY");
            Space();
            Keyword(create.Security == ViewSecurity.Definer ? "DEFINER" : "INVOKER");
        }

        Space();
        Keyword("VIEW");
        Space();
        WriteTableName(create.Name);
        if (create.Columns is { Count: > 0 }) WriteIdentifierList(create.Columns);
        Space();
        Keyword("AS");
        Space();
        WriteNode(create.Query);
    }

    private void WriteCreateIndex(CreateIndexStatement create)
    {
        Keyword("CREATE");
        if (create.IsUnique)
        {
            Space();
            Keyword("UNIQUE");
        }

        Space();
        Keyword("INDEX");
        if (create.IfNotExists)
        {
            Space();
            Keyword("IF NOT EXISTS");
        }

        Space();
        WriteTableName(create.Name);
        Space();
        Keyword("ON");
        Space();
        WriteTableName(create.Table);
        _builder.Append(" (");
        WriteSeparated(create.Columns, column =>
        {
            WriteExpression(column.Expression);
            if (column.Direction != OrderDirection.Unspecified)
            {
                Space();
                Keyword(column.Direction == OrderDirection.Descending ? "DESC" : "ASC");
            }

            if (column.NullOrder != NullOrder.Unspecified)
            {
                Space();
                Keyword(column.NullOrder == NullOrder.First ? "NULLS FIRST" : "NULLS LAST");
            }
        });
        _builder.Append(')');
        if (create.Where is not null)
        {
            Space();
            Keyword("WHERE");
            Space();
            WriteExpression(create.Where);
        }
    }

    private void WriteCreateSequence(CreateSequenceStatement create)
    {
        Keyword("CREATE SEQUENCE");
        if (create.IfNotExists)
        {
            Space();
            Keyword("IF NOT EXISTS");
        }

        Space();
        WriteTableName(create.Name);
        WriteSequenceOptions(create.Options);
    }

    private void WriteAlterSequence(AlterSequenceStatement alter)
    {
        Keyword("ALTER SEQUENCE");
        Space();
        WriteTableName(alter.Name);
        WriteSequenceOptions(alter.Options);
    }

    private void WriteSequenceOptions(SequenceOptions options)
    {
        WriteSequenceOption("START WITH", options.StartWith);
        WriteSequenceOption("INCREMENT BY", options.IncrementBy);
        WriteSequenceOption("MINVALUE", options.MinimumValue);
        WriteSequenceOption("MAXVALUE", options.MaximumValue);
        WriteSequenceOption("CACHE", options.Cache);
        if (options.Cycle.HasValue)
        {
            Space();
            Keyword(options.Cycle.Value ? "CYCLE" : "NO CYCLE");
        }
    }

    private void WriteSequenceOption(string name, SqlExpression? value)
    {
        if (value is null) return;
        Space();
        Keyword(name);
        Space();
        WriteExpression(value);
    }

    private void WriteWindowDefinition(WindowDefinition window)
    {
        WriteIdentifier(window.Name);
        Space();
        Keyword("AS");
        _builder.Append(" (");
        var hasClause = false;
        if (window.BaseWindow is not null)
        {
            WriteIdentifier(window.BaseWindow);
            hasClause = true;
        }

        if (window.PartitionBy is { Count: > 0 })
        {
            if (hasClause) Space();
            Keyword("PARTITION BY");
            Space();
            WriteSeparated(window.PartitionBy, value => WriteExpression(value));
            hasClause = true;
        }

        if (window.OrderBy is { Count: > 0 })
        {
            if (hasClause) Space();
            Keyword("ORDER BY");
            Space();
            WriteOrderByItems(window.OrderBy);
            hasClause = true;
        }

        if (window.Frame is not null)
        {
            if (hasClause) Space();
            WriteWindowFrame(window.Frame);
        }

        _builder.Append(')');
    }

    private void WriteWindowFrame(WindowFrame frame)
    {
        Keyword(frame.Unit switch
        {
            WindowFrameUnit.Rows => "ROWS",
            WindowFrameUnit.Range => "RANGE",
            WindowFrameUnit.Groups => "GROUPS",
            _ => throw new ArgumentOutOfRangeException(),
        });
        Space();
        if (frame.End is null)
        {
            WriteWindowFrameBound(frame.Start);
            return;
        }

        Keyword("BETWEEN");
        Space();
        WriteWindowFrameBound(frame.Start);
        Space();
        Keyword("AND");
        Space();
        WriteWindowFrameBound(frame.End);
    }

    private void WriteWindowFrameBound(WindowFrameBound bound)
    {
        if (bound.Offset is not null)
        {
            WriteExpression(bound.Offset);
            Space();
        }

        Keyword(bound.Kind switch
        {
            WindowFrameBoundKind.UnboundedPreceding => "UNBOUNDED PRECEDING",
            WindowFrameBoundKind.Preceding => "PRECEDING",
            WindowFrameBoundKind.CurrentRow => "CURRENT ROW",
            WindowFrameBoundKind.Following => "FOLLOWING",
            WindowFrameBoundKind.UnboundedFollowing => "UNBOUNDED FOLLOWING",
            _ => throw new ArgumentOutOfRangeException(),
        });
    }

    private void WriteConnectBy(ConnectByClause connectBy)
    {
        if (connectBy.StartWith is not null)
        {
            ClauseBreak();
            Keyword("START WITH");
            Space();
            WriteExpression(connectBy.StartWith);
        }

        ClauseBreak();
        Keyword("CONNECT BY");
        if (connectBy.NoCycle)
        {
            Space();
            Keyword("NOCYCLE");
        }

        Space();
        WriteExpression(connectBy.Condition);
    }
}

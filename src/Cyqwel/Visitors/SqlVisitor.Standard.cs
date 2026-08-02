using Cyqwel.Ast;

namespace Cyqwel.Visitors;

public abstract partial class SqlVisitor
{
    protected virtual void VisitValues(ValuesStatement node) => DefaultVisit(node);
    protected virtual void VisitMerge(MergeStatement node) => DefaultVisit(node);
    protected virtual void VisitCreateTable(CreateTableStatement node) => DefaultVisit(node);
    protected virtual void VisitAlterTable(AlterTableStatement node) => DefaultVisit(node);
    protected virtual void VisitDrop(DropStatement node) => DefaultVisit(node);
    protected virtual void VisitTruncate(TruncateStatement node) => DefaultVisit(node);
    protected virtual void VisitCreateView(CreateViewStatement node) => DefaultVisit(node);
    protected virtual void VisitCreateIndex(CreateIndexStatement node) => DefaultVisit(node);
    protected virtual void VisitCreateSequence(CreateSequenceStatement node) => DefaultVisit(node);
    protected virtual void VisitAlterSequence(AlterSequenceStatement node) => DefaultVisit(node);
    protected virtual void VisitBooleanTest(BooleanTestExpression node) => DefaultVisit(node);
    protected virtual void VisitDistinctFrom(DistinctFromExpression node) => DefaultVisit(node);
    protected virtual void VisitRow(RowExpression node) => DefaultVisit(node);
    protected virtual void VisitDefault(DefaultExpression node) => DefaultVisit(node);
    protected virtual void VisitCollate(CollateExpression node) => DefaultVisit(node);
    protected virtual void VisitExtract(ExtractExpression node) => DefaultVisit(node);
    protected virtual void VisitInterval(IntervalExpression node) => DefaultVisit(node);
    protected virtual void VisitSequenceValue(SequenceValueExpression node) => DefaultVisit(node);
    protected virtual void VisitTryCast(TryCastExpression node) => DefaultVisit(node);
    protected virtual void VisitWindowDefinition(WindowDefinition node) => DefaultVisit(node);
    protected virtual void VisitWindowFrame(WindowFrame node) => DefaultVisit(node);
    protected virtual void VisitWindowFrameBound(WindowFrameBound node) => DefaultVisit(node);
    protected virtual void VisitConnectBy(ConnectByClause node) => DefaultVisit(node);
    protected virtual void VisitMergeWhen(MergeWhenClause node) => DefaultVisit(node);
    protected virtual void VisitMergeUpdate(MergeUpdateAction node) => DefaultVisit(node);
    protected virtual void VisitMergeInsert(MergeInsertAction node) => DefaultVisit(node);
    protected virtual void VisitMergeDelete(MergeDeleteAction node) => DefaultVisit(node);
    protected virtual void VisitColumnDefinition(ColumnDefinition node) => DefaultVisit(node);
    protected virtual void VisitPrimaryKeyConstraint(PrimaryKeyConstraint node) => DefaultVisit(node);
    protected virtual void VisitUniqueConstraint(UniqueConstraint node) => DefaultVisit(node);
    protected virtual void VisitForeignKeyConstraint(ForeignKeyConstraint node) => DefaultVisit(node);
    protected virtual void VisitCheckConstraint(CheckConstraint node) => DefaultVisit(node);
    protected virtual void VisitAddColumn(AddColumnAction node) => DefaultVisit(node);
    protected virtual void VisitDropColumn(DropColumnAction node) => DefaultVisit(node);
    protected virtual void VisitAlterColumn(AlterColumnAction node) => DefaultVisit(node);
    protected virtual void VisitAddConstraint(AddConstraintAction node) => DefaultVisit(node);
    protected virtual void VisitDropConstraint(DropConstraintAction node) => DefaultVisit(node);
    protected virtual void VisitRenameColumn(RenameColumnAction node) => DefaultVisit(node);
    protected virtual void VisitRenameTable(RenameTableAction node) => DefaultVisit(node);
    protected virtual void VisitIndexColumn(IndexColumn node) => DefaultVisit(node);
    protected virtual void VisitSequenceOptions(SequenceOptions node) => DefaultVisit(node);
}

namespace Cyqwel.Ast;

public sealed record ParenthesizedTable(TableSource Source, SqlIdentifier? Alias = null) : TableSource;

public sealed record DerivedMutationTable(
    MergeStatement Statement,
    SqlIdentifier Alias,
    IReadOnlyList<SqlIdentifier>? Columns = null) : TableSource;

public sealed record TableFunction(
    FunctionCallExpression Function,
    SqlIdentifier? Alias = null,
    IReadOnlyList<SqlIdentifier>? Columns = null) : TableSource;

public sealed record OpenJsonTable(
    SqlExpression Expression,
    SqlExpression? Path = null,
    IReadOnlyList<OpenJsonColumn>? Schema = null,
    SqlIdentifier? Alias = null) : TableSource;

public sealed record OpenJsonColumn(
    SqlIdentifier Name,
    SqlDataType DataType,
    LiteralExpression? Path = null,
    bool AsJson = false) : SqlNode;

public sealed record PivotTable(
    TableSource Source,
    FunctionCallExpression Aggregate,
    ColumnExpression Column,
    IReadOnlyList<SqlIdentifier> Values,
    SqlIdentifier? Alias = null) : TableSource;

public sealed record UnpivotTable(
    TableSource Source,
    SqlIdentifier ValueColumn,
    SqlIdentifier NameColumn,
    IReadOnlyList<SqlIdentifier> Columns,
    SqlIdentifier? Alias = null) : TableSource;

public enum TSqlTableHintKind
{
    NoLock, RowLock, UpdLock, HoldLock, ReadPast, NoWait,
    ReadCommitted, ReadCommittedLock, ReadUncommitted, RepeatableRead,
    Serializable, TabLock, TabLockX, PagLock, XLock, Snapshot, Index,
}

public sealed record TSqlTableHint(
    TSqlTableHintKind Kind,
    IReadOnlyList<TSqlIndexReference>? Indexes = null) : SqlNode
{
    public static TSqlTableHint NoLock { get; } = new(TSqlTableHintKind.NoLock);
    public static TSqlTableHint RowLock { get; } = new(TSqlTableHintKind.RowLock);
    public static TSqlTableHint UpdLock { get; } = new(TSqlTableHintKind.UpdLock);
    public static TSqlTableHint HoldLock { get; } = new(TSqlTableHintKind.HoldLock);
    public static TSqlTableHint ReadPast { get; } = new(TSqlTableHintKind.ReadPast);
    public static TSqlTableHint NoWait { get; } = new(TSqlTableHintKind.NoWait);
    public static TSqlTableHint ReadCommitted { get; } = new(TSqlTableHintKind.ReadCommitted);
    public static TSqlTableHint ReadCommittedLock { get; } = new(TSqlTableHintKind.ReadCommittedLock);
    public static TSqlTableHint ReadUncommitted { get; } = new(TSqlTableHintKind.ReadUncommitted);
    public static TSqlTableHint RepeatableRead { get; } = new(TSqlTableHintKind.RepeatableRead);
    public static TSqlTableHint Serializable { get; } = new(TSqlTableHintKind.Serializable);
    public static TSqlTableHint TabLock { get; } = new(TSqlTableHintKind.TabLock);
    public static TSqlTableHint TabLockX { get; } = new(TSqlTableHintKind.TabLockX);
    public static TSqlTableHint PagLock { get; } = new(TSqlTableHintKind.PagLock);
    public static TSqlTableHint XLock { get; } = new(TSqlTableHintKind.XLock);
    public static TSqlTableHint Snapshot { get; } = new(TSqlTableHintKind.Snapshot);
}

public sealed record TSqlIndexReference(SqlIdentifier? Name = null, int? Id = null) : SqlNode
{
    internal bool IsValid => Name is not null ? Id is null : Id is >= 0;
}

public enum TSqlTableSampleUnit { Unspecified, Percent, Rows }

public sealed record TSqlTableSample(
    LiteralExpression Amount,
    TSqlTableSampleUnit Unit = TSqlTableSampleUnit.Unspecified,
    bool IsSystem = false) : SqlNode
{
    internal bool IsValid => Enum.IsDefined(Unit)
        && Amount.Value is sbyte or byte or short or ushort or int or uint or long or ulong or decimal
        && Convert.ToDecimal(Amount.Value, System.Globalization.CultureInfo.InvariantCulture) >= 0
        && (Unit != TSqlTableSampleUnit.Percent
            || Convert.ToDecimal(Amount.Value, System.Globalization.CultureInfo.InvariantCulture) <= 100);
}

public enum TSqlJoinHint { Hash, Loop, Merge }

public enum TSqlQueryOptionKind
{
    Recompile, MaxDop, MaxRecursion, OptimizeForUnknown, ForceOrder, Fast,
}

public sealed record TSqlQueryOption(TSqlQueryOptionKind Kind, int? Value = null) : SqlNode
{
    internal bool IsValid => Kind switch
    {
        TSqlQueryOptionKind.MaxDop or TSqlQueryOptionKind.MaxRecursion => Value is >= 0 and <= 32767,
        TSqlQueryOptionKind.Fast => Value is >= 0,
        TSqlQueryOptionKind.Recompile or TSqlQueryOptionKind.OptimizeForUnknown or TSqlQueryOptionKind.ForceOrder => Value is null,
        _ => false,
    };
}

public enum TSqlResultFormatKind { Json, Xml }

public enum TSqlResultFormatMode { Auto, Path, Raw }

public sealed record TSqlResultFormat(
    TSqlResultFormatKind Kind,
    TSqlResultFormatMode Mode,
    LiteralExpression? ElementName = null,
    bool HasRoot = false,
    LiteralExpression? Root = null,
    bool IncludeNullValues = false,
    bool WithoutArrayWrapper = false,
    bool Type = false) : SqlNode
{
    internal bool IsValid => (HasRoot || Root is null)
        && (Root is null || Root.Value is string)
        && (ElementName is null || ElementName.Value is string)
        && Kind switch
        {
            TSqlResultFormatKind.Json => Mode is TSqlResultFormatMode.Auto or TSqlResultFormatMode.Path
                && ElementName is null && !Type && !(HasRoot && WithoutArrayWrapper),
            TSqlResultFormatKind.Xml => Mode is TSqlResultFormatMode.Auto or TSqlResultFormatMode.Path or TSqlResultFormatMode.Raw
                && (Mode != TSqlResultFormatMode.Auto || ElementName is null)
                && !IncludeNullValues && !WithoutArrayWrapper,
            _ => false,
        };
}

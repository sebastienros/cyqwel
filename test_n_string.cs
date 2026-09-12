var sql = "SELECT N'ação'";
var parsed = Cyqwel.SqlDialects.TSql.Parse(sql);
var output = parsed.ToSql(Cyqwel.SqlDialects.TSql);
System.Console.WriteLine($"Input:  {sql}");
System.Console.WriteLine($"Output: {output}");

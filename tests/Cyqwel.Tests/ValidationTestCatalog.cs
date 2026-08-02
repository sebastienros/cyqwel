using Cyqwel.Validation;

namespace Cyqwel.Tests;

internal static class ValidationTestCatalog
{
    public static SqlSchemaCatalog Create(bool withRelationship = false) =>
        new(
            new SqlTableSchema(
                "users",
                [
                    new("id", "integer", IsPrimaryKey: true),
                    new("name", "varchar"),
                    new("email", "varchar"),
                    new("age", "integer"),
                    new("active", "boolean"),
                ],
                PrimaryKey: ["id"]),
            new SqlTableSchema(
                "orders",
                [
                    new("id", "integer", IsPrimaryKey: true),
                    new(
                        "user_id",
                        "integer",
                        References: withRelationship
                            ? new SqlColumnReference("users", "id")
                            : null),
                    new("total", "decimal"),
                    new("description", "varchar"),
                ],
                PrimaryKey: ["id"]));
}

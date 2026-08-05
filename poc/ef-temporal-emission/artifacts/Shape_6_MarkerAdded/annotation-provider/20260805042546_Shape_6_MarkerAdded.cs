using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norse.Spike.Migrations;

/// <inheritdoc />
public partial class _20260805042546_Shape_6_MarkerAdded : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterTable(
            name: "widgets")
            .Annotation("Norse:Temporal", true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterTable(
            name: "widgets")
            .OldAnnotation("Norse:Temporal", true);
    }
}

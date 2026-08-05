using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norse.Spike.Migrations;

/// <inheritdoc />
public partial class _20260805042248_Shape_3_RenameColumn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "Name",
            table: "widgets",
            newName: "display_name");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "display_name",
            table: "widgets",
            newName: "Name");
    }
}

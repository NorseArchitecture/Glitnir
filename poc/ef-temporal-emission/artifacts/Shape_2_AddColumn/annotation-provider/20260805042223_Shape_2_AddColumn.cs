using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norse.Spike.Migrations;

/// <inheritdoc />
public partial class _20260805042223_Shape_2_AddColumn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "description",
            table: "widgets",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "description",
            table: "widgets");
    }
}

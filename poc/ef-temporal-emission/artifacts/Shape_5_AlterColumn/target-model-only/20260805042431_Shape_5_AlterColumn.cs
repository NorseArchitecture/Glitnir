using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norse.Spike.Migrations;

/// <inheritdoc />
public partial class _20260805042431_Shape_5_AlterColumn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "widgets",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "widgets",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(128)",
            oldMaxLength: 128);
    }
}

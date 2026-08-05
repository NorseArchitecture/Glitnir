using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Norse.Spike.Migrations;

/// <inheritdoc />
public partial class _20260805045349_Shape_1_Create : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "widgets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_widgets", x => x.Id);
            })
            .Annotation("Norse:Temporal", true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "widgets");
    }
}

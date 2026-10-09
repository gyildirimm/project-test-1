using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalRows = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    InvalidCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateInFileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AlreadyExistsCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerImportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerImportId = table.Column<int>(type: "INTEGER", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    RawName = table.Column<string>(type: "TEXT", nullable: true),
                    RawEmail = table.Column<string>(type: "TEXT", nullable: true),
                    RawPhone = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    DuplicateOfLineNumber = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerImportRows_CustomerImports_CustomerImportId",
                        column: x => x.CustomerImportId,
                        principalTable: "CustomerImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerImportRows_CustomerImportId_LineNumber",
                table: "CustomerImportRows",
                columns: new[] { "CustomerImportId", "LineNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerImportRows");

            migrationBuilder.DropTable(
                name: "CustomerImports");
        }
    }
}

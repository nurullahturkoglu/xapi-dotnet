using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xapi.Lrs.Sqlite.Migrations
{
    /// <inheritdoc />
    internal partial class AddAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "xapi_attachment_content",
                columns: table => new
                {
                    sha2 = table.Column<string>(type: "TEXT", nullable: false),
                    content_type = table.Column<string>(type: "TEXT", nullable: false),
                    length = table.Column<long>(type: "INTEGER", nullable: false),
                    content = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_attachment_content", x => x.sha2);
                });

            migrationBuilder.CreateTable(
                name: "xapi_statement_attachment",
                columns: table => new
                {
                    statement_pk = table.Column<Guid>(type: "TEXT", nullable: false),
                    sha2 = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_statement_attachment", x => new { x.statement_pk, x.sha2 });
                    table.ForeignKey(
                        name: "fk_xapi_statement_attachment_content",
                        column: x => x.sha2,
                        principalTable: "xapi_attachment_content",
                        principalColumn: "sha2",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_xapi_statement_attachment_statement",
                        column: x => x.statement_pk,
                        principalTable: "xapi_statement",
                        principalColumn: "pk",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_xapi_statement_attachment_sha2",
                table: "xapi_statement_attachment",
                column: "sha2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "xapi_statement_attachment");

            migrationBuilder.DropTable(
                name: "xapi_attachment_content");
        }
    }
}

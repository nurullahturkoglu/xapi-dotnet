using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xapi.Lrs.Sqlite.Migrations
{
    /// <inheritdoc />
    internal partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "xapi_activity",
                columns: table => new
                {
                    iri = table.Column<string>(type: "TEXT", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_activity", x => x.iri);
                });

            migrationBuilder.CreateTable(
                name: "xapi_actor",
                columns: table => new
                {
                    ifi = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<short>(type: "INTEGER", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_actor", x => new { x.ifi, x.type });
                });

            migrationBuilder.CreateTable(
                name: "xapi_credential",
                columns: table => new
                {
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    secret_hash = table.Column<string>(type: "TEXT", nullable: false),
                    scopes = table.Column<string>(type: "TEXT", nullable: false),
                    authority_payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_credential", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "xapi_statement",
                columns: table => new
                {
                    pk = table.Column<Guid>(type: "TEXT", nullable: false),
                    statement_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    stored = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    verb_iri = table.Column<string>(type: "TEXT", nullable: false),
                    registration = table.Column<Guid>(type: "TEXT", nullable: true),
                    ref_statement_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    is_voided = table.Column<bool>(type: "INTEGER", nullable: false),
                    version = table.Column<string>(type: "TEXT", nullable: false),
                    authority_key = table.Column<string>(type: "TEXT", nullable: true),
                    payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_statement", x => x.pk);
                });

            migrationBuilder.CreateTable(
                name: "xapi_statement_activity",
                columns: table => new
                {
                    statement_pk = table.Column<Guid>(type: "TEXT", nullable: false),
                    usage = table.Column<short>(type: "INTEGER", nullable: false),
                    activity_iri = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_statement_activity", x => new { x.statement_pk, x.usage, x.activity_iri });
                    table.ForeignKey(
                        name: "fk_xapi_statement_activity_statement",
                        column: x => x.statement_pk,
                        principalTable: "xapi_statement",
                        principalColumn: "pk",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "xapi_statement_actor",
                columns: table => new
                {
                    statement_pk = table.Column<Guid>(type: "TEXT", nullable: false),
                    usage = table.Column<short>(type: "INTEGER", nullable: false),
                    actor_ifi = table.Column<string>(type: "TEXT", nullable: false),
                    actor_type = table.Column<short>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_statement_actor", x => new { x.statement_pk, x.usage, x.actor_ifi, x.actor_type });
                    table.ForeignKey(
                        name: "fk_xapi_statement_actor_statement",
                        column: x => x.statement_pk,
                        principalTable: "xapi_statement",
                        principalColumn: "pk",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "xapi_statement_ref",
                columns: table => new
                {
                    ancestor_pk = table.Column<Guid>(type: "TEXT", nullable: false),
                    descendant_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_statement_ref", x => new { x.ancestor_pk, x.descendant_id });
                    table.ForeignKey(
                        name: "fk_xapi_statement_ref_statement",
                        column: x => x.ancestor_pk,
                        principalTable: "xapi_statement",
                        principalColumn: "pk",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_xapi_statement_ref_statement_id",
                table: "xapi_statement",
                column: "ref_statement_id");

            migrationBuilder.CreateIndex(
                name: "ix_xapi_statement_registration_pk",
                table: "xapi_statement",
                columns: new[] { "registration", "pk" });

            migrationBuilder.CreateIndex(
                name: "ix_xapi_statement_verb_iri_pk",
                table: "xapi_statement",
                columns: new[] { "verb_iri", "pk" });

            migrationBuilder.CreateIndex(
                name: "ux_xapi_statement_statement_id",
                table: "xapi_statement",
                column: "statement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_xapi_statement_activity_iri_usage_pk",
                table: "xapi_statement_activity",
                columns: new[] { "activity_iri", "usage", "statement_pk" });

            migrationBuilder.CreateIndex(
                name: "ix_xapi_statement_actor_ifi_usage_pk",
                table: "xapi_statement_actor",
                columns: new[] { "actor_ifi", "usage", "statement_pk" });

            migrationBuilder.CreateIndex(
                name: "ix_xapi_statement_ref_descendant_id",
                table: "xapi_statement_ref",
                column: "descendant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "xapi_activity");

            migrationBuilder.DropTable(
                name: "xapi_actor");

            migrationBuilder.DropTable(
                name: "xapi_credential");

            migrationBuilder.DropTable(
                name: "xapi_statement_activity");

            migrationBuilder.DropTable(
                name: "xapi_statement_actor");

            migrationBuilder.DropTable(
                name: "xapi_statement_ref");

            migrationBuilder.DropTable(
                name: "xapi_statement");
        }
    }
}

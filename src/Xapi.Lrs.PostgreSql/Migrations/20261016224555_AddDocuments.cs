using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xapi.Lrs.PostgreSql.Migrations
{
    /// <inheritdoc />
    internal partial class AddDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "xapi_activity_profile",
                columns: table => new
                {
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    activity_iri = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    etag = table.Column<string>(type: "text", nullable: false),
                    updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_activity_profile", x => new { x.activity_iri, x.profile_id });
                });

            migrationBuilder.CreateTable(
                name: "xapi_agent_profile",
                columns: table => new
                {
                    profile_id = table.Column<string>(type: "text", nullable: false),
                    agent_ifi = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    etag = table.Column<string>(type: "text", nullable: false),
                    updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_agent_profile", x => new { x.agent_ifi, x.profile_id });
                });

            migrationBuilder.CreateTable(
                name: "xapi_state_document",
                columns: table => new
                {
                    state_id = table.Column<string>(type: "text", nullable: false),
                    activity_iri = table.Column<string>(type: "text", nullable: false),
                    agent_ifi = table.Column<string>(type: "text", nullable: false),
                    registration = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    etag = table.Column<string>(type: "text", nullable: false),
                    updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xapi_state_document", x => new { x.activity_iri, x.agent_ifi, x.registration, x.state_id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "xapi_activity_profile");

            migrationBuilder.DropTable(
                name: "xapi_agent_profile");

            migrationBuilder.DropTable(
                name: "xapi_state_document");
        }
    }
}

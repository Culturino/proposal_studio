using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProposalStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_invites", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_invites_token",
                table: "user_invites",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_invites");
        }
    }
}

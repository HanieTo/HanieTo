using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HanieTo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChatPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChatId = table.Column<string>(type: "TEXT", nullable: false),
                    Language = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatPreferences_ChatId",
                table: "ChatPreferences",
                column: "ChatId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatPreferences");
        }
    }
}

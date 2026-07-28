using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HanieTo.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelAccessTokenFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessToken",
                table: "Channels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccessTokenSecret",
                table: "Channels",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessToken",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "AccessTokenSecret",
                table: "Channels");
        }
    }
}

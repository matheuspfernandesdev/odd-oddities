using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OddOddities.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoColumnsToPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "VideoBytes",
                table: "Posts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoDurationSeconds",
                table: "Posts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoObjectKey",
                table: "Posts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoBytes",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "VideoDurationSeconds",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "VideoObjectKey",
                table: "Posts");
        }
    }
}

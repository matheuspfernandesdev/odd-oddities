using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OddOddities.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMaxCaptionContentLengthSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Key",
                keyValue: "MAX_CAPTION_CONTENT_LENGTH",
                column: "Value",
                value: "1600");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Key",
                keyValue: "MAX_CAPTION_CONTENT_LENGTH",
                column: "Value",
                value: "800");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OddOddities.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeGenerationAttemptPostIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GenerationAttempts_Posts_PostId",
                table: "GenerationAttempts");

            migrationBuilder.AlterColumn<long>(
                name: "PostId",
                table: "GenerationAttempts",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddForeignKey(
                name: "FK_GenerationAttempts_Posts_PostId",
                table: "GenerationAttempts",
                column: "PostId",
                principalTable: "Posts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GenerationAttempts_Posts_PostId",
                table: "GenerationAttempts");

            migrationBuilder.AlterColumn<long>(
                name: "PostId",
                table: "GenerationAttempts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GenerationAttempts_Posts_PostId",
                table: "GenerationAttempts",
                column: "PostId",
                principalTable: "Posts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

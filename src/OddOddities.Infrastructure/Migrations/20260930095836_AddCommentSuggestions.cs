using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OddOddities.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SourceCommentSuggestionId",
                table: "Posts",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommentSuggestions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommentId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    MediaId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AuthorUsername = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CommentText = table.Column<string>(type: "text", nullable: false),
                    Classification = table.Column<int>(type: "integer", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentSuggestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_SourceCommentSuggestionId",
                table: "Posts",
                column: "SourceCommentSuggestionId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentSuggestions_CommentId",
                table: "CommentSuggestions",
                column: "CommentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_CommentSuggestions_SourceCommentSuggestionId",
                table: "Posts",
                column: "SourceCommentSuggestionId",
                principalTable: "CommentSuggestions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_CommentSuggestions_SourceCommentSuggestionId",
                table: "Posts");

            migrationBuilder.DropTable(
                name: "CommentSuggestions");

            migrationBuilder.DropIndex(
                name: "IX_Posts_SourceCommentSuggestionId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "SourceCommentSuggestionId",
                table: "Posts");
        }
    }
}

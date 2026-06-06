using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreLearningSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkQuizToLesson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QuizId",
                table: "Lessons",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_QuizId",
                table: "Lessons",
                column: "QuizId");

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_Quizzes_QuizId",
                table: "Lessons",
                column: "QuizId",
                principalTable: "Quizzes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_Quizzes_QuizId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_QuizId",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "QuizId",
                table: "Lessons");
        }
    }
}
